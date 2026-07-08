using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarModManager.Models;
using StellarModManager.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace StellarModManager.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string gamePath = "No game selected";

    [ObservableProperty]
    private string melonLoaderStatusText = "";

    private readonly SettingsService settingsService = new();

    public ObservableCollection<InstalledModInfo> InstalledMods { get; } = new();
    private readonly InstalledModsService installedModsService = new();

    public ObservableCollection<OnlineModInfo> OnlineMods { get; } = new();
    private readonly ModRepositoryService repositoryService = new();
    private readonly ModInstallerService installerService = new();
    private readonly ModDeploymentService deploymentService = new();

    private readonly MelonLoaderInstaller melonLoaderInstaller = new();

    [ObservableProperty]
    private bool setupRequired = true;

    [ObservableProperty]
    private bool isMelonLoaderValid;

    [ObservableProperty]
    private bool isInstallingMelonLoader;

    [ObservableProperty]
    private string installProgressText = "";

    public MainWindowViewModel()
    {
        var profile = settingsService.LoadGameProfile();

        if (profile != null)
        {
            GamePath = profile.GamePath;

            var detector = new MelonLoaderDetector();
            var result = detector.CheckInstallation(profile.GamePath);

            if (result == MelonLoaderStatus.Installed)
            {
                IsMelonLoaderValid = true;
                SetupRequired = false;

                LoadInstalledMods();
                _ = RefreshMods();
            }
            else
            {
                MelonLoaderStatusText = "Previously saved game path is no longer valid";
            }
        }
    }

    partial void OnIsMelonLoaderValidChanged(bool value)
    {
        CloseSetupCommand.NotifyCanExecuteChanged();
        InstallMelonLoaderCommand.NotifyCanExecuteChanged();
    }

    partial void OnGamePathChanged(string value) => InstallMelonLoaderCommand.NotifyCanExecuteChanged();

    partial void OnIsInstallingMelonLoaderChanged(bool value) => InstallMelonLoaderCommand.NotifyCanExecuteChanged();

    private void LoadInstalledMods()
    {
        string libraryPath = Path.Combine(AppContext.BaseDirectory, "Library");

        InstalledMods.Clear();

        var mods = installedModsService.GetInstalledMods(libraryPath);

        foreach (var mod in mods)
        {
            InstalledMods.Add(mod);
        }

        RefreshUpdateStatuses();
    }

    // Check for both id and version.
    private void RefreshUpdateStatuses()
    {
        foreach (var installed in InstalledMods)
        {
            var online = OnlineMods.FirstOrDefault(m => m.Id == installed.Id);

            if (online != null &&
                Version.TryParse(online.Version, out var onlineV) &&
                Version.TryParse(installed.Version, out var installedV) &&
                onlineV > installedV)
            {
                installed.IsUpdateAvailable = true;
                installed.LatestVersion = online.Version;
            }
            else
            {
                installed.IsUpdateAvailable = false;
                installed.LatestVersion = null;
            }
        }
    }

    private bool CheckMelonLoader(string path)
    {
        var detector = new MelonLoaderDetector();
        var result = detector.CheckInstallation(path);

        MelonLoaderStatusText = result switch
        {
            MelonLoaderStatus.Installed => "MelonLoader is installed",
            MelonLoaderStatus.NotInstalled => "MelonLoader is not installed",
            _ => "Could not read that folder"
        };

        return result == MelonLoaderStatus.Installed;
    }

    [RelayCommand]
    private async Task SelectGameFolder()
    {
        Window? window = null;
        if (App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            window = desktop.MainWindow;
        }

        if (window == null)
            return;

        var folders = await window.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Select Unity Game Folder",
                AllowMultiple = false
            });

        if (folders.Count == 0)
            return;

        string path = folders[0].Path.LocalPath;

        GamePath = path;

        bool valid = CheckMelonLoader(path);
        IsMelonLoaderValid = valid;

        if (!valid)
            return;

        settingsService.SaveGameProfile(new GameProfile { GamePath = path });
    }

    private bool CanInstallMelonLoader() => GamePath != "No game selected" && !IsMelonLoaderValid && !IsInstallingMelonLoader;

    [RelayCommand(CanExecute = nameof(CanInstallMelonLoader))]
    private async Task InstallMelonLoader()
    {
        IsInstallingMelonLoader = true;
        InstallProgressText = "Starting...";

        try
        {
            var progress = new Progress<string>(text => InstallProgressText = text);

            await melonLoaderInstaller.InstallAsync(GamePath, progress);

            IsMelonLoaderValid = CheckMelonLoader(GamePath);

            if (IsMelonLoaderValid)
            {
                settingsService.SaveGameProfile(new GameProfile { GamePath = GamePath });
            }
        }
        catch (Exception ex)
        {
            MelonLoaderStatusText = $"Auto-install failed: {ex.Message}"; // Hopesfully this never happens... ijdsfjipfdsipjhfdfdf
        }
        finally
        {
            IsInstallingMelonLoader = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsMelonLoaderValid))]
    private void CloseSetup()
    {
        SetupRequired = false;

        LoadInstalledMods();
        _ = RefreshMods();
    }

    [RelayCommand]
    private async Task DeployMod(InstalledModInfo mod)
    {
        if (GamePath == "No game selected")
        {
            MelonLoaderStatusText = "Select a game first";
            return;
        }

        mod.IsDeploying = true;
        mod.DeployProgress = 0;

        try
        {
            string libraryPath = Path.Combine(AppContext.BaseDirectory, "Library", mod.Id);
            var progress = new Progress<double>(pct => mod.DeployProgress = pct);

            await Task.Run(() => deploymentService.DeployMod(libraryPath, GamePath, progress));

            MelonLoaderStatusText = $"{mod.Name} copied to game";
        }
        catch (Exception ex)
        {
            MelonLoaderStatusText = $"Deploy failed: {ex.Message}";
        }
        finally
        {
            mod.IsDeploying = false;
        }
    }

    [RelayCommand]
    private async Task RemoveMod(InstalledModInfo mod)
    {
        mod.IsRemoving = true;

        try
        {
            string libraryPath = Path.Combine(AppContext.BaseDirectory, "Library", mod.Id);

            await Task.Run(() =>
            {
                if (GamePath != "No game selected")
                {
                    deploymentService.RemoveDeployedFiles(libraryPath, GamePath);
                }

                if (Directory.Exists(libraryPath))
                {
                    Directory.Delete(libraryPath, true);
                }
            });

            InstalledMods.Remove(mod);
            MelonLoaderStatusText = $"{mod.Name} removed";
        }
        catch (Exception ex)
        {
            MelonLoaderStatusText = $"Remove failed: {ex.Message}";
        }
        finally
        {
            mod.IsRemoving = false;
        }
    }

    [RelayCommand]
    private async Task UpdateMod(InstalledModInfo mod)
    {
        OnlineModInfo? onlineMatch = OnlineMods.FirstOrDefault(m => m.Id == mod.Id);

        if (onlineMatch == null)
        {
            MelonLoaderStatusText = "Could not find this mod in the online repository";
            return;
        }

        mod.IsUpdating = true;
        mod.UpdateProgress = 0;

        try
        {
            string libraryPath = Path.Combine(AppContext.BaseDirectory, "Library", mod.Id);

            // Clean out whatever the old version placed in the game folder first since file layouts might change between mod versions.
            if (GamePath != "No game selected")
            {
                await Task.Run(() => deploymentService.RemoveDeployedFiles(libraryPath, GamePath));
            }

            string downloads = Path.Combine(AppContext.BaseDirectory, "Downloads");
            Directory.CreateDirectory(downloads);

            string zipFile = Path.Combine(downloads, $"{mod.Id}.zip");
            var progress = new Progress<double>(pct => mod.UpdateProgress = pct);

            await repositoryService.DownloadModAsync(onlineMatch.DownloadUrl, zipFile, progress);
            await installerService.InstallAsync(zipFile, libraryPath);

            File.Delete(zipFile);

            MelonLoaderStatusText = $"{mod.Name} updated to {onlineMatch.Version}. Install it to your game again to update the deployed copy.";
        }
        catch (Exception ex)
        {
            MelonLoaderStatusText = $"Update failed: {ex.Message}";
        }
        finally
        {
            mod.IsUpdating = false;
        }

        LoadInstalledMods();
    }

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