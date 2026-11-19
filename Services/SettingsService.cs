using StellarModManager.Models;
using System.Collections.Generic;
using System;
using System.IO;
using System.Text.Json;

namespace StellarModManager.Services;

public class SettingsService
{
    private readonly string gameProfilePath = Path.Combine(AppContext.BaseDirectory, "Data", "settings.json");
    private readonly string appSettingsPath = Path.Combine(AppContext.BaseDirectory, "Data", "appsettings.json");
    private readonly string modProfilesPath = Path.Combine(AppContext.BaseDirectory, "Data", "modprofiles.json");

    private class ModProfilesFile
    {
        public string ActiveProfile { get; set; } = "Default";
        public List<ModProfile> Profiles { get; set; } = new();
    }

    public void SaveModProfiles(List<ModProfile> profiles, string activeProfileName)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(modProfilesPath)!);
        var data = new ModProfilesFile { ActiveProfile = activeProfileName, Profiles = profiles };
        File.WriteAllText(modProfilesPath, JsonSerializer.Serialize(data));
    }

    public (List<ModProfile> profiles, string activeProfile) LoadModProfiles()
    {
        if (!File.Exists(modProfilesPath))
            return (new List<ModProfile>(), "Default");

        string json = File.ReadAllText(modProfilesPath);
        var data = JsonSerializer.Deserialize<ModProfilesFile>(json);
        return (data?.Profiles ?? new List<ModProfile>(), data?.ActiveProfile ?? "Default");
    }

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