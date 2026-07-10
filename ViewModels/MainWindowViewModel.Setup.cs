using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarModManager.Models;
using StellarModManager.Services;
using System.Threading.Tasks;

namespace StellarModManager.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private bool setupRequired = true;

    [ObservableProperty]
    private bool isMelonLoaderValid;

    [ObservableProperty]
    private bool isInstallingMelonLoader;

    [ObservableProperty]
    private string installProgressText = "";

    private readonly MelonLoaderInstaller melonLoaderInstaller = new();

    partial void OnGamePathChanged(string value) => InstallMelonLoaderCommand.NotifyCanExecuteChanged();

    partial void OnIsMelonLoaderValidChanged(bool value)
    {
        CloseSetupCommand.NotifyCanExecuteChanged();
        InstallMelonLoaderCommand.NotifyCanExecuteChanged();
        ApplyAutoCheckTimerState();
    }

    partial void OnIsInstallingMelonLoaderChanged(bool value) => InstallMelonLoaderCommand.NotifyCanExecuteChanged();

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
            var progress = new System.Progress<string>(text => InstallProgressText = text);

            await melonLoaderInstaller.InstallAsync(GamePath, progress);

            IsMelonLoaderValid = CheckMelonLoader(GamePath);

            if (IsMelonLoaderValid)
            {
                settingsService.SaveGameProfile(new GameProfile { GamePath = GamePath });
            }
        }
        catch (System.Exception ex)
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
}