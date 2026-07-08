using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace StellarModManager.Services;

public enum GameArchitecture
{
    Unknown,
    X86,
    X64
}

public class MelonLoaderInstaller
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/LavaGang/MelonLoader/releases/latest";

    public async Task InstallAsync(string gamePath, IProgress<string>? progress = null)
    {
        progress?.Report("Locating game executable...");

        string? exePath = FindGameExecutable(gamePath);

        if (exePath == null)
        {
            throw new InvalidOperationException(
                "Could not find the game's executable in the selected folder.");
        }

        GameArchitecture architecture = DetectArchitecture(exePath);

        if (architecture == GameArchitecture.Unknown)
        {
            throw new InvalidOperationException(
                "Could not determine whether the game is 32-bit or 64-bit.");
        }

        progress?.Report(
            $"Detected {(architecture == GameArchitecture.X64 ? "64-bit" : "32-bit")} game. " +
            "Fetching latest MelonLoader release...");

        string assetName = architecture == GameArchitecture.X64
            ? "MelonLoader.x64.zip"
            : "MelonLoader.x86.zip";

        string downloadUrl = await GetLatestReleaseAssetUrlAsync(assetName);

        string tempZip = Path.Combine(Path.GetTempPath(), $"MelonLoader_{Guid.NewGuid():N}.zip");

        try
        {
            progress?.Report("Downloading MelonLoader...");
            await DownloadFileAsync(downloadUrl, tempZip);

            progress?.Report("Installing MelonLoader into game folder...");
            ZipFile.ExtractToDirectory(tempZip, gamePath, overwriteFiles: true);

            progress?.Report("MelonLoader installed successfully.");
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }
        }
    }

    private static string? FindGameExecutable(string gamePath)
    {
        if (!Directory.Exists(gamePath))
            return null;

        // Check for the "<ProductName>_Data" folder to find the game executable.
        foreach (var exe in Directory.GetFiles(gamePath, "*.exe"))
        {
            string nameWithoutExtension = Path.GetFileNameWithoutExtension(exe);
            string dataFolder = Path.Combine(gamePath, $"{nameWithoutExtension}_Data");

            if (Directory.Exists(dataFolder))
            {
                return exe;
            }
        }

        return null;
    }

    private static GameArchitecture DetectArchitecture(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs);

            fs.Seek(0x3C, SeekOrigin.Begin);
            int peOffset = reader.ReadInt32();

            fs.Seek(peOffset + 4, SeekOrigin.Begin); // Skip "PE" (4 bytes) to get to Machine
            ushort machine = reader.ReadUInt16();

            return machine switch
            {
                0x8664 => GameArchitecture.X64,
                0x014c => GameArchitecture.X86,
                _ => GameArchitecture.Unknown
            };
        }
        catch { return GameArchitecture.Unknown; }
    }

    private static async Task<string> GetLatestReleaseAssetUrlAsync(string assetName)
    {
        using var client = new HttpClient();

        client.DefaultRequestHeaders.UserAgent.ParseAdd("StellarModManager");

        string json = await client.GetStringAsync(LatestReleaseUrl);

        using var doc = JsonDocument.Parse(json);
        var assets = doc.RootElement.GetProperty("assets");

        foreach (var asset in assets.EnumerateArray())
        {
            string? name = asset.GetProperty("name").GetString();

            if (string.Equals(name, assetName, StringComparison.OrdinalIgnoreCase))
            {
                return asset.GetProperty("browser_download_url").GetString() ?? throw new InvalidOperationException("Release asset had no download URL.");
            }
        }

        throw new InvalidOperationException($"Could not find an asset named '{assetName}' in the latest MelonLoader release.");
    }

    private static async Task DownloadFileAsync(string url, string destinationPath)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("StellarModManager");

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var contentStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = File.Create(destinationPath);
        await contentStream.CopyToAsync(fileStream);
    }
}