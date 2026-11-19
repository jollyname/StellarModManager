using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using static StellarModManager.Services.ProfileFileTransaction;

namespace StellarModManager.Services;

internal sealed partial class ProfileStorageService
{
    internal string GetModsDirectory(Guid profileId) => ModsDirectory(profileId);

    internal List<ExternalDll> ScanExternal(Guid profileId, IReadOnlySet<string> ownedFiles,
        IReadOnlySet<string> changedPaths, bool invalidate, CancellationToken cancellationToken) =>
        ExternalModDiscovery.Scan(ModsDirectory(profileId),
            Path.Combine(ProfileDirectory(profileId), "external-mods.json"), ownedFiles, changedPaths, invalidate, cancellationToken);

    internal ProfileSnapshot RemoveExternal(Guid profileId, string gamePath, ExternalDll dll) =>
        Change((transaction, state) =>
        {
            RequireActiveProfile(state, profileId, gamePath);
            if (Packages(profileId).Any(package => package.Files.Contains(dll.Path, ModIdentity)))
                throw new IOException($"'{dll.Path}' is now managed by an installed package.");

            var path = ExternalPath(profileId, dll.Path);
            var info = new FileInfo(path);
            if (!info.Exists || info.LinkTarget != dll.LinkTarget
                || !string.Equals(ExternalModDiscovery.HashFile(path), dll.Hash, StringComparison.Ordinal))
                throw new IOException($"'{dll.Path}' changed. Select it again to remove it.");

            var backup = transaction.ScratchPath(profilesRoot);
            transaction.Move(path, backup);
            var backedUp = new FileInfo(backup);
            var sameFile = dll.LinkTarget == null
                ? backedUp.LinkTarget == null && string.Equals(ExternalModDiscovery.HashFile(backup), dll.Hash, StringComparison.Ordinal)
                : backedUp.LinkTarget == dll.LinkTarget;
            if (!sameFile)
                throw new IOException($"'{dll.Path}' changed. Select it again to remove it.");
        });

    internal ProfileSnapshot? RegisterExternal(Guid profileId, string gamePath, IReadOnlyList<CatalogDllMatch> matches,
        CancellationToken cancellationToken) => Run(transaction =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = ReadState() ?? throw new IOException("Set up the game folder before registering mods.");
        RequireActiveProfile(state, profileId, gamePath);
        var packages = Packages(profileId);
        var ownedFiles = packages.SelectMany(package => package.Files).ToHashSet(ModIdentity);
        var changed = false;

        foreach (var byMod in matches.GroupBy(match => match.Release.Mod.Id, ModIdentity))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (packages.Any(package => ModIdentity.Equals(package.Mod.Id, byMod.Key))
                || byMod.Select(match => match.Release.Mod.Version).Distinct(ModIdentity).Count() != 1)
                continue;

            var release = byMod.First().Release;
            var verified = new HashSet<string>(ModIdentity);
            foreach (var byArchivePath in byMod.GroupBy(match => match.ReleasePath, ModIdentity))
            {
                var exact = byArchivePath.FirstOrDefault(match => ModIdentity.Equals(match.Dll.Path, match.ReleasePath));
                var occurrence = exact ?? (byArchivePath.Count() == 1 ? byArchivePath.First() : null);
                if (occurrence == null || !release.Files.TryGetValue(occurrence.ReleasePath, out var expected))
                    continue;

                if (CanClaim(occurrence.Dll.Path, expected))
                    verified.Add(occurrence.Dll.Path);
            }

            if (verified.Count == 0)
                continue;

            foreach (var (path, hash) in release.Files)
            {
                if (CanClaim(path, hash))
                    verified.Add(path);
            }

            ownedFiles.UnionWith(verified);
            packages.Add(new ProfilePackage(release.Mod, [.. verified]));
            changed = true;
        }

        if (!changed)
            return null;

        cancellationToken.ThrowIfCancellationRequested();
        transaction.ReplaceJson(InventoryPath(profileId), packages);
        return Snapshot(state);

        bool CanClaim(string relative, string hash)
        {
            if (!ModInstallerService.InMods(relative) || ownedFiles.Contains(relative))
                return false;

            try
            {
                if (!string.Equals(ModInstallerService.GameRelativePath(relative), relative, StringComparison.Ordinal))
                    return false;

                var path = PackageFile(profileId, relative);
                if (!Exists(path))
                    return false;

                RequireRegularFile(path);
                return string.Equals(ExternalModDiscovery.HashFile(path), hash, StringComparison.Ordinal);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return false;
            }
        }
    });

    private string RequireActiveProfile(ProfileState state, Guid profileId, string gamePath)
    {
        var game = GetGamePath(gamePath);
        if (state.ActiveProfileId != profileId || !string.Equals(state.GamePath, game, PathComparison)
            || !IsLinkTo(Path.Combine(game, "Mods"), ModsDirectory(profileId), directory: true))
            throw new IOException("The active profile or its game link changed. Reload the profile before editing it.");
        return game;
    }

    private string ExternalPath(Guid profileId, string relative)
    {
        if (!relative.StartsWith("Mods/", StringComparison.Ordinal) || !relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || relative.Split('/').Any(part => part is "" or "." or ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            || (OperatingSystem.IsWindows() && relative.Contains('\\')))
            throw new IOException($"Invalid external DLL path: {relative}");

        EnsureRealDirectory(ProfileDirectory(profileId));
        return CheckedNativePath(ProfileDirectory(profileId), relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
