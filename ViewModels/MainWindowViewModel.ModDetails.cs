using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarModManager.Models;

namespace StellarModManager.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private OnlineModInfo? selectedMod;

    [ObservableProperty]
    private bool isModDetailsOpen;

    [RelayCommand]
    private void OpenModDetails(OnlineModInfo mod)
    {
        SelectedMod = mod;
        IsModDetailsOpen = true;
        _ = repositoryService.LoadGalleryAsync(mod);
    }
    
    [RelayCommand]
    private void CloseModDetails()
    {
        IsModDetailsOpen = false;
    }
}