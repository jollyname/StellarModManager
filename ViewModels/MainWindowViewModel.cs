using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using StellarModManager.Services;
using System;

namespace StellarModManager.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string gamePath = "No game selected";

    [ObservableProperty]
    private string melonLoaderStatusText = "";

    private readonly SettingsService settingsService = new();

    public MainWindowViewModel()
    {
        LoadAvailableThemes();
        LoadAvailableLanguages();

        var appSettings = settingsService.LoadAppSettings();
        selectedTheme = appSettings.Theme;
        selectedLanguage = appSettings.Language;
        confirmBeforeRemove = appSettings.ConfirmBeforeRemove;
        autoCheckForModUpdates = appSettings.AutoCheckForModUpdates;
        autoCheckForAppUpdates = appSettings.AutoCheckForAppUpdates;

        themeService.ApplyTheme(themeService.LoadTheme(selectedTheme));
        LocalizationService.Instance.SetLanguage(selectedLanguage);

        updateCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        updateCheckTimer.Tick += async (_, _) => await RefreshMods();

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

        ApplyAutoCheckTimerState();

        if (appSettings.AutoCheckForAppUpdates)
        {
            _ = CheckForAppUpdates();
        }
    }
}