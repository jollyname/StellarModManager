using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using StellarModManager.Models;
using Avalonia.Interactivity;
using StellarModManager.ViewModels;

namespace StellarModManager.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as MainWindowViewModel)?.StopExternalDiscovery();
    }

    // Lets the styles swap the maximize icon for the restore icon
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
            Classes.Set("maximized", WindowState == WindowState.Maximized);
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnNewProfileKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainWindowViewModel vm)
            vm.CreateProfileCommand.Execute(null);
    }

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is ModProfile profile && DataContext is MainWindowViewModel vm)
        {
            if (e.Key == Key.Enter) vm.CommitRenameCommand.Execute(profile);
            if (e.Key == Key.Escape) vm.CancelRenameCommand.Execute(profile);
        }
    }
}