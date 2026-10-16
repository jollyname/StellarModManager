using StellarModManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using System.Linq; // thumbnailss
using Avalonia.Platform;

namespace StellarModManager.Services;

public class ModRepositoryService
{
    private readonly HttpClient httpClient = new();
    private readonly SemaphoreSlim commitSlots = new(4);

    public ModRepositoryService()
    {
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("StellarModManager");
    }

    private static readonly string[] DefaultThumbnailAssets =
    {
        "avares://StellarModManager/Assets/Thumbnails/smileyAngry.png",
        "avares://StellarModManager/Assets/Thumbnails/smileyCry.png",
        "avares://StellarModManager/Assets/Thumbnails/smileyFrown.png",
        "avares://StellarModManager/Assets/Thumbnails/smileyGrumpy.png",
        "avares://StellarModManager/Assets/Thumbnails/smileyTounge.png",
        "avares://StellarModManager/Assets/Thumbnails/smileyWhat.png"
    };

    private static Bitmap LoadDefaultThumbnail(OnlineModInfo mod)
    {
        int index = Random.Shared.Next(DefaultThumbnailAssets.Length);

        using var stream = AssetLoader.Open(new Uri(DefaultThumbnailAssets[index]));

        return Bitmap.DecodeToWidth(stream, 192);
    }

    // The catalogue host sends Cache-Control: no-cache. raw.githubusercontent.com ignores a query string.
    private static string NoCache(string url) => $"{url}?t={DateTime.UtcNow.Ticks}";

    public async Task<ModListResult> GetModsAsync(string url)
    {
        string json = await httpClient.GetStringAsync(NoCache(url));

        RepositoryResponse? response = JsonSerializer.Deserialize<RepositoryResponse>(json);
        var result = new ModListResult();

        if (response == null)
            return result;

        //load every mod at once, to speed up request
        OnlineModInfo?[] mods = await Task.WhenAll(response.Mods.Select(entry => LoadModAsync(entry, result)));
        result.Mods.AddRange(mods.OfType<OnlineModInfo>());
        return result;
    }

    private static readonly Regex GitHubName = new(@"^[A-Za-z0-9][A-Za-z0-9._-]*$");
    private static readonly Regex RepoPath = new(@"^([A-Za-z0-9_-][A-Za-z0-9._-]*(/[A-Za-z0-9_-][A-Za-z0-9._-]*)*)?$");

    private static bool IsSafe(ModRegistryEntry entry)
    {
        return GitHubName.IsMatch(entry.Author) && GitHubName.IsMatch(entry.Repo) && RepoPath.IsMatch(entry.MetadataPath);
    }

    private async Task<OnlineModInfo?> LoadModAsync(ModRegistryEntry entry, ModListResult result)
    {
        if (!IsSafe(entry))
            return null;

        try
        {
            string commit = await LatestCommitAsync(entry.Author, entry.Repo);
            string metadataUrl = GithubFiles.RawFile(entry.Author, entry.Repo, commit, entry.MetadataPath, "mod.json");

            string modJson = await httpClient.GetStringAsync(metadataUrl);

            OnlineModInfo? mod = JsonSerializer.Deserialize<OnlineModInfo>(modJson);

            if (mod == null)
            {
                result.AddFailure(entry.Author, entry.Repo);
                return null;
            }

            mod.RepoOwner = entry.Author;
            mod.RepoName = entry.Repo;

            if (!string.IsNullOrWhiteSpace(entry.Ai))
            {
                mod.Ai = entry.Ai; // Override from mods list wins
                Console.WriteLine($"Override from entry: {entry.Repo}, ai tag is: {entry.Ai}");
            }
            mod.RebuildTags();

            mod.DownloadUrl = $"https://github.com/{entry.Author}/{entry.Repo}/releases/download/v{mod.Version}/{entry.Repo}.zip";

            // grab every image with thumbnail at the same time
            string ToUrl(string path) => GithubFiles.RawFile(entry.Author, entry.Repo, commit, entry.MetadataPath, path);

            if (!string.IsNullOrWhiteSpace(mod.Thumbnail))
            {
                mod.ThumbnailUrl = ToUrl(mod.Thumbnail);
            }

            mod.ImageUrls = mod.Images.Where(path => !string.IsNullOrWhiteSpace(path)).Select(ToUrl).ToList();

            return mod;
        }
        catch (Exception ex)
        {
            result.AddFailure(entry.Author, entry.Repo);
            Console.WriteLine($"Failed to load mod from {entry.Repo}: {ex.Message}");
            return null;
        }
    }

    private async Task<string> LatestCommitAsync(string owner, string repo)
    {
        await commitSlots.WaitAsync();
        try
        {
            string atom = await httpClient.GetStringAsync($"https://github.com/{owner}/{repo}/commits/main.atom");
            string? commit = GithubFiles.CommitFromAtom(atom);

            if (commit == null)
                throw new InvalidOperationException($"No commit for {owner}/{repo}");

            return commit;
        }
        finally
        {
            commitSlots.Release();
        }
    }

    // reuse request instead of always starting a new one
    public Task LoadChangelogAsync(OnlineModInfo mod)
    {
        if (mod.ChangelogTask == null)
            mod.ChangelogTask = FetchChangelogAsync(mod);

        return mod.ChangelogTask;
    }

    private async Task FetchChangelogAsync(OnlineModInfo mod)
    {
        try
        {
            var releases = await httpClient.GetFromJsonAsync<List<GitHubRelease>>(
                $"https://api.github.com/repos/{mod.RepoOwner}/{mod.RepoName}/releases?per_page=30") ?? new();

            mod.Downloads = releases.Sum(release => release.assets?.Sum(asset => asset.download_count) ?? 0);
            mod.LastUpdated = releases.Max(release => release.published_at);

            foreach (var release in releases)
            {
                if (Version.TryParse(release.tag_name.TrimStart('v'), out var version))
                    mod.Changelog.Add(new ChangelogEntry(version, release.body ?? ""));
            }
        }
        catch (Exception ex)
        {
            // for retrying the changelog fetch later
            mod.ChangelogTask = null;
            Console.WriteLine($"Changelog failed for {mod.Name}: {ex.Message}");
        }
    }

    public async Task DownloadModAsync(string url, string destination, IProgress<double>? progress = null)
    {
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;

        await using var contentStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = File.Create(destination);

        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
            totalRead += bytesRead;

            if (totalBytes.HasValue && totalBytes.Value > 0)
            {
                progress?.Report((double)totalRead / totalBytes.Value * 100);
            }
        }
    }

    public async Task LoadThumbnailAsync(OnlineModInfo mod)
    {
        if (mod.ThumbnailImage != null)
            return;

        if (string.IsNullOrWhiteSpace(mod.ThumbnailUrl))
        {
            mod.ThumbnailImage = LoadDefaultThumbnail(mod);
            return;
        }

        try
        {
            byte[] bytes = await httpClient.GetByteArrayAsync(mod.ThumbnailUrl);

            using var contentStream = new MemoryStream(bytes);
            mod.ThumbnailImage = Bitmap.DecodeToWidth(contentStream, 192);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Thumbnail loading failed for {mod.Name}: {ex.Message}");

            mod.ThumbnailImage = LoadDefaultThumbnail(mod);
        }
    }

    public async Task LoadGalleryAsync(OnlineModInfo mod)
    {
        var urls = new[] { mod.ThumbnailUrl }
        .Concat(mod.ImageUrls)
        .Where(url => !string.IsNullOrEmpty(url))
        .Distinct()
        .ToList();


        if (mod.GalleryLoaded || urls.Count == 0)
            return;
        mod.GalleryLoaded = true;

        foreach (string url in urls)
        {
            try
            {
                byte[] bytes = await httpClient.GetByteArrayAsync(url);
                using var contentStream = new MemoryStream(bytes);
                mod.GalleryImages.Add(Bitmap.DecodeToWidth(contentStream, 1280));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Gallery image failed for {mod.Name}: {ex.Message}");
            }
        }
    }

    private class RepositoryResponse
    {
        [JsonPropertyName("mods")]
        public List<ModRegistryEntry> Mods { get; set; } = new();
    }
}