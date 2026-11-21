using StellarModManager.Models;
using System.IO;
using System.Text.Json;

namespace StellarModManager.Services;

public class SettingsService
{
    public void SaveGameProfile(GameProfile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.GameProfilePath)!);
        string json = JsonSerializer.Serialize(profile);
        File.WriteAllText(_gameProfilePath, json);
    }

    public GameProfile? LoadGameProfile()
    {
        if (!File.Exists(AppPaths.GameProfilePath))
            return null;

        string json = File.ReadAllText(AppPaths.GameProfilePath);
        return JsonSerializer.Deserialize<GameProfile>(json);
    }

    public void SaveAppSettings(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.AppSettingsPath)!);
        string json = JsonSerializer.Serialize(settings);
        File.WriteAllText(AppPaths.AppSettingsPath, json);
    }

    public AppSettings LoadAppSettings()
    {
        if (!File.Exists(AppPaths.AppSettingsPath))
            return new AppSettings();

        string json = File.ReadAllText(AppPaths.AppSettingsPath);
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }
}