using System.IO;

namespace StellarModManager.Services;

public enum MelonLoaderStatus
{
    Installed,
    NotInstalled
}

public class MelonLoaderDetector
{
    // Idk if this is a good way to check if MelonLoader is installed, but Idc.
    public MelonLoaderStatus CheckInstallation(string gamePath)
    {
        bool melonLoaderFolder = Directory.Exists(Path.Combine(gamePath, "MelonLoader"));

        bool loaderDll = File.Exists(Path.Combine(gamePath, "version.dll"));

        if (melonLoaderFolder && loaderDll)
        {
            return MelonLoaderStatus.Installed;
        }

        return MelonLoaderStatus.NotInstalled;
    }
}