using StellarModManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using static StellarModManager.Services.ProfileFileTransaction;

namespace StellarModManager.Services;

internal sealed partial class ProfileStorageService
{
    private sealed record LegacyProfile(string Name, List<string>? DeployedModIds);
    private sealed record LegacyProfiles(string ActiveProfile, List<LegacyProfile>? Profiles);
    private sealed record LegacyPackage(string Directory, ProfilePackage Package, HashSet<string>? Deployment);

    private ProfileState Migrate(ProfileFileTransaction transaction, string gamePath, List<string> missing)
    {
        var oldProfiles = File.Exists(StatePath)
            ? JsonSerializer.Deserialize<LegacyProfiles>(File.ReadAllText(StatePath))
                ?? throw new IOException($"Invalid legacy profiles file: {StatePath}")
            : null;
        var profiles = oldProfiles?.Profiles is { Count: > 0 } saved
            ? saved : [new("Default", null)];
        var oldActive = profiles.FirstOrDefault(profile => profile.Name == oldProfiles?.ActiveProfile) ?? profiles[0];

        var library = Path.Combine(managerPath, "Library");
        var libraryPackages = ReadLegacyLibrary(library).ToDictionary(item => item.Package.Mod.Id, ModIdentity);
        var state = new ProfileState { GamePath = gamePath };
        var liveFiles = FindLegacyGameFiles(gamePath, libraryPackages.Values);
        var activeIds = LegacySelection(oldActive, libraryPackages.Values);

        foreach (var old in profiles)
        {
            var profile = AddProfile(transaction, state, old.Name);
            var active = ReferenceEquals(old, oldActive);
            if (active)
                state.ActiveProfileId = profile.Id;

            var ids = active ? activeIds : (old.DeployedModIds ?? []).ToHashSet(ModIdentity);
            foreach (var id in ids.Where(id => !libraryPackages.ContainsKey(id)))
                missing.Add($"{old.Name}: {id}");

            var packages = libraryPackages.Values.Where(package => ids.Contains(package.Package.Mod.Id)).ToList();
            foreach (var package in packages)
            {
                var id = package.Package.Mod.Id;
                var keepEdits = active || !activeIds.Contains(id);
                foreach (var path in package.Package.Files)
                {
                    var source = CheckedPath(package.Directory, path);
                    if (keepEdits && liveFiles.TryGetValue(path, out var live) && ModIdentity.Equals(live.Owner, id))
                        source = live.Path;

                    CopyNewFile(transaction, source, PackageFile(profile.Id, path));
                }
            }

            transaction.ReplaceJson(InventoryPath(profile.Id), packages.Select(package => package.Package).ToList());
        }

        var gameMods = Path.Combine(gamePath, "Mods");
        if (Exists(gameMods))
            AdoptMods(transaction, state.ActiveProfileId, gamePath, liveFiles.Keys.ToHashSet(ModIdentity));

        foreach (var file in liveFiles.Where(item => !ModInstallerService.InMods(item.Key)))
            transaction.MoveAside(gamePath, file.Value.Path);

        Attach(transaction, state.ActiveProfileId, gamePath);
        SaveState(transaction, state);

        foreach (var entry in libraryPackages.Values)
            transaction.MoveAside(library, entry.Directory, directory: true);

        return state;
    }

    private static HashSet<string> LegacySelection(LegacyProfile profile, IEnumerable<LegacyPackage> packages) =>
        (profile.DeployedModIds ?? packages.Where(package => package.Deployment != null)
            .Select(package => package.Package.Mod.Id)).ToHashSet(ModIdentity);

    private static Dictionary<string, (string Owner, string Path)> FindLegacyGameFiles(
        string gamePath, IEnumerable<LegacyPackage> packages)
    {
        var files = new Dictionary<string, (string Owner, string Path)>(ModIdentity);
        foreach (var package in packages.Where(package => package.Deployment != null))
        {
            foreach (var path in package.Package.Files.Where(package.Deployment!.Contains))
            {
                var gameFile = CheckedPath(gamePath, path);
                if (!Exists(gameFile))
                    continue;

                RequireRegularFile(gameFile);
                if (!files.TryAdd(path, (package.Package.Mod.Id, gameFile)))
                    throw new IOException($"Multiple legacy mods claim '{gameFile}'.");
            }
        }
        return files;
    }

    private static IEnumerable<LegacyPackage> ReadLegacyLibrary(string library)
    {
        if (!Exists(library))
            yield break;

        EnsureRealDirectory(library);
        foreach (var directory in Directory.EnumerateDirectories(library))
        {
            EnsureRealDirectory(directory);
            var metadata = Path.Combine(directory, "mod.json");
            if (!Exists(metadata))
                continue;

            RequireRegularFile(metadata);
            using var stream = File.OpenRead(metadata);
            var mod = ModInstallerService.ReadMetadata(stream);
            var files = RegularFiles(directory)
                .Select(path => Path.GetRelativePath(directory, path))
                .Where(path => !ModInstallerService.IsInternalFile(path))
                .Select(ModInstallerService.GameRelativePath)
                .ToList();

            HashSet<string>? deployment = null;
            var manifest = Path.Combine(directory, ".deployment.json");
            if (Exists(manifest))
            {
                RequireRegularFile(manifest);
                var paths = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(manifest))
                    ?? throw new IOException($"Invalid legacy deployment manifest: {manifest}");
                deployment = paths.Select(ModInstallerService.GameRelativePath).ToHashSet(ModIdentity);
            }

            yield return new LegacyPackage(directory, new ProfilePackage(mod, files), deployment);
        }
    }
}
