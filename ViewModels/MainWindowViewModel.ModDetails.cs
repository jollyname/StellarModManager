using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarModManager.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace StellarModManager.ViewModels;

// Each page keeps its own list selection; the details pane shows the one of the current page.
// SelectedMod is the mod's repository entry, SelectedInstalledMod its library copy; either may be null.
public partial class MainWindowViewModel
{
    [ObservableProperty]
    private OnlineModInfo? browseSelection;

    [ObservableProperty]
    private InstalledModInfo? librarySelection;

    [ObservableProperty]
    private InstalledModInfo? updatesSelection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsMod), nameof(HasSelection), nameof(CanInstallSelected))]
    private OnlineModInfo? selectedMod;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsMod), nameof(HasSelection), nameof(CanInstallSelected), nameof(IsSelectedInstalled))]
    private InstalledModInfo? selectedInstalledMod;

    public ModInfo? DetailsMod => (ModInfo?)SelectedInstalledMod ?? SelectedMod;

    public bool HasSelection => DetailsMod != null;

    public bool CanInstallSelected => SelectedMod != null && SelectedInstalledMod == null;

    public bool IsSelectedInstalled => SelectedInstalledMod != null;

    partial void OnBrowseSelectionChanged(OnlineModInfo? value) => ShowDetails();

    partial void OnLibrarySelectionChanged(InstalledModInfo? value) => ShowDetails();

    partial void OnUpdatesSelectionChanged(InstalledModInfo? value) => ShowDetails();

    partial void OnSelectedModChanged(OnlineModInfo? value)
    {
        if (value == null)
            return;

        _ = repositoryService.LoadGalleryAsync(value);
        _ = repositoryService.LoadChangelogAsync(value);
    }

    private void ShowDetails()
    {
        switch (CurrentPage)
        {
            case AppPage.Browse:
                SelectedMod = BrowseSelection;
                SelectedInstalledMod = BrowseSelection == null ? null : InstalledMods.FirstOrDefault(m => m.Id == BrowseSelection.Id);
                break;
            case AppPage.Library:
                SelectedInstalledMod = LibrarySelection;
                SelectedMod = LibrarySelection?.Online;
                break;
            case AppPage.Updates:
                SelectedInstalledMod = UpdatesSelection;
                SelectedMod = UpdatesSelection?.Online;
                break;
        }
    }

    [RelayCommand]
    private async Task OpenRepository()
    {
        if (SelectedMod?.RepoUrl is not string url)
            return;

        if (App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: Window window })
        {
            await window.Launcher.LaunchUriAsync(new Uri(url));
        }
    }
}
