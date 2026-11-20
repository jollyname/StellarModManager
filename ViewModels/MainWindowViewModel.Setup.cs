using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarModManager.Models;
using StellarModManager.Services;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StellarModManager.ViewModels;

public partial class MainWindowViewModel
{
    public IReadOnlyList<GameInstallationInfo> DetectedGameInstalls { get; } = GameLocatorService.Locate();

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
    private void SelectSuggestedFolder(GameInstallationInfo info) => UseGameFolder(info.Directory.FullName);

    [RelayCommand]
    private async Task SelectGameFolder()
    {
        Window? window = null;
        if (App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            window = desktop.MainWindow;
        }

        if (window == null || IsModOperationInProgress)
            return;

        if (!TryBeginModOperation(requireProfile: false))
            return;
        try
        {
            var folders = await window.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Select Unity Game Folder",
                    AllowMultiple = false
                });

            if (folders.Count > 0)
                SetGameFolder(folders[0].Path.LocalPath);
        }
        finally
        {
            IsModOperationInProgress = false;
        }
    }

    private void UseGameFolder(string path)
    {
        if (!TryBeginModOperation(requireProfile: false))
            return;
        try
        {
            SetGameFolder(path);
        }
        finally
        {
            IsModOperationInProgress = false;
        }
    }

    private void SetGameFolder(string path)
    {
        GamePath = path;
        IsMelonLoaderValid = CheckMelonLoader(path);
        if (IsMelonLoaderValid)
        {
            OpenGameProfiles(path, settingsService.LoadGameProfile()?.GamePath);
            settingsService.SaveGameProfile(new GameProfile { GamePath = path });
            SearchGameProcessIfNull();
        }
        else
        {
            ClearGameProfiles();
            SetupRequired = true;
        }
    }

    private bool CanInstallMelonLoader() => GamePath != "No game selected" && !IsMelonLoaderValid && !IsInstallingMelonLoader;

    [RelayCommand(CanExecute = nameof(CanInstallMelonLoader))]
    private async Task InstallMelonLoader()
    {
        if (!TryBeginModOperation(requireProfile: false))
            return;
        IsInstallingMelonLoader = true;
        InstallProgressText = "Starting...";

        try
        {
            var progress = new System.Progress<string>(text => InstallProgressText = text);

            await melonLoaderInstaller.InstallAsync(GamePath, progress);

            IsMelonLoaderValid = CheckMelonLoader(GamePath);

            if (IsMelonLoaderValid)
            {
                OpenGameProfiles(GamePath, settingsService.LoadGameProfile()?.GamePath);
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
            IsModOperationInProgress = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsMelonLoaderValid))]
    private void CloseSetup()
    {
        if (IsModOperationInProgress)
            return;
        SetupRequired = false;
        _ = RefreshMods();
    }
}
