using System;
using System.IO;

namespace StellarModManager.Services;

internal static class AppPaths
{
    internal static string UserDataDirectory { get; } = 
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StellarModManager");
}