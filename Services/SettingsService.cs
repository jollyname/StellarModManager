using StellarModManager.Models;
using System;
using System.IO;
using System.Text.Json;

namespace StellarModManager.Services;

public class SettingsService
{
    private readonly string filePath = Path.Combine(AppContext.BaseDirectory, "Data", "settings.json");

    public void SaveGameProfile(GameProfile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        string json = JsonSerializer.Serialize(profile);

        File.WriteAllText(filePath, json);
    }

    public GameProfile? LoadGameProfile()
    {
        if (!File.Exists(filePath))
            return null;

        string json = File.ReadAllText(filePath);

        return JsonSerializer.Deserialize<GameProfile>(json);
    }
}