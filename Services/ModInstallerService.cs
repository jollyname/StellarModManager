using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace StellarModManager.Services;

public class ModInstallerService
{
    public async Task InstallAsync(string zipPath, string libraryPath)
    {
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException("Mod zip not found", zipPath);
        }

        if (Directory.Exists(libraryPath))
        {
            Directory.Delete(libraryPath, true);
        }

        Directory.CreateDirectory(libraryPath);

        ZipFile.ExtractToDirectory(zipPath, libraryPath);

        string? modJson =Directory.GetFiles(libraryPath, "mod.json", SearchOption.AllDirectories).FirstOrDefault();

        if (modJson == null)
        {
            throw new Exception("Invalid mod package: mod.json missing");
        }

        string modRoot =Path.GetDirectoryName(modJson)!;

        if (modRoot != libraryPath)
        {
            foreach (string item in Directory.GetFileSystemEntries(modRoot))
            {
                string destination =Path.Combine(libraryPath, Path.GetFileName(item));

                if (Directory.Exists(item))
                {
                    Directory.Move(item, destination);
                }
                else
                {
                    File.Move(item, destination);
                }
            }

            Directory.Delete(modRoot, true);
        }

        await Task.CompletedTask;
    }
}