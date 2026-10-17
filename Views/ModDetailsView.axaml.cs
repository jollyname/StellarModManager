using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using StellarModManager.Models;
using StellarModManager.ViewModels;

namespace StellarModManager.Views;

public partial class ModDetailsView : UserControl
{
    private MainWindowViewModel? viewModel;

    public ModDetailsView()
    {
        InitializeComponent();
    }

    // Start each newly selected mod at the top of the pane
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (viewModel != null)
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        viewModel = DataContext as MainWindowViewModel;

        if (viewModel != null)
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.DetailsMod))
            DetailsScroll.ScrollToHome();
    }

    private void OpenVideoClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ModVideo video })
            viewModel?.OpenVideoCommand.Execute(video);
    }
}
