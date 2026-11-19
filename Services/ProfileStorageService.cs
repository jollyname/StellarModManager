using StellarModManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using static StellarModManager.Services.ProfileFileTransaction;

namespace StellarModManager.Services;

internal sealed record ProfileSnapshot(List<ModProfile> Profiles, Guid ActiveProfileId,
    List<InstalledModInfo> InstalledMods, List<string> MissingLegacyMods)
{
    internal IReadOnlySet<string> OwnedFiles { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

internal sealed partial class ProfileStorageService(string managerPath)
{
    private sealed class ProfileState
    {
        public int Version { get; set; } = 1;
        public Guid ActiveProfileId { get; set; }
        public string GamePath { get; set; } = "";
        public List<ModProfile> Profiles { get; set; } = [];
    }

    private readonly string profilesRoot = Path.Combine(managerPath, "Profiles");
    private readonly string dataRoot = Path.Combine(managerPath, "Data");
    private string StatePath => Path.Combine(dataRoot, "modprofiles.json");

    private static readonly StringComparer ModIdentity = StringComparer.OrdinalIgnoreCase;

    public string? GetSavedGamePath()
    {
        if (!File.Exists(StatePath) && !File.Exists(Path.Combine(dataRoot, "profiles.transaction.json")))
            return null;
        return Run(_ => ReadState()?.GamePath);
    }

    public ProfileSnapshot Initialize(string gamePath) => Run(transaction =>
    {
        gamePath = GetGamePath(gamePath);
        var state = ReadState();
        var missing = new List<string>();

        if (state == null)
        {
            state = Migrate(transaction, gamePath, missing);
        }
        else
        {
            Activate(transaction, state, state.ActiveProfileId, gamePath);
        }

        return Snapshot(state, missing);
    });

    public ProfileSnapshot Switch(Guid profileId, string gamePath) => Change((transaction, state) =>
        Activate(transaction, state, profileId, GetGamePath(gamePath)));

    public ProfileSnapshot Create(string name, string gamePath) => Change((transaction, state) =>
    {
        var profile = AddProfile(transaction, state, name);
        Activate(transaction, state, profile.Id, GetGamePath(gamePath));
    });

    public ProfileSnapshot Import(string name, IReadOnlyList<StagedMod> mods) => Change((transaction, state) =>
    {
        var profile = AddProfile(transaction, state, name);
        Apply(transaction, profile.Id, mods, [], null);
        SaveState(transaction, state);
    });

    public ProfileSnapshot Rename(Guid profileId, string name) => Change((transaction, state) =>
    {
        var profile = FindProfile(state, profileId);
        profile.Name = ValidateName(state, name, profileId);
        SaveState(transaction, state);
    });

    public ProfileSnapshot DeleteProfile(Guid profileId, string gamePath) => Change((transaction, state) =>
    {
        var profile = FindProfile(state, profileId);
        if (state.Profiles.Count <= 1)
            throw new InvalidOperationException("Keep at least one mod profile.");

        if (profileId == state.ActiveProfileId)
            Activate(transaction, state, state.Profiles.First(item => item.Id != profileId).Id, GetGamePath(gamePath), save: false);

        transaction.MoveAside(profilesRoot, ProfileDirectory(profileId), directory: true);
        state.Profiles.Remove(profile);
        SaveState(transaction, state);
    });

    public ProfileSnapshot Install(Guid profileId, string gamePath, IReadOnlyList<StagedMod> mods) =>
        ChangePackages(profileId, gamePath, mods, []);

    public ProfileSnapshot Remove(Guid profileId, string gamePath, string modId) =>
        ChangePackages(profileId, gamePath, [], [modId]);

    private ProfileSnapshot ChangePackages(Guid profileId, string gamePath,
        IReadOnlyList<StagedMod> mods, IReadOnlyList<string> removals) => Change((transaction, state) =>
    {
        gamePath = RequireActiveProfile(state, profileId, gamePath);

        Apply(transaction, profileId, mods, removals, gamePath);
    });

    private ProfileSnapshot Change(Action<ProfileFileTransaction, ProfileState> action) => Run(transaction =>
    {
        var state = ReadState() ?? throw new IOException("Set up the game folder before managing profiles.");
        action(transaction, state);
        return Snapshot(state);
    });

    private T Run<T>(Func<ProfileFileTransaction, T> operation)
    {
        EnsureDirectory(dataRoot);
        EnsureDirectory(profilesRoot);
        using var operationLock = new FileStream(Path.Combine(dataRoot, "profiles.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var transaction = new ProfileFileTransaction(Path.Combine(dataRoot, "profiles.transaction.json"));
        var result = operation(transaction);
        transaction.Commit();
        return result;
    }

    private ProfileState? ReadState()
    {
        if (!File.Exists(StatePath))
            return null;
        RequireRegularFile(StatePath);
        var state = JsonSerializer.Deserialize<ProfileState>(File.ReadAllText(StatePath))
            ?? throw new IOException($"Invalid profiles file: {StatePath}");
        if (state.ActiveProfileId == Guid.Empty)
            return null;
        if (state.Version != 1 || state.Profiles.Any(profile => profile.Id == Guid.Empty)
            || state.Profiles.Select(profile => profile.Id).Distinct().Count() != state.Profiles.Count)
            throw new IOException($"Unsupported or invalid profiles file: {StatePath}");
        FindProfile(state, state.ActiveProfileId);
        return state;
    }

    private void SaveState(ProfileFileTransaction transaction, ProfileState state) =>
        transaction.ReplaceJson(StatePath, state);

    private ProfileSnapshot Snapshot(ProfileState state, List<string>? missing = null)
    {
        var packages = Packages(state.ActiveProfileId);
        return new ProfileSnapshot(state.Profiles, state.ActiveProfileId, [.. packages.Select(package => package.Mod)],
            missing ?? [])
        {
            OwnedFiles = packages.SelectMany(package => package.Files).ToHashSet(ModIdentity)
        };
    }

    private string ProfileDirectory(Guid profileId) => Path.Combine(profilesRoot, profileId.ToString("N"));
    private string ModsDirectory(Guid profileId) => Path.Combine(ProfileDirectory(profileId), "Mods");

    private string PackageFile(Guid profileId, string relativePath) =>
        ModInstallerService.InMods(relativePath)
            ? CheckedPath(ProfileDirectory(profileId), relativePath)
            : CheckedPath(Path.Combine(ProfileDirectory(profileId), "GameFiles"), relativePath);

    private string InventoryPath(Guid profileId) => Path.Combine(ProfileDirectory(profileId), "packages.json");

    private List<ProfilePackage> Packages(Guid profileId)
    {
        EnsureRealDirectory(ProfileDirectory(profileId));
        var path = InventoryPath(profileId);
        if (!Exists(path))
            return [];

        RequireRegularFile(path);
        return JsonSerializer.Deserialize<List<ProfilePackage>>(File.ReadAllText(path))
            ?? throw new IOException($"Invalid package inventory: {path}");
    }

    private ModProfile AddProfile(ProfileFileTransaction transaction, ProfileState state, string name)
    {
        var profile = new ModProfile { Id = Guid.NewGuid(), Name = ValidateName(state, name) };
        var staged = transaction.ScratchPath(profilesRoot);
        Directory.CreateDirectory(Path.Combine(staged, "Mods"));
        transaction.Move(staged, ProfileDirectory(profile.Id), directory: true);
        state.Profiles.Add(profile);
        return profile;
    }

    private static string ValidateName(ProfileState state, string name, Guid except = default)
    {
        name = name.Trim();
        if (name.Length == 0 || state.Profiles.Any(profile => profile.Id != except
            && profile.Name == name))
            throw new InvalidOperationException("Enter a unique profile name.");
        return name;
    }

    private static ModProfile FindProfile(ProfileState state, Guid id) =>
        state.Profiles.FirstOrDefault(profile => profile.Id == id)
        ?? throw new IOException($"Profile '{id}' no longer exists.");

    private string GetGamePath(string path)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        EnsureRealDirectory(path);
        var gameMods = Path.Combine(path, "Mods");
        var manager = Path.TrimEndingDirectorySeparator(Path.GetFullPath(managerPath));
        if (string.Equals(manager, gameMods, PathComparison)
            || manager.StartsWith(gameMods + Path.DirectorySeparatorChar, PathComparison))
            throw new IOException("Move Stellar Mod Manager outside the game's Mods directory.");
        return path;
    }

    private void Activate(ProfileFileTransaction transaction, ProfileState state, Guid profileId,
        string gamePath, bool save = true)
    {
        FindProfile(state, profileId);
        EnsureRealDirectory(ProfileDirectory(profileId));
        transaction.CreateDirectory(ModsDirectory(profileId));

        if (state.GamePath.Length != 0 && (state.ActiveProfileId != profileId
            || !string.Equals(state.GamePath, gamePath, PathComparison))
            && Directory.Exists(state.GamePath))
        {
            Detach(transaction, state.ActiveProfileId, state.GamePath);
        }

        Attach(transaction, profileId, gamePath);

        var changed = state.ActiveProfileId != profileId
            || !string.Equals(state.GamePath, gamePath, PathComparison);
        state.ActiveProfileId = profileId;
        state.GamePath = gamePath;
        if (save && changed)
            SaveState(transaction, state);
    }

    private void Attach(ProfileFileTransaction transaction, Guid profileId, string gamePath)
    {
        var gameMods = Path.Combine(gamePath, "Mods");
        var target = ModsDirectory(profileId);
        if (!IsLinkTo(gameMods, target, directory: true))
        {
            if (Exists(gameMods))
            {
                EnsureRealDirectory(gameMods);
                AdoptMods(transaction, profileId, gamePath);
            }

            transaction.CreateLink(gameMods, target, directory: true);
        }

        foreach (var path in ExtraFiles(Packages(profileId)))
            AttachExtra(transaction, profileId, gamePath, path);
    }

    private void Detach(ProfileFileTransaction transaction, Guid profileId, string gamePath)
    {
        EnsureRealDirectory(gamePath);
        var gameMods = Path.Combine(gamePath, "Mods");
        if (IsLinkTo(gameMods, ModsDirectory(profileId), directory: true))
            transaction.MoveAside(gamePath, gameMods, directory: true);

        foreach (var path in ExtraFiles(Packages(profileId)))
            DetachExtra(transaction, profileId, gamePath, path);
    }

    private static IEnumerable<string> ExtraFiles(IEnumerable<ProfilePackage> packages) =>
        packages.SelectMany(package => package.Files).Where(path => !ModInstallerService.InMods(path));

    private void AttachExtra(ProfileFileTransaction transaction, Guid profileId, string gamePath, string path)
    {
        var source = PackageFile(profileId, path);
        var destination = CheckedPath(gamePath, path);
        if (!Exists(source))
            return;

        RequireRegularFile(source);
        if (IsLinkTo(destination, source, directory: false))
            return;
        transaction.CreateDirectory(Path.GetDirectoryName(destination)!);
        transaction.CreateLink(destination, source);
    }

    private void DetachExtra(ProfileFileTransaction transaction, Guid profileId, string gamePath, string path)
    {
        var destination = CheckedPath(gamePath, path);
        if (IsLinkTo(destination, PackageFile(profileId, path), directory: false))
            transaction.MoveAside(gamePath, destination);
    }

    private void AdoptMods(ProfileFileTransaction transaction, Guid profileId, string gamePath,
        HashSet<string>? migratedFiles = null)
    {
        var gameMods = Path.Combine(gamePath, "Mods");
        var profileMods = ModsDirectory(profileId);
        EnsureRealDirectory(gameMods);
        AdoptEntries(gameMods, profileMods);
        transaction.MoveAside(gamePath, gameMods, directory: true);

        void AdoptEntries(string source, string destination)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(source))
            {
                var relative = Path.GetRelativePath(gameMods, entry);
                if (migratedFiles?.Contains("Mods/" + relative.Replace(Path.DirectorySeparatorChar, '/')) == true)
                    continue;

                var target = Path.Combine(destination, Path.GetFileName(entry));
                var attributes = File.GetAttributes(entry);
                var directory = attributes.HasFlag(FileAttributes.Directory);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    FileSystemInfo info = directory ? new DirectoryInfo(entry) : new FileInfo(entry);
                    var link = info.LinkTarget ?? throw new IOException($"Could not read the existing link at '{entry}'.");
                    var linkTarget = Path.GetFullPath(link, source);
                    if (string.Equals(linkTarget, gameMods, PathComparison)
                        || linkTarget.StartsWith(gameMods + Path.DirectorySeparatorChar, PathComparison))
                        linkTarget = Path.Combine(profileMods, Path.GetRelativePath(gameMods, linkTarget));

                    transaction.CreateLink(target, linkTarget, directory);
                }
                else if (directory)
                {
                    transaction.CreateDirectory(target);
                    AdoptEntries(entry, target);
                }
                else
                {
                    CopyNewFile(transaction, entry, target);
                }
            }
        }
    }

    private void CopyNewFile(ProfileFileTransaction transaction, string source, string destination)
    {
        var staged = transaction.ScratchPath(profilesRoot);
        File.Copy(source, staged);
        transaction.CreateDirectory(Path.GetDirectoryName(destination)!);
        transaction.Move(staged, destination);
    }

    private void Apply(ProfileFileTransaction transaction, Guid profileId, IReadOnlyList<StagedMod> mods,
        IReadOnlyList<string> removals, string? gamePath)
    {
        var current = Packages(profileId);
        var replacing = new HashSet<string>(mods.Select(item => item.Package.Mod.Id).Concat(removals), ModIdentity);
        var kept = current.Where(item => !replacing.Contains(item.Mod.Id)).ToList();
        var next = kept.Concat(mods.Select(item => item.Package)).ToList();

        var installed = next.Select(item => item.Mod).ToList();
        if (installed.Select(mod => mod.Id).Distinct(ModIdentity).Count() != installed.Count)
            throw new IOException("Multiple packages declare the same mod ID.");

        foreach (var staged in mods)
        {
            var missing = DependencyResolver.FindMissing(staged.Package.Mod, installed);
            if (missing.Count > 0)
                throw new IOException($"'{staged.Package.Mod.Name}' requires: {string.Join(", ", missing)}.");
        }

        var owners = new Dictionary<string, string>(ModIdentity);
        foreach (var package in next)
        {
            foreach (var path in package.Files)
            {
                if (!owners.TryAdd(path, package.Mod.Id))
                    throw new IOException($"File '{path}' is shared by '{owners[path]}' and '{package.Mod.Id}'.");
            }
        }

        foreach (var old in current.Where(item => replacing.Contains(item.Mod.Id)))
        {
            foreach (var path in old.Files)
            {
                var destination = PackageFile(profileId, path);
                if (gamePath != null && !ModInstallerService.InMods(path))
                    DetachExtra(transaction, profileId, gamePath, path);

                if (Exists(destination))
                {
                    RequireRegularFile(destination);
                    transaction.MoveAside(profilesRoot, destination);
                }
            }
        }

        foreach (var staged in mods)
        {
            foreach (var path in staged.Package.Files)
            {
                CopyNewFile(transaction, CheckedPath(staged.Directory, path), PackageFile(profileId, path));
                if (gamePath != null && !ModInstallerService.InMods(path))
                    AttachExtra(transaction, profileId, gamePath, path);
            }
        }

        transaction.ReplaceJson(InventoryPath(profileId), next);
    }

    private static string CheckedPath(string root, string path) =>
        CheckedNativePath(root, ModInstallerService.GameRelativePath(path).Replace('/', Path.DirectorySeparatorChar));

    private static string CheckedNativePath(string root, string path)
    {
        var current = root;
        if (Exists(current))
            EnsureRealDirectory(current);

        var parts = path.Split(Path.DirectorySeparatorChar);
        for (var index = 0; index < parts.Length - 1; index++)
        {
            current = Path.Combine(current, parts[index]);
            if (Exists(current))
                EnsureRealDirectory(current);
        }
        return Path.Combine(current, parts[^1]);
    }

    private static IEnumerable<string> RegularFiles(string root)
    {
        EnsureRealDirectory(root);
        foreach (var path in Directory.EnumerateFileSystemEntries(root))
        {
            var attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException($"Move this existing link out of the folder before continuing: {path}");

            if (attributes.HasFlag(FileAttributes.Directory))
            {
                foreach (var file in RegularFiles(path))
                    yield return file;
            }
            else
            {
                yield return path;
            }
        }
    }

    private static void EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        EnsureRealDirectory(path);
    }

    private static void EnsureRealDirectory(string path)
    {
        if (!IsRegularDirectory(path))
            throw new IOException($"Expected a regular directory at '{path}'.");
    }

    private static void RequireRegularFile(string path)
    {
        if (Attributes(path) is not { } attributes
            || (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new IOException($"Expected a regular file at '{path}'.");
    }
}
