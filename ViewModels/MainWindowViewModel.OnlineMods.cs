using CommunityToolkit.Mvvm.Input;
using StellarModManager.Models;
using StellarModManager.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace StellarModManager.ViewModels;

public partial class MainWindowViewModel
{
    public ObservableCollection<OnlineModInfo> OnlineMods { get; } = new();
    private readonly ModRepositoryService repositoryService = new();
    private readonly ModInstallerService installerService = new();

    [RelayCommand]
    private async Task InstallMod(OnlineModInfo mod)
    {
        mod.IsInstalling = true;
        mod.DownloadProgress = 0;

        try
        {
            string downloads = Path.Combine(AppContext.BaseDirectory, "Downloads");
            Directory.CreateDirectory(downloads);

            string zipFile = Path.Combine(downloads, $"{mod.Id}.zip");
            var progress = new Progress<double>(pct => mod.DownloadProgress = pct);

            await repositoryService.DownloadModAsync(mod.DownloadUrl, zipFile, progress);

            string libraryPath = Path.Combine(AppContext.BaseDirectory, "Library", mod.Id);
            await installerService.InstallAsync(zipFile, libraryPath);

            mod.IsInstalled = true;

            File.Delete(zipFile);

            MelonLoaderStatusText = $"{mod.Name} installed";
        }
        catch (Exception ex)
        {
            MelonLoaderStatusText = $"Install failed: {ex.Message}";
        }
        finally
        {
            mod.IsInstalling = false;
        }

        LoadInstalledMods();
        _ = RefreshMods();
    }

    [RelayCommand]
    private async Task RefreshMods()
    {
        var mods = await repositoryService.GetModsAsync(
            "https://raw.githubusercontent.com/jollyname/StellarModRepository/main/mods.json"
        );

        foreach (var mod in mods)
        {
            mod.IsInstalled = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "Library", mod.Id));

            var existing = OnlineMods.FirstOrDefault(x => x.Id == mod.Id);

            if (existing != null)
            {
                existing.Name = mod.Name;
                existing.Author = mod.Author;
                existing.Version = mod.Version;
                existing.Description = mod.Description;
                existing.DownloadUrl = mod.DownloadUrl;
                existing.IsInstalled = mod.IsInstalled;
            }
            else
            {
                OnlineMods.Add(mod);
            }
        }

        RefreshUpdateStatuses();
    }
}