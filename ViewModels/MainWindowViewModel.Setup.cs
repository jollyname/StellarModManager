using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarModManager.Models;
using StellarModManager.Services;
using System;
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

    [ObservableProperty]
    private string setupGamePath = "No game selected";

    [ObservableProperty]
    private string? setupError;

    private readonly MelonLoaderInstaller melonLoaderInstaller = new();

    partial void OnSetupGamePathChanged(string value) => InstallMelonLoaderCommand.NotifyCanExecuteChanged();

    partial void OnIsProfileBusyChanged(bool value)
    {
        CancelGameSetupCommand.NotifyCanExecuteChanged();
        CloseSetupCommand.NotifyCanExecuteChanged();
        InstallMelonLoaderCommand.NotifyCanExecuteChanged();
        if (!value && retryExternalScan)
        {
            retryExternalScan = false;
            QueueExternalScan();
        }
    }

    partial void OnIsMelonLoaderValidChanged(bool value)
    {
        CloseSetupCommand.NotifyCanExecuteChanged();
        InstallMelonLoaderCommand.NotifyCanExecuteChanged();
        ApplyAutoCheckTimerState();
    }

    partial void OnIsInstallingMelonLoaderChanged(bool value)
    {
        CancelGameSetupCommand.NotifyCanExecuteChanged();
        CloseSetupCommand.NotifyCanExecuteChanged();
        InstallMelonLoaderCommand.NotifyCanExecuteChanged();
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

    private async Task RestoreGameAsync()
    {
        await RunProfileOperation("StatusProfileSetupFailed", async () =>
        {
            try
            {
                var path = await Task.Run(() => profileStorage.GetSavedGamePath() ?? settingsService.LoadGameProfile()?.GamePath);
                if (path == null)
                    return;

                SetupGamePath = path;
                IsMelonLoaderValid = CheckMelonLoader(path);
                if (!IsMelonLoaderValid)
                    return;

                await FinishGameSetupAsync(path);
            }
            catch (Exception ex)
            {
                SetupError = LocalizationService.Instance.Format("StatusProfileSetupFailed", ex.Message);
            }
        });
    }

    [RelayCommand]
    private Task SelectSuggestedFolder(GameInstallationInfo info) => ChooseGameFolderAsync(info.Directory.FullName);

    [RelayCommand]
    private async Task SelectGameFolder()
    {
        if (IsProfileBusy || IsInstallingMelonLoader || GetMainWindow() is not { } window)
            return;

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Unity Game Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
            await ChooseGameFolderAsync(folders[0].Path.LocalPath);
    }

    private Task ChooseGameFolderAsync(string path) => RunProfileOperation("StatusProfileSetupFailed", async () =>
    {
        var alreadySetUp = !SetupRequired;
        var valid = CheckMelonLoader(path);
        if (alreadySetUp && valid)
        {
            await FinishGameSetupAsync(path);
            return;
        }

        SetupError = null;
        SetupRequired = true;
        SetupGamePath = path;
        IsMelonLoaderValid = valid;
    });

    private async Task FinishGameSetupAsync(string path)
    {
        var snapshot = await Task.Run(() => profileStorage.Initialize(path));
        if (!string.Equals(GamePath, path, ProfileFileTransaction.PathComparison))
        {
            GameProcess = null;
            GamePath = path;
        }

        SetupGamePath = path;
        SetupError = null;
        SetupRequired = false;
        IsMelonLoaderValid = true;
        ApplyProfileSnapshot(snapshot);
        SearchGameProcessIfNull();
        _ = RefreshMods();
    }

    private bool CanCancelGameSetup() => !IsProfileBusy && !IsInstallingMelonLoader;

    [RelayCommand(CanExecute = nameof(CanCancelGameSetup))]
    private void CancelGameSetup()
    {
        if (ActiveProfile == null)
            return;

        SetupError = null;
        SetupGamePath = GamePath;
        IsMelonLoaderValid = CheckMelonLoader(GamePath);
        SetupRequired = false;
    }

    private bool CanInstallMelonLoader() => SetupGamePath != "No game selected"
        && !IsMelonLoaderValid && !IsInstallingMelonLoader && !IsProfileBusy;

    [RelayCommand(CanExecute = nameof(CanInstallMelonLoader))]
    private async Task InstallMelonLoader()
    {
        IsInstallingMelonLoader = true;
        InstallProgressText = "Starting...";
        SetupError = null;

        try
        {
            var progress = new Progress<string>(text => InstallProgressText = text);
            await melonLoaderInstaller.InstallAsync(SetupGamePath, progress);
            IsMelonLoaderValid = CheckMelonLoader(SetupGamePath);
        }
        catch (Exception ex)
        {
            MelonLoaderStatusText = $"Auto-install failed: {ex.Message}";
        }
        finally
        {
            IsInstallingMelonLoader = false;
        }
    }

    private bool CanCloseSetup() => IsMelonLoaderValid && !IsProfileBusy && !IsInstallingMelonLoader;

    [RelayCommand(CanExecute = nameof(CanCloseSetup))]
    private Task CloseSetup() => RunProfileOperation("StatusProfileSetupFailed", async () =>
    {
        try
        {
            await FinishGameSetupAsync(SetupGamePath);
        }
        catch (Exception ex)
        {
            SetupError = LocalizationService.Instance.Format("StatusProfileSetupFailed", ex.Message);
        }
    });
}
