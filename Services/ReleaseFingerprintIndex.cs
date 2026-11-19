using StellarModManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StellarModManager.Services;

internal sealed record ReleaseFingerprint(string Url, InstalledModInfo Mod, Dictionary<string, string> Files);
internal sealed record CatalogDllMatch(ReleaseFingerprint Release, ExternalDll Dll, string ReleasePath);
internal sealed record CatalogModRelease(string Id, string Name, string Repo, string Url);

internal sealed class ReleaseFingerprintIndex(string cachePath)
{
    private sealed record FingerprintCache(int Version, List<ReleaseFingerprint> Releases);
    private readonly object cacheLock = new();
    private readonly Dictionary<string, DateTime> retryAfter = new(StringComparer.Ordinal);
    private List<ReleaseFingerprint>? cached;

    public void Record(string url, StagedMod staged)
    {
        var files = staged.Package.Files.Where(ModInstallerService.InMods).ToDictionary(
            path => path, path => ExternalModDiscovery.HashFile(Path.Combine(staged.Directory, path)),
            StringComparer.OrdinalIgnoreCase);
        lock (cacheLock)
        {
            var releases = GetCache();
            releases.RemoveAll(item => item.Url == url && SameId(item.Mod.Id, staged.Package.Mod.Id));
            releases.Add(new ReleaseFingerprint(url, staged.Package.Mod, files));
            ExternalModDiscovery.WriteCache(cachePath, new FingerprintCache(1, releases));
        }
    }

    public async Task<List<CatalogDllMatch>> MatchAsync(IReadOnlyList<ExternalDll> files,
        IReadOnlyList<CatalogModRelease> catalog, ModRepositoryService repository, ModInstallerService installer,
        CancellationToken cancellationToken)
    {
        var eligible = files.Where(file => file.LinkTarget == null).ToList();
        if (eligible.Count == 0)
            return [];

        List<ReleaseFingerprint> releases;
        lock (cacheLock)
            releases = [.. GetCache()];

        var candidates = eligible.SelectMany(file => Candidates(file, catalog))
            .Distinct()
            .ToList();

        foreach (var (id, url) in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (releases.Any(item => item.Url == url && SameId(item.Mod.Id, id)) || IsCoolingDown(url))
                continue;

            await FetchAsync(id, url, repository, installer, cancellationToken).ConfigureAwait(false);
        }

        lock (cacheLock)
            releases = [.. GetCache()];
        return [.. eligible.Select(file => Match(file, releases)).OfType<CatalogDllMatch>()];
    }

    private async Task FetchAsync(string id, string url, ModRepositoryService repository,
        ModInstallerService installer, CancellationToken cancellationToken)
    {
        DirectoryInfo? temporary = null;
        try
        {
            temporary = Directory.CreateTempSubdirectory("StellarModManager-fingerprint-");
            var archive = Path.Combine(temporary.FullName, "release.zip");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await repository.DownloadModAsync(url, archive, null, timeout.Token).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Record(url, installer.Extract(archive, Path.Combine(temporary.FullName, "payload"), id));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
            or System.Net.Http.HttpRequestException or System.Text.Json.JsonException or OperationCanceledException)
        {
            lock (cacheLock)
                retryAfter[url] = DateTime.UtcNow.AddMinutes(5);
        }
        finally
        {
            try { temporary?.Delete(recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private bool IsCoolingDown(string url)
    {
        lock (cacheLock)
            return retryAfter.TryGetValue(url, out var retry) && DateTime.UtcNow < retry;
    }

    private List<ReleaseFingerprint> GetCache()
    {
        if (cached == null)
        {
            var saved = ExternalModDiscovery.ReadCache<FingerprintCache>(cachePath);
            cached = saved is { Version: 1, Releases: not null } ? saved.Releases : [];
        }
        return cached;
    }

    private static IEnumerable<(string Id, string Url)> Candidates(ExternalDll file, IReadOnlyList<CatalogModRelease> catalog)
    {
        var names = file.Melons.Select(melon => Normalize(melon.Name)).ToHashSet(StringComparer.Ordinal);
        names.Add(Normalize(Path.GetFileNameWithoutExtension(file.Path)));
        names.Remove("");

        foreach (var mod in catalog)
        {
            if (!names.Contains(Normalize(mod.Name)) && !names.Contains(Normalize(mod.Id))
                && !names.Contains(Normalize(mod.Repo)))
                continue;

            if (!Uri.TryCreate(mod.Url, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps
                || url.Host != "github.com")
                continue;

            var root = mod.Url[..(mod.Url.LastIndexOf('/') + 1)];
            var tagStart = root.LastIndexOf("/download/", StringComparison.Ordinal);
            if (tagStart >= 0)
            {
                var releaseRoot = root[..(tagStart + "/download/".Length)];
                var asset = url.Segments[^1];
                foreach (var melon in file.Melons)
                {
                    var version = VersionText(melon.Version);
                    if (version.Length > 0 && char.IsAsciiDigit(version[0])
                        && version.All(character => char.IsAsciiLetterOrDigit(character) || ".+-".Contains(character)))
                    {
                        yield return (mod.Id, $"{releaseRoot}v{Uri.EscapeDataString(version)}/{asset}");
                    }
                }
            }

            yield return (mod.Id, mod.Url);
        }
    }

    private static CatalogDllMatch? Match(ExternalDll file, List<ReleaseFingerprint> releases)
    {
        var matching = releases.Where(release => MatchingPaths(release, file).Any()).ToList();
        if (matching.Count == 0 || matching.Select(item => item.Mod.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
            return null;

        if (matching.Select(item => item.Mod.Version).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            matching = [.. matching.Where(release => file.Melons.Any(melon =>
                string.Equals(VersionText(release.Mod.Version), VersionText(melon.Version), StringComparison.OrdinalIgnoreCase)))];
        }

        if (matching.Count == 0 || matching.Select(item => item.Mod.Version).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
            return null;

        var byPath = matching.Select(release => (Release: release, Path: MatchPath(release, file)))
            .Where(item => item.Path != null).ToList();
        if (byPath.Count == 0 || byPath.Select(item => item.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
            return null;

        return new CatalogDllMatch(byPath[0].Release, file, byPath[0].Path!);
    }

    private static string? MatchPath(ReleaseFingerprint release, ExternalDll file)
    {
        var paths = MatchingPaths(release, file).ToList();
        return paths.FirstOrDefault(path => string.Equals(path, file.Path, StringComparison.OrdinalIgnoreCase))
            ?? (paths.Count == 1 ? paths[0] : null);
    }

    private static IEnumerable<string> MatchingPaths(ReleaseFingerprint release, ExternalDll file) =>
        release.Files.Where(pair => IsDll(pair.Key) && string.Equals(pair.Value, file.Hash, StringComparison.Ordinal)).Select(pair => pair.Key);

    private static bool IsDll(string path) => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
    private static string VersionText(string value) => value.Trim().TrimStart('v', 'V');
    private static bool SameId(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
