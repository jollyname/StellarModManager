using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace StellarModManager.Services;


public static partial class SteamInfoService
{
    private const string GamesDirectory = "steamapps/common/";
    private const string LibraryVdfPath = "steamapps/libraryfolders.vdf";


    [SupportedOSPlatform("windows")]
    public static readonly (RegistryKey root, string subKey, string valueName)[] RegistryPaths =
    [
        (Registry.CurrentUser, @"SOFTWARE\Valve\Steam", "SteamPath"),
        (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath")
    ];

    [GeneratedRegex("""(?:"path")(?:\s*)(?:")(?<path>.*)(?:")""")]
    private static partial Regex PathExtractionRegex();

    [GeneratedRegex("""(?:"appid")(?:\s*)(?:")(?<appid>.*)(?:")""")]
    private static partial Regex AppIdExtractionRegex();

    [GeneratedRegex("""(?:"installdir")(?:\s*)(?:")(?<directory>.*)(?:")""")]
    private static partial Regex DirectoryExtractionRegex();


    public static IEnumerable<DirectoryInfo> GetSteamGames()
    {
        return GetSteamLibraries()
            .Select(d => new DirectoryInfo(Path.Combine(d.FullName, GamesDirectory)))
            .Where(d => d.Exists)
            .SelectMany(d => d.GetDirectories())
            .DistinctBy(d => d.FullName);
    }

    public static IEnumerable<DirectoryInfo> GetSteamLibraries()
    {
        var potentialSteamPaths = GetSteamPathFromRegistry();

        string? fileContent = null;
        foreach (var PotentialSteam in potentialSteamPaths)
        {
            if (!PotentialSteam.Exists) continue;

            var filePath = Path.Combine(PotentialSteam.FullName, LibraryVdfPath);

            if (Path.Exists(filePath))
            {
                fileContent = File.ReadAllText(filePath);
                break;
            }
        }
        if (fileContent is null) return [];

        var gamePaths = PathExtractionRegex()
            .Matches(fileContent)
            .Select(m => new DirectoryInfo(m.Groups["path"].Value.Replace(@"\\", @"\")))
            .Where(d => d.Exists)
            .DistinctBy(d => d.FullName);

        return gamePaths;
    }


    public static string? GetAppId(DirectoryInfo directory)
    {
        var appmanifests = GetSteamLibraries()
            .SelectMany(d => 
                Directory.EnumerateFiles
                (
                    path: Path.Combine(d.FullName, "steamapps"), 
                    searchPattern: "appmanifest_*.acf"
                )
            );

        foreach (var appmanifest in appmanifests)
        {
            string fileContent;

            try
            {
                fileContent = File.ReadAllText(appmanifest);
            } 
            catch(Exception e)
            {
                Console.WriteLine(e);
                continue;
            }

            var directoryMatch = DirectoryExtractionRegex().Match(fileContent);
            if (!directoryMatch.Groups.TryGetValue("directory", out var directoryGroup)) continue;
            if (directoryGroup.Value != directory.Name) continue;

            var appIdMatch = AppIdExtractionRegex().Match(fileContent);
            if (!appIdMatch.Groups.TryGetValue("appid", out var appIdGroup)) 
            { 
                Console.WriteLine("Found a fitting appmanifest but failed to extract the appid");
                continue;
            }

            return appIdGroup.Value;
        }

        return null;
    }


    private static List<DirectoryInfo> GetSteamPathFromRegistry()
    {
        if (!OperatingSystem.IsWindows()) return [];

        var result = new List<DirectoryInfo>();
        foreach (var (root, subKey, valueName) in RegistryPaths)
        {
            using var key = root.OpenSubKey(subKey);
            var value = key?.GetValue(valueName);
            
            if (value is string path && Path.Exists(path))
            {
                result.Add(new DirectoryInfo(path));
            }
        }
        return result;
    }
}
