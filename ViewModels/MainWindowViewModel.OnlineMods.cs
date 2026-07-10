using CommunityToolkit.Mvvm.Input;
using StellarModManager.Models;
using StellarModManager.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
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
    }

    [RelayCommand]
    private async Task RefreshMods()
    {
        var mods = await repositoryService.GetModsAsync(
            "https://raw.githubusercontent.com/jollyname/StellarModRepository/main/mods.json"
        );

        OnlineMods.Clear();

        foreach (var mod in mods)
        {
            OnlineMods.Add(mod);
        }

        RefreshUpdateStatuses();
    }
}