using StellarModManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace StellarModManager.Services;

public class ModRepositoryService
{
    private readonly HttpClient httpClient = new();

    public async Task<List<OnlineModInfo>> GetModsAsync(string url)
    {
        string json = await httpClient.GetStringAsync(url);

        RepositoryResponse? response = JsonSerializer.Deserialize<RepositoryResponse>(json);

        if (response == null)
            return new();

        List<OnlineModInfo> mods = new();

        foreach (ModRegistryEntry entry in response.Mods)
        {
            try
            {
                string metadataUrl = $"https://raw.githubusercontent.com/{entry.Author}/{entry.Repo}/main/{entry.MetadataPath}/mod.json";

                string modJson = await httpClient.GetStringAsync(metadataUrl);

                OnlineModInfo? mod = JsonSerializer.Deserialize<OnlineModInfo>(modJson);

                if (mod != null)
                {
                    mod.RepoName = entry.Repo;

                    mod.DownloadUrl = $"https://github.com/{entry.Author}/{entry.Repo}/releases/download/v{mod.Version}/{entry.Repo}.zip";

                    mods.Add(mod);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load mod from {entry.Repo}: {ex.Message}");
            }
        }

        return mods;
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

    private class RepositoryResponse
    {
        [JsonPropertyName("mods")]
        public List<ModRegistryEntry> Mods { get; set; } = new();
    }
}