using StellarModManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StellarModManager.Services;

internal sealed class ModProfileStore
{
    private sealed class LegacyProfiles
    {
        public string ActiveProfile { get; set; } = "Default";
        public List<ModProfile> Profiles { get; set; } = new();
    }

    private readonly string appDirectory;
    private readonly string gamePath;
    private readonly string profilesRoot;
    private readonly string storePath;
    private readonly string statePath;
    private readonly string journalPath;
    private readonly ModPackageCache packages;

    internal ModProfileStore(string appDirectory, string gamePath, ModPackageCache packages)
    {
        this.appDirectory = appDirectory;
        this.gamePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gamePath));
        this.packages = packages;
        profilesRoot = AppPaths.ProfilesRoot;
        var key = ProfileFiles.PathComparer == StringComparer.OrdinalIgnoreCase
            ? this.gamePath.ToUpperInvariant()
            : this.gamePath;
        storePath = Path.Combine(profilesRoot, ModPackageCache.DirectoryKey(key));
        statePath = Path.Combine(storePath, "state.json");
        journalPath = Path.Combine(storePath, "pending.json");
    }

    private FileStream Lock()
    {
        Directory.CreateDirectory(storePath);
        return new FileStream(Path.Combine(storePath, "state.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    internal GameModProfiles Open(string? legacyGamePath)
    {
        using var fileLock = Lock();
        if (File.Exists(statePath))
        {
            var existing = Load();
            PruneSnapshots(existing);
            return existing;
        }

        var legacyPath = Path.Combine(appDirectory, "Data", "modprofiles.json");
        var newPath = AppPaths.ModProfilesPath;
        bool useLegacy = (string.IsNullOrWhiteSpace(legacyGamePath) ||
            ProfileFiles.PathComparer.Equals(gamePath, Path.TrimEndingDirectorySeparator(Path.GetFullPath(legacyGamePath)))) &&
            ClaimLegacyData();
        var legacy = useLegacy && File.Exists(legacyPath) ? ProfileFiles.ReadJson<LegacyProfiles>(legacyPath) : new();
        if (legacy.Profiles.Count == 0)
            legacy.Profiles.Add(new ModProfile { Name = "Default" });

        var active = legacy.Profiles.FirstOrDefault(p => p.Name == legacy.ActiveProfile) ?? legacy.Profiles[0];
        active.DeployedModIds = new();
        var state = new GameModProfiles
        {
            Profiles = legacy.Profiles,
            ActiveProfileId = active.Id,
            InstalledMods = useLegacy ? ReadLegacyInstalledMods() : new()
        };
        ProfileFiles.WriteJson(statePath, state);
        return state;
    }

    private bool ClaimLegacyData()
    {
        using var migrationLock = new FileStream(Path.Combine(profilesRoot, "legacy.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        var ownerFile = Path.Combine(profilesRoot, "legacy-installation.json");
        if (File.Exists(ownerFile))
            return ProfileFiles.PathComparer.Equals(ProfileFiles.ReadJson<string>(ownerFile), gamePath);

        ProfileFiles.WriteJson(ownerFile, gamePath);
        return true;
    }

    private List<ProfileMod> ReadLegacyInstalledMods()
    {
        var installed = new List<ProfileMod>();
        var library = Path.Combine(appDirectory, "Library");
        if (!Directory.Exists(library))
            return installed;

        foreach (var folder in Directory.EnumerateDirectories(library))
        {
            var manifest = Path.Combine(folder, ".deployment.json");
            if (!File.Exists(manifest) || ModPackageCache.ReadPackage(folder) is not ModPackage package)
                continue;

            var files = ProfileFiles.ReadJson<List<string>>(manifest)
                .Select(ProfileFiles.Normalize).Distinct(ProfileFiles.PathComparer).ToList();
            if (files.Count > 0)
                installed.Add(new ProfileMod(package.Mod, files));
        }
        return LiveMods(installed);
    }

    private GameModProfiles Load(Guid? expectedProfile = null)
    {
        var state = ProfileFiles.ReadJson<GameModProfiles>(statePath);
        ProfileFileTransaction.Recover(gamePath, journalPath, state.LastOperationId);
        if (expectedProfile.HasValue && expectedProfile != state.ActiveProfileId)
            throw new InvalidOperationException("The active profile changed in another window. Reload the game folder.");
        state.InstalledMods = LiveMods(state.InstalledMods);
        return state;
    }

    internal FileStream LockForLaunch(Guid expectedProfile)
    {
        var fileLock = Lock();
        try
        {
            Load(expectedProfile);
            return fileLock;
        }
        catch
        {
            fileLock.Dispose();
            throw;
        }
    }

    private List<ProfileMod> LiveMods(IEnumerable<ProfileMod> mods) =>
        mods.Select(mod => mod with
            {
                Files = mod.Files.Where(path => File.Exists(ProfileFiles.Resolve(gamePath, path))).ToList()
            })
            .Where(mod => mod.Files.Count > 0)
            .ToList();

    internal GameModProfiles Edit(Guid expectedProfile, Action<GameModProfiles> edit)
    {
        using var fileLock = Lock();
        var state = Load(expectedProfile);
        edit(state);
        ProfileFiles.WriteJson(statePath, state);
        PruneSnapshots(state);
        return state;
    }

    internal (GameModProfiles State, List<string> Missing) Switch(
        Guid expectedProfile, Guid targetId, ModProfile? clone, bool deleteActive, Action checkGameStopped)
    {
        using var fileLock = Lock();
        var state = Load(expectedProfile);
        var outgoing = state.Profiles.Single(p => p.Id == expectedProfile);
        var target = clone ?? state.Profiles.Single(p => p.Id == targetId);
        var missing = new List<string>();
        if (outgoing.Id == target.Id)
            return (state, missing);
        if (clone != null && state.Profiles.Any(profile => profile.Name == clone.Name))
            throw new InvalidOperationException("A profile with that name already exists.");

        var generation = Guid.NewGuid();
        var snapshot = SnapshotPath(outgoing.Id, generation);
        try
        {
            Transact(state, transaction =>
            {
                checkGameStopped();
                Capture(snapshot, state.InstalledMods);
                List<ProfileMod> incomingMods;
                string incoming;
                if (clone != null)
                {
                    incomingMods = state.InstalledMods;
                    incoming = Path.Combine(transaction.WorkFolder, "new-profile");
                    CopyManagedFiles(snapshot, incoming, incomingMods);
                    state.Profiles.Add(clone);
                }
                else if (target.SnapshotId is Guid snapshotId)
                {
                    incoming = SnapshotPath(target.Id, snapshotId);
                    incomingMods = ProfileFiles.ReadJson<List<ProfileMod>>(Path.Combine(incoming, "manifest.json"));
                }
                else
                {
                    incoming = Path.Combine(transaction.WorkFolder, "legacy");
                    incomingMods = Materialize(target, incoming, missing);
                }

                var incomingFolder = Path.Combine(incoming, ProfileFiles.Mods);
                if (!Directory.Exists(incomingFolder))
                    throw new DirectoryNotFoundException($"The saved Mods folder is missing for {target.Name}.");
                transaction.Add(ProfileFiles.Mods, incomingFolder, isDirectory: true);

                var outgoingOutside = OutsideFiles(state.InstalledMods);
                var incomingOutside = OutsideFiles(incomingMods);
                foreach (var path in outgoingOutside.Except(incomingOutside, ProfileFiles.PathComparer))
                    transaction.Add(path, replacement: null);
                foreach (var path in incomingOutside)
                {
                    EnsureOwnedOrMissing(outgoingOutside, path);
                    transaction.Add(path, ResolveSnapshotFile(incoming, path));
                }

                outgoing.SnapshotId = generation;
                target.DeployedModIds = new();
                state.ActiveProfileId = target.Id;
                state.InstalledMods = incomingMods;
                if (deleteActive)
                    state.Profiles.Remove(outgoing);
                checkGameStopped();
            });
        }
        catch
        {
            ProfileFiles.TryDeleteDirectory(snapshot);
            throw;
        }

        PruneSnapshots(state);
        return (state, missing);
    }

    private void Capture(string snapshot, List<ProfileMod> mods)
    {
        var liveMods = ProfileFiles.Resolve(gamePath, ProfileFiles.Mods);
        if (Path.Exists(liveMods) && !Directory.Exists(liveMods))
            throw new IOException("A file is blocking the Mods folder.");

        ProfileFiles.CopyDirectory(liveMods, Path.Combine(snapshot, ProfileFiles.Mods));
        foreach (var path in OutsideFiles(mods))
            CopyToSnapshot(snapshot, path, ProfileFiles.Resolve(gamePath, path));
        ProfileFiles.WriteJson(Path.Combine(snapshot, "manifest.json"), mods);
    }

    private static void CopyManagedFiles(string snapshot, string destination, List<ProfileMod> mods)
    {
        Directory.CreateDirectory(Path.Combine(destination, ProfileFiles.Mods));
        var paths = mods.SelectMany(mod => mod.Files).Distinct(ProfileFiles.PathComparer);
        foreach (var path in paths)
        {
            var source = ResolveSnapshotFile(snapshot, path);
            CopyToSnapshot(destination, path, source);
        }
    }

    private static string ResolveSnapshotFile(string snapshot, string path)
    {
        var root = ProfileFiles.IsInMods(path) ? snapshot : Path.Combine(snapshot, "outside");
        return ProfileFiles.Resolve(root, path);
    }

    private static void CopyToSnapshot(string snapshot, string path, string source, bool overwrite = false)
    {
        var destination = ResolveSnapshotFile(snapshot, path);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite);
    }

    private List<ProfileMod> Materialize(ModProfile target, string folder, List<string> missing)
    {
        Directory.CreateDirectory(Path.Combine(folder, ProfileFiles.Mods));
        var mods = new List<ProfileMod>();
        var ids = (target.DeployedModIds ?? new()).Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim()).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            if (packages.Find(id) is not ModPackage package)
            {
                missing.Add(id);
                continue;
            }
            var files = PackageFiles(package);
            foreach (var (path, source) in files)
                CopyToSnapshot(folder, path, source, overwrite: true);
            mods.Add(new ProfileMod(package.Mod, files.Keys.ToList()));
        }
        return mods;
    }

    internal GameModProfiles ChangeInstalledMods(Guid expectedProfile, IReadOnlyList<ModPackage> install, string? uninstall = null)
    {
        using var fileLock = Lock();
        var state = Load(expectedProfile);
        var replaced = install.Select(p => p.Mod.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (uninstall != null)
            replaced.Add(uninstall);
        var previous = state.InstalledMods;
        var next = previous.Where(p => !replaced.Contains(p.Mod.Id)).ToList();
        var incoming = new Dictionary<string, string>(ProfileFiles.PathComparer);
        foreach (var package in install)
        {
            var files = PackageFiles(package);
            foreach (var (path, source) in files)
                incoming[path] = source;
            next.Add(new ProfileMod(package.Mod, files.Keys.ToList()));
        }

        var beforePaths = previous.SelectMany(p => p.Files).ToHashSet(ProfileFiles.PathComparer);
        var afterPaths = next.SelectMany(p => p.Files).ToHashSet(ProfileFiles.PathComparer);
        Transact(state, transaction =>
        {
            foreach (var path in beforePaths.Except(afterPaths, ProfileFiles.PathComparer))
                transaction.Add(path, replacement: null);

            foreach (var (path, source) in incoming)
            {
                if (!ProfileFiles.IsInMods(path))
                    EnsureOwnedOrMissing(beforePaths, path);
                transaction.Add(path, source);
            }
            state.InstalledMods = next;
        });
        return state;
    }

    private static Dictionary<string, string> PackageFiles(ModPackage package)
    {
        var files = new Dictionary<string, string>(ProfileFiles.PathComparer);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false
        };
        foreach (var source in Directory.EnumerateFiles(package.Folder, "*", options))
        {
            if (Path.GetFileName(source) is "mod.json" or ".deployment.json")
                continue;
            var relative = Path.GetRelativePath(package.Folder, source);
            ProfileFiles.Resolve(package.Folder, relative);
            var path = ProfileFiles.Normalize(relative);
            if (path == ProfileFiles.Mods)
                throw new InvalidDataException("A mod package cannot replace the Mods directory with a file.");
            files.Add(path, source);
        }

        if (files.Count == 0)
            throw new InvalidDataException($"{package.Mod.Name} has no files to install.");
        return files;
    }

    private void EnsureOwnedOrMissing(HashSet<string> owned, string path)
    {
        if (!owned.Contains(path) && Path.Exists(ProfileFiles.Resolve(gamePath, path)))
            throw new IOException($"An untracked file or folder is blocking {path}.");
    }

    private static HashSet<string> OutsideFiles(IEnumerable<ProfileMod> mods) =>
        mods.SelectMany(p => p.Files).Where(path => !ProfileFiles.IsInMods(path)).ToHashSet(ProfileFiles.PathComparer);

    private void Transact(GameModProfiles state, Action<ProfileFileTransaction> prepare)
    {
        var transaction = new ProfileFileTransaction(gamePath, journalPath);
        var previousOperation = state.LastOperationId;
        try
        {
            transaction.BeginStaging();
            prepare(transaction);
            transaction.Apply();
            state.LastOperationId = transaction.Id;
            ProfileFiles.WriteJson(statePath, state);
        }
        catch (Exception failure)
        {
            try
            {
                ProfileFileTransaction.Recover(gamePath, journalPath, previousOperation);
                ProfileFiles.TryDeleteDirectory(transaction.WorkFolder);
            }
            catch (Exception recoveryFailure)
            {
                throw new IOException($"{failure.Message} Recovery could not finish. {recoveryFailure.Message}", recoveryFailure);
            }
            throw;
        }

        try
        {
            ProfileFileTransaction.Recover(gamePath, journalPath, state.LastOperationId);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string SnapshotPath(Guid profile, Guid generation) =>
        Path.Combine(storePath, "snapshots", profile.ToString("N"), generation.ToString("N"));

    private void PruneSnapshots(GameModProfiles state)
    {
        var root = Path.Combine(storePath, "snapshots");
        if (!Directory.Exists(root))
            return;

        var saved = state.Profiles.Where(p => p.SnapshotId.HasValue)
            .Select(p => SnapshotPath(p.Id, p.SnapshotId!.Value)).ToHashSet(ProfileFiles.PathComparer);
        try
        {
            foreach (var profile in Directory.EnumerateDirectories(root))
            {
                if (!Guid.TryParseExact(Path.GetFileName(profile), "N", out _))
                    continue;
                foreach (var snapshot in Directory.EnumerateDirectories(profile))
                {
                    if (Guid.TryParseExact(Path.GetFileName(snapshot), "N", out _) && !saved.Contains(snapshot))
                        ProfileFiles.TryDeleteDirectory(snapshot);
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
