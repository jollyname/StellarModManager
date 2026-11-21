using Avalonia.Data.Converters;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarModManager.Models;
using StellarModManager.Services;
using System.Collections.ObjectModel;
using System.Reflection;

namespace StellarModManager.ViewModels;

public partial class MainWindowViewModel
{
    public static readonly IValueConverter LanguageNameConverter =
        new FuncValueConverter<string, string>(code =>
            code is null ? "" : LocalizationService.Instance.GetLanguageDisplayName(code));

    private readonly ThemeService _themeService = new();

    public string AppVersion { get; } = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

    public ObservableCollection<string> AvailableThemes { get; } = new();
    public ObservableCollection<string> AvailableLanguages { get; } = new();

    [ObservableProperty]
    public partial string SelectedTheme { get; set; } = "Purple";

    [ObservableProperty]
    public partial string SelectedLanguage { get; set; } = "en";

    [ObservableProperty]
    public partial bool ConfirmBeforeRemove { get; set; } = true;

    [ObservableProperty]
    public partial bool AutoDeployDependencies { get; set; }

    [ObservableProperty]
    public partial bool AutoCheckForAppUpdates { get; set; } = true;

    [ObservableProperty]
    public partial bool AutoCheckForModUpdates { get; set; } = true;

    // Mod lists show a three-column card grid instead of the table
    [ObservableProperty]
    public partial bool IsCompactView { get; set; }

    private readonly DispatcherTimer _updateCheckTimer;

    // Settings are saved as soon as they change
    private void PersistSettings()
    {
        _settingsService.SaveAppSettings(new AppSettings
        {
            Language = SelectedLanguage,
            Theme = SelectedTheme,
            ConfirmBeforeRemove = ConfirmBeforeRemove,
            AutoDeployDependencies = AutoDeployDependencies,
            AutoCheckForModUpdates = AutoCheckForModUpdates,
            AutoCheckForAppUpdates = AutoCheckForAppUpdates,
            ModSort = SortIndex,
            CompactModList = IsCompactView
        });
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        LocalizationService.Instance.SetLanguage(value);
        PersistSettings();
    }

    partial void OnSelectedThemeChanged(string value)
    {
        _themeService.ApplyTheme(_themeService.LoadTheme(value));
        PersistSettings();
    }

    partial void OnAutoCheckForAppUpdatesChanged(bool value) => PersistSettings();

    partial void OnConfirmBeforeRemoveChanged(bool value) => PersistSettings();

    partial void OnAutoDeployDependenciesChanged(bool value) => PersistSettings();

    partial void OnIsCompactViewChanged(bool value) => PersistSettings();

    [RelayCommand]
    private void ShowListView() => IsCompactView = false;

    [RelayCommand]
    private void ShowCompactView() => IsCompactView = true;

    partial void OnAutoCheckForModUpdatesChanged(bool value)
    {
        PersistSettings();
        ApplyAutoCheckTimerState();
    }

    private void ApplyAutoCheckTimerState()
    {
        if (AutoCheckForModUpdates && IsMelonLoaderValid)
            _updateCheckTimer.Start();
        else
            _updateCheckTimer.Stop();
    }

    private void LoadAvailableThemes()
    {
        AvailableThemes.Clear();

        foreach (var name in _themeService.GetAvailableThemeNames())
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