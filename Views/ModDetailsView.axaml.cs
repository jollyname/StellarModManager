using System;
using System.ComponentModel;
using Avalonia.Controls;
using StellarModManager.ViewModels;

namespace StellarModManager.Views;

public partial class ModDetailsView : UserControl
{
    private MainWindowViewModel? _viewModel;

    public ModDetailsView()
    {
        InitializeComponent();
    }

    // Start each newly selected mod at the top of the pane
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        _viewModel?.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as MainWindowViewModel;
        _viewModel?.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.DetailsMod))
            DetailsScroll.ScrollToHome();
    }
}
