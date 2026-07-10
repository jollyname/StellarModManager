using StellarModManager.Models;
using System;
using System.IO;
using System.Text.Json;

namespace StellarModManager.Services;

public class SettingsService
{
    private readonly string gameProfilePath = Path.Combine(AppContext.BaseDirectory, "Data", "settings.json");
    private readonly string appSettingsPath = Path.Combine(AppContext.BaseDirectory, "Data", "appsettings.json");

    public void SaveGameProfile(GameProfile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(gameProfilePath)!);
        string json = JsonSerializer.Serialize(profile);
        File.WriteAllText(gameProfilePath, json);
    }

    public GameProfile? LoadGameProfile()
    {
        if (!File.Exists(gameProfilePath))
            return null;

        string json = File.ReadAllText(gameProfilePath);
        return JsonSerializer.Deserialize<GameProfile>(json);
    }

    public void SaveAppSettings(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(appSettingsPath)!);
        string json = JsonSerializer.Serialize(settings);
        File.WriteAllText(appSettingsPath, json);
    }

    public AppSettings LoadAppSettings()
    {
        if (!File.Exists(appSettingsPath))
            return new AppSettings();

        string json = File.ReadAllText(appSettingsPath);
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }
}