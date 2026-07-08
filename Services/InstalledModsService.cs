using StellarModManager.Models;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace StellarModManager.Services;

public class InstalledModsService
{
    public List<InstalledModInfo> GetInstalledMods(string libraryPath)
    {
        List<InstalledModInfo> mods = new();

        if (!Directory.Exists(libraryPath))
            return mods;

        foreach (string modFolder in Directory.GetDirectories(libraryPath))
        {
            string folderName = Path.GetFileName(modFolder);
            string jsonPath = Path.Combine(modFolder, $"{folderName}.json");

            if (!File.Exists(jsonPath))
                continue;

            string json = File.ReadAllText(jsonPath);
            InstalledModInfo? mod = JsonSerializer.Deserialize<InstalledModInfo>(json);

            if (mod != null)
                mods.Add(mod);
        }

        return mods;
    }
}