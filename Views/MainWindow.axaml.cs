using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using System;

namespace StellarModManager.Views;

public partial class MainWindow : Window
{
    private Border? _titleBarDrag;
    private Control? _maximizeGlyph;
    private Control? _restoreGlyph;
    private bool _isDragging;

    public MainWindow()
    {
        InitializeComponent();

        _titleBarDrag = this.FindControl<Border>("TitleBarDrag");
        _maximizeGlyph = this.FindControl<Control>("MaximizeGlyph");
        _restoreGlyph = this.FindControl<Control>("RestoreGlyph");

        if (_titleBarDrag is not null)
        {
            _titleBarDrag.PointerPressed += OnTitleBarPointerPressed;
        }

        PropertyChanged += OnWindowPropertyChanged;

        UpdateMaximizeGlyph();
    }

    /// <summary>
    /// Opens the window at 80% of the display it lands on, centered. Without this the
    /// window falls back to the platform default, which on a 4K panel is a postage stamp
    /// and on a laptop clips the two-column card grid.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (WindowState == WindowState.Maximized)
            return;

        Screen? screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;

        if (screen is null)
            return;

        PixelRect bounds = screen.WorkingArea;

        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        // work in DIPs so the ratio holds regardless of per-monitor scaling
        double scale = screen.Scaling;
        double width = Math.Min((bounds.Width / scale) * 0.8, bounds.Width / scale);
        double height = Math.Min((bounds.Height / scale) * 0.8, bounds.Height / scale);

        Width = Math.Max(width, MinWidth);
        Height = Math.Max(height, MinHeight);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // A second press while dragging toggles maximize, matching standard Windows behaviour.
        if (_isDragging)
        {
            ToggleMaximize();
            return;
        }

        _isDragging = true;

        try
        {
            BeginMoveDrag(e);
        }
        catch (InvalidOperationException)
        {
            // BeginMoveDrag throws if the press did not originate on the window chrome.
        }

        _isDragging = false;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty)
        {
            UpdateMaximizeGlyph();
        }
    }

    private void UpdateMaximizeGlyph()
    {
        bool maximized = WindowState == WindowState.Maximized;

        if (_maximizeGlyph is not null)
        {
            _maximizeGlyph.IsVisible = !maximized;
        }

        if (_restoreGlyph is not null)
        {
            _restoreGlyph.IsVisible = maximized;
        }
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
