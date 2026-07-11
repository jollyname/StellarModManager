using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarModManager.Models;
using StellarModManager.Services;
using System;
using System.Threading.Tasks;

namespace StellarModManager.ViewModels;

public partial class MainWindowViewModel
{
    private readonly AppUpdatingService appUpdatingService = new();

    [ObservableProperty]
    private bool isUpdateAvailable;

    [ObservableProperty]
    private UpdateInfo? availableUpdate;

    [ObservableProperty]
    private bool isUpdatePanelOpen;

    [ObservableProperty]
    private bool isDownloadingUpdate;

    [ObservableProperty]
    private double downloadProgress;

    public async Task CheckForAppUpdates()
    {
        var update = await appUpdatingService.CheckAsync();
        if (update is null) return;

        AvailableUpdate = update;
        IsUpdateAvailable = true;
        IsUpdatePanelOpen = true;
    }

    [RelayCommand]
    private void OpenUpdatePanel()
    {
        if (AvailableUpdate is null) return;
        IsUpdatePanelOpen = true;
    }

    [RelayCommand]
    private void CloseUpdatePanel()
    {
        if (IsDownloadingUpdate) return;
        IsUpdatePanelOpen = false;
    }

    [RelayCommand]
    private async Task DownloadUpdate()
    {
        if (AvailableUpdate is null) return;
        if (IsDownloadingUpdate) return;

        IsDownloadingUpdate = true;
        DownloadProgress = 0;

        var progress = new Progress<double>(percent => DownloadProgress = percent);
        await appUpdatingService.DownloadAndInstallUpdateAsync(AvailableUpdate, progress);

        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lifetime)
        {
            lifetime.Shutdown();
        }
        else
        {
            Environment.Exit(0);
        }
    }
}