using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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
    public partial bool IsUpdateAvailable { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvailableUpdateText))]
    public partial UpdateInfo? AvailableUpdate { get; set; }

    public string AvailableUpdateText => AvailableUpdate is null
        ? ""
        : LocalizationService.Instance.Format("NewVersionFormat", AvailableUpdate.Version);

    [ObservableProperty]
    public partial bool IsUpdatePanelOpen { get; set; }

    [ObservableProperty]
    public partial bool IsUpdateBannerVisible { get; set; }

    [ObservableProperty]
    public partial bool IsDownloadingUpdate { get; set; }

    [ObservableProperty]
    public partial double DownloadProgress { get; set; }

    public async Task CheckForAppUpdates()
    {
        var update = await appUpdatingService.CheckAsync();
        if (update is null) return;

        AvailableUpdate = update;
        IsUpdateAvailable = true;
        IsUpdateBannerVisible = true;
    }

    [RelayCommand]
    private void OpenUpdatePanel()
    {
        if (AvailableUpdate is null) return;
        IsUpdatePanelOpen = true;
    }

    [RelayCommand]
    private void DismissUpdateBanner() => IsUpdateBannerVisible = false;

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

        if (!AppUpdatingService.IsInstalled())
        {
            if (App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: Window window })
            {
                await window.Launcher.LaunchUriAsync(new Uri(AvailableUpdate.ReleaseUrl));
            }

            IsUpdatePanelOpen = false;
            return;
        }


        IsDownloadingUpdate = true;
        DownloadProgress = 0;

        var progress = new Progress<double>(percent => DownloadProgress = percent);
        await appUpdatingService.DownloadAndInstallUpdateAsync(AvailableUpdate, progress);

        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            lifetime.Shutdown();
        }
        else
        {
            Environment.Exit(0);
        }
    }
}