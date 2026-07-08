using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace StellarModManager.Services;

public class ModDeploymentService
{
    private const string ManifestFileName = ".deployment.json";

    public void DeployMod(string installedModPath, string gamePath, IProgress<double>? progress = null)
    {
        var allFiles = Directory.GetFiles(installedModPath, "*", SearchOption.AllDirectories);
        var files = new List<string>();

        foreach (var file in allFiles)
        {
            if (Path.GetFileName(file) != ManifestFileName)
            {
                files.Add(file);
            }
        }

        var relativePaths = new List<string>();
        int copied = 0;

        foreach (string file in files)
        {
            string relativePath = Path.GetRelativePath(installedModPath, file);
            relativePaths.Add(relativePath);

            string destination = Path.Combine(gamePath, relativePath);
            string? directory = Path.GetDirectoryName(destination);

            if (directory != null)
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(file, destination, true);

            copied++;
            progress?.Report(files.Count == 0 ? 100 : (double)copied / files.Count * 100);
        }

        WriteManifest(installedModPath, relativePaths);
    }

    // Removes exactly the files this mod placed in the game folder by looking them up in its manifest.
    public void RemoveDeployedFiles(string installedModPath, string gamePath)
    {
        List<string> relativePaths = ReadManifest(installedModPath);

        foreach (string relativePath in relativePaths)
        {
            string gameFile = Path.Combine(gamePath, relativePath);

            if (File.Exists(gameFile))
            {
                File.Delete(gameFile);
            }

            // Clean up now empty directories, going upward from the deleted file,
            // stopping at the game root so it never deletes outside it.
            string? dir = Path.GetDirectoryName(gameFile);

            while (!string.IsNullOrEmpty(dir) &&
                   dir.StartsWith(gamePath, StringComparison.OrdinalIgnoreCase) &&
                   !dir.Equals(gamePath, StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                    dir = Path.GetDirectoryName(dir);
                }
                else
                {
                    break;
                }
            }
        }

        string manifestPath = Path.Combine(installedModPath, ManifestFileName);

        if (File.Exists(manifestPath))
        {
            File.Delete(manifestPath);
        }
    }

    private static void WriteManifest(string installedModPath, List<string> relativePaths)
    {
        string manifestPath = Path.Combine(installedModPath, ManifestFileName);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(relativePaths));
    }

    private static List<string> ReadManifest(string installedModPath)
    {
        string manifestPath = Path.Combine(installedModPath, ManifestFileName);

        if (!File.Exists(manifestPath))
        {
            return new List<string>();
        }

        string json = File.ReadAllText(manifestPath);
        return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
    }
}