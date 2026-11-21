using System;
using System.IO;

namespace StellarModManager.Services;

internal static class AppPaths
{
    internal static string UserDataDirectory { get; } = 
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StellarModManager");

    internal static string GameProfilePath { get; }
        = Path.Combine(UserDataDirectory, "settings.json");

    internal static string AppSettingsPath { get; } 
        = Path.Combine(UserDataDirectory, "appsettings.json");

    internal static string ModProfilesPath { get; }
        = Path.Combine(UserDataDirectory, "modprofiles.json");

    internal static string ProfilesRoot { get; }
        = Path.Combine(UserDataDirectory, "Profiles");
}