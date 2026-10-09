using Avalonia.Data.Converters;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
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
    private string selectedTheme = "Midnight Slate";

    [ObservableProperty]
    private string selectedLanguage = "en";

    [ObservableProperty]
    private bool confirmBeforeRemove = true;

    [ObservableProperty]
    private bool autoCheckForAppUpdates = true;  

    [ObservableProperty]
    private bool autoCheckForModUpdates = true;

    private readonly DispatcherTimer updateCheckTimer;

    // Settings are saved as soon as they change
    private void PersistSettings()
    {
        settingsService.SaveAppSettings(new AppSettings
        {
            Language = SelectedLanguage,
            Theme = SelectedTheme,
            ConfirmBeforeRemove = ConfirmBeforeRemove,
            AutoCheckForModUpdates = AutoCheckForModUpdates,
            AutoCheckForAppUpdates = AutoCheckForAppUpdates,
            ModSort = SortIndex
        });
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        LocalizationService.Instance.SetLanguage(value);
        PersistSettings();
    }

    partial void OnSelectedThemeChanged(string value)
    {
        themeService.ApplyTheme(themeService.LoadTheme(value));
        PersistSettings();
    }

    partial void OnAutoCheckForAppUpdatesChanged(bool value) => PersistSettings();

    partial void OnConfirmBeforeRemoveChanged(bool value) => PersistSettings();

    partial void OnAutoCheckForModUpdatesChanged(bool value)
    {
        PersistSettings();
        ApplyAutoCheckTimerState();
    }

    private void ApplyAutoCheckTimerState()
    {
        if (AutoCheckForModUpdates && IsMelonLoaderValid)
            updateCheckTimer.Start();
        else
            updateCheckTimer.Stop();
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