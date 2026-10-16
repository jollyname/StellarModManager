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
    private readonly SemaphoreSlim releaseSlots = new(4);
    private readonly DownloadCountStore downloadCounts = new(DownloadCountStore.DefaultPath);
    private readonly HashSet<string> fetchedReleases = new(StringComparer.OrdinalIgnoreCase);
    private readonly object limitGate = new();
    private DateTime blockedUntil;

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

    // request no cache (even tho im almost certain this doesnt work, we still try...)
    private static string NoCache(string url) => $"{url}?t={DateTime.UtcNow.Ticks}";

    public async Task<List<OnlineModInfo>> GetModsAsync(string url)
    {
        string json = await httpClient.GetStringAsync(NoCache(url));

        RepositoryResponse? response = JsonSerializer.Deserialize<RepositoryResponse>(json);

        if (response == null)
            return new();

        //load every mod at once, to speed up request
        OnlineModInfo?[] mods = await Task.WhenAll(response.Mods.Select(LoadModAsync));

        return mods.OfType<OnlineModInfo>().ToList();
    }

    private static readonly Regex GitHubName = new(@"^[A-Za-z0-9][A-Za-z0-9._-]*$");
    private static readonly Regex RepoPath = new(@"^([A-Za-z0-9_-][A-Za-z0-9._-]*(/[A-Za-z0-9_-][A-Za-z0-9._-]*)*)?$");

    private static bool IsSafe(ModRegistryEntry entry)
    {
        return GitHubName.IsMatch(entry.Author) && GitHubName.IsMatch(entry.Repo) && RepoPath.IsMatch(entry.MetadataPath);
    }

    private async Task<OnlineModInfo?> LoadModAsync(ModRegistryEntry entry)
    {
        if (!IsSafe(entry))
            return null;

        try
        {
            string metadataUrl = $"https://raw.githubusercontent.com/{entry.Author}/{entry.Repo}/main/{entry.MetadataPath}/mod.json";

            string modJson = await httpClient.GetStringAsync(NoCache(metadataUrl));

            OnlineModInfo? mod = JsonSerializer.Deserialize<OnlineModInfo>(modJson);

            if (mod == null)
                return null;

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
            string baseUrl = $"https://raw.githubusercontent.com/{entry.Author}/{entry.Repo}/main/{entry.MetadataPath}/";
            string ToUrl(string path) => $"{baseUrl}{path}";

            if (!string.IsNullOrWhiteSpace(mod.Thumbnail))
            {
                mod.ThumbnailUrl = ToUrl(mod.Thumbnail);
            }

            mod.ImageUrls = mod.Images.Where(path => !string.IsNullOrWhiteSpace(path)).Select(ToUrl).ToList();

            return mod;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load mod from {entry.Repo}: {ex.Message}");
            return null;
        }
    }

    // Reuse a finished request. A rate-limit response keeps the saved counts and does not retry until GitHub allows it.
    public Task LoadChangelogAsync(OnlineModInfo mod)
    {
        downloadCounts.Apply(mod);

        if (IsBlocked())
            return Task.CompletedTask;

        string key = mod.RepoOwner + "/" + mod.RepoName;

        lock (fetchedReleases)
        {
            if (fetchedReleases.Contains(key))
                return Task.CompletedTask;

            if (mod.ChangelogTask is { IsCompleted: false })
                return mod.ChangelogTask;

            mod.ChangelogTask = FetchChangelogAsync(mod, key);
            return mod.ChangelogTask;
        }
    }

    public void InvalidateChangelog(OnlineModInfo mod)
    {
        lock (fetchedReleases)
            fetchedReleases.Remove(mod.RepoOwner + "/" + mod.RepoName);

        mod.ChangelogTask = null;
    }

    private bool IsBlocked()
    {
        lock (limitGate)
            return DateTime.UtcNow < blockedUntil;
    }

    private void Block(DateTime until)
    {
        lock (limitGate)
        {
            if (until > blockedUntil)
                blockedUntil = until;
        }
    }

    private async Task FetchChangelogAsync(OnlineModInfo mod, string key)
    {
        if (IsBlocked())
            return;

        await releaseSlots.WaitAsync();

        try
        {
            if (IsBlocked())
                return;

            using HttpResponseMessage response = await httpClient.GetAsync(
                $"https://api.github.com/repos/{mod.RepoOwner}/{mod.RepoName}/releases?per_page=30");

            if (ReleaseCounts.IsLimited(response))
            {
                Block(ReleaseCounts.BlockedUntil(response, DateTime.UtcNow));
                Console.WriteLine($"GitHub rate limit hit for {mod.Name}; keeping saved download counts.");
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Changelog failed for {mod.Name}: {(int)response.StatusCode}");
                return;
            }

            List<GitHubRelease> releases = await response.Content.ReadFromJsonAsync<List<GitHubRelease>>() ?? new();
            mod.Downloads = ReleaseCounts.Total(releases);
            mod.LastUpdated = ReleaseCounts.Latest(releases);
            mod.Changelog.Clear();

            foreach (GitHubRelease release in releases)
            {
                if (!string.IsNullOrWhiteSpace(release.tag_name)
                    && Version.TryParse(release.tag_name.TrimStart('v'), out Version? version))
                {
                    mod.Changelog.Add(new ChangelogEntry(version, release.body ?? ""));
                }
            }

            downloadCounts.Remember(mod);

            lock (fetchedReleases)
                fetchedReleases.Add(key);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Changelog failed for {mod.Name}: {ex.Message}");
        }
        finally
        {
            releaseSlots.Release();
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
            byte[] bytes = await httpClient.GetByteArrayAsync(
                NoCache(mod.ThumbnailUrl));

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
                byte[] bytes = await httpClient.GetByteArrayAsync(NoCache(url));
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