using Avalonia.Data.Converters;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarModManager.Models;
using StellarModManager.Services;
using System.Collections.ObjectModel;

namespace StellarModManager.ViewModels;

public partial class MainWindowViewModel
{
    public static readonly IValueConverter LanguageNameConverter =
    new FuncValueConverter<string, string>(code =>
        code is null ? "" : LocalizationService.Instance.GetLanguageDisplayName(code));

    private readonly ThemeService themeService = new();

    public ObservableCollection<string> AvailableThemes { get; } = new();
    public ObservableCollection<string> AvailableLanguages { get; } = new();

    [ObservableProperty]
    private bool hasUnsavedSettings;

    private bool suppressChangeTracking;

    [ObservableProperty]
    private string selectedTheme = "purple";

    [ObservableProperty]
    private bool isSettingsOpen;

    [ObservableProperty]
    private string selectedLanguage = "en";

    [ObservableProperty]
    private bool confirmBeforeRemove = true;

    [ObservableProperty]
    private bool autoCheckForAppUpdates = true;  

    [ObservableProperty]
    private bool autoCheckForModUpdates = true;

    private readonly DispatcherTimer updateCheckTimer;

    [RelayCommand]
    private void OpenSettings()
    {
        suppressChangeTracking = true;

        var settings = settingsService.LoadAppSettings();

        SelectedLanguage = settings.Language;
        SelectedTheme = settings.Theme;
        ConfirmBeforeRemove = settings.ConfirmBeforeRemove;
        AutoCheckForModUpdates = settings.AutoCheckForModUpdates;
        AutoCheckForAppUpdates = settings.AutoCheckForAppUpdates;

        suppressChangeTracking = false;
        HasUnsavedSettings = false;

        IsSettingsOpen = true;
    }

    [RelayCommand]
    private void SaveSettings()
    {
        PersistSettings();
        HasUnsavedSettings = false;
    }

    [RelayCommand]
    private void CloseSettings()
    {
        PersistSettings();
        HasUnsavedSettings = false;

        IsSettingsOpen = false;
    }

    private void PersistSettings()
    {
        settingsService.SaveAppSettings(new AppSettings
        {
            Language = SelectedLanguage,
            Theme = SelectedTheme,
            ConfirmBeforeRemove = ConfirmBeforeRemove,
            AutoCheckForModUpdates = AutoCheckForModUpdates,
            AutoCheckForAppUpdates = AutoCheckForAppUpdates
        });
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        LocalizationService.Instance.SetLanguage(value);
        MarkSettingsChanged();
    }

    partial void OnSelectedThemeChanged(string value)
    {
        themeService.ApplyTheme(themeService.LoadTheme(value));
        MarkSettingsChanged();
    }

    partial void OnAutoCheckForAppUpdatesChanged(bool value)
    {
        HasUnsavedSettings = true;
    }

    partial void OnConfirmBeforeRemoveChanged(bool value)
    {
        MarkSettingsChanged();
    }

    partial void OnAutoCheckForModUpdatesChanged(bool value)
    {
        MarkSettingsChanged();
        ApplyAutoCheckTimerState();
    }

    private void ApplyAutoCheckTimerState()
    {
        if (AutoCheckForModUpdates && IsMelonLoaderValid)
            updateCheckTimer.Start();
        else
            updateCheckTimer.Stop();
    }

    private void MarkSettingsChanged()
    {
        if (!suppressChangeTracking)
            HasUnsavedSettings = true;
    }

    private void LoadAvailableThemes()
    {
        AvailableThemes.Clear();

        foreach (var name in themeService.GetAvailableThemeNames())
        {
            AvailableThemes.Add(name);
        }
    }

    private void LoadAvailableLanguages()
    {
        AvailableLanguages.Clear();

        foreach (var language in LocalizationService.Instance.GetAvailableLanguages())
        {
            AvailableLanguages.Add(language);
        }
    }
}