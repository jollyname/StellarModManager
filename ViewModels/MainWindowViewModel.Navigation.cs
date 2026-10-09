using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StellarModManager.ViewModels;

public enum AppPage
{
    Browse,
    Library,
    Updates,
    Settings
}

public partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowsePage), nameof(IsLibraryPage), nameof(IsUpdatesPage), nameof(IsSettingsPage))]
    private AppPage currentPage;

    [ObservableProperty]
    private bool isPaneOpen = true;

    public bool IsBrowsePage => CurrentPage == AppPage.Browse;
    public bool IsLibraryPage => CurrentPage == AppPage.Library;
    public bool IsUpdatesPage => CurrentPage == AppPage.Updates;
    public bool IsSettingsPage => CurrentPage == AppPage.Settings;

    [RelayCommand]
    private void Navigate(AppPage page) => CurrentPage = page;

    [RelayCommand]
    private void TogglePane() => IsPaneOpen = !IsPaneOpen;

    partial void OnCurrentPageChanged(AppPage value)
    {
        ShowDetails();

        if (value == AppPage.Browse)
        {
            RefreshModsCommand.Execute(null);
        }
    }
}
