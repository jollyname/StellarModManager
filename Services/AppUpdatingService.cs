using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Threading.Tasks;

namespace StellarModManager.Services;

public class AppUpdatingService
{
    private const string RepoApi = "https://api.github.com/repos/jollyname/StellarModManager/releases/latest";

    public async Task<UpdateInfo?> CheckAsync()
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("StellarModManager");

        var release = await client.GetFromJsonAsync<GitHubRelease>(RepoApi);
        if (release is null) return null;

        var latest = Version.Parse(release.tag_name.TrimStart('v'));
        var current = Assembly.GetExecutingAssembly().GetName().Version!;

        if (latest <= current) return null;

        return new UpdateInfo(latest, release.assets, release.body);
    }

    public async Task DownloadAndInstallUpdateAsync(UpdateInfo update, IProgress<double> progress)
    {
        GitHubAsset? installerAsset = null;

        foreach (var asset in update.Assets)
        {
            string nameLower = asset.name.ToLower();

            if (nameLower.EndsWith(".exe") && nameLower.Contains("setup"))
            {
                installerAsset = asset;
                break;
            }
        }

        string tempFilePath = Path.Combine(Path.GetTempPath(), installerAsset!.name);

        using var client = new HttpClient();
        using var response = await client.GetAsync(installerAsset.browser_download_url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? 0;

        using var httpStream = await response.Content.ReadAsStreamAsync();
        using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write);

        byte[] buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await httpStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead);
            totalRead += bytesRead;

            if (totalBytes > 0)
            {
                double percent = (double)totalRead / totalBytes * 100;
                progress.Report(percent);
            }
        }

        fileStream.Close();

        Process.Start(new ProcessStartInfo(tempFilePath) { UseShellExecute = true });
    }
}