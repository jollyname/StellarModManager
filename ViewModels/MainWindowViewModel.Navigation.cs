using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StellarModManager.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private int selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == 0) // Online Mods tab
        {
            RefreshModsCommand.Execute(null);
        }
    }
}