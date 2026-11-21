using StellarModManager.Models;
using System;
using System.IO;
using System.Text.Json;

namespace StellarModManager.Services;

public class SettingsService
{
    private readonly string _gameProfilePath = Path.Combine(AppContext.BaseDirectory, "Data", "settings.json");
    private readonly string _appSettingsPath = Path.Combine(AppContext.BaseDirectory, "Data", "appsettings.json");

    public void SaveGameProfile(GameProfile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_gameProfilePath)!);
        string json = JsonSerializer.Serialize(profile);
        File.WriteAllText(_gameProfilePath, json);
    }

    public GameProfile? LoadGameProfile()
    {
        if (!File.Exists(_gameProfilePath))
            return null;

        string json = File.ReadAllText(_gameProfilePath);
        return JsonSerializer.Deserialize<GameProfile>(json);
    }

    public void SaveAppSettings(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_appSettingsPath)!);
        string json = JsonSerializer.Serialize(settings);
        File.WriteAllText(_appSettingsPath, json);
    }

    public AppSettings LoadAppSettings()
    {
        if (!File.Exists(_appSettingsPath))
            return new AppSettings();

        string json = File.ReadAllText(_appSettingsPath);
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }
}