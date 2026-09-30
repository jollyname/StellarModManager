using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Templates;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StellarModManager.Models;
using StellarModManager.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace StellarModManager.Views;

/// <summary>
/// The installed-mod browser. Columns are derived from the viewport so the grid
/// stays at three-up with a 220px minimum card, folding to two then one as the
/// window narrows. Only the visible slice of rows is ever materialized, so the
/// grid holds up at 500 mods without scroll jank.
/// </summary>
public partial class MainLibraryView : UserControl
{
    private const double MinCardWidth = 320;
    private const double MaxCardWidth = 480;
    private const double GridGap = 24;
    private const int BufferRows = 2;

    /// <summary>Cards are 180px-tall banners, so three across makes them postage stamps. Two reads better.</summary>
    // The card is a fixed size, so the column count is whatever fits. This is a sanity ceiling
    // for absurdly wide monitors, not a layout target: capping it at 2 left most of the window
    // empty instead of filling it.
    private const int MaxColumns = 8;

    /// <summary>Header 62 + hero 180 + description 48 + footer 50.</summary>
    private static double DefaultCardHeight => 340;

    private readonly Dictionary<InstalledModInfo, ContentControl> pool = new();

    private ScrollViewer? gridScroll;
    private ScrollViewer? browseScroll;
    private Control? libraryBody;
    private Panel? gridRoot;
    private WrapPanel? cardsPanel;
    private WrapPanel? browsePanel;
    private DataTemplate? cardTemplate;
    private MainWindowViewModel? vm;

    private int columns = 3;
    private double cardWidth = MinCardWidth;
    private double cardHeight = 340;
    private int lastStart = -1;
    private int lastEnd = -1;

    public MainLibraryView()
    {
        InitializeComponent();

        gridScroll = this.FindControl<ScrollViewer>("GridScroll");
        browseScroll = this.FindControl<ScrollViewer>("BrowseScroll");
        libraryBody = this.FindControl<Control>("LibraryBody");
        gridRoot = this.FindControl<Panel>("GridRoot");
        cardsPanel = this.FindControl<WrapPanel>("CardsPanel");

        if (Resources.TryGetValue("ModCardTemplate", out object? template))
            cardTemplate = template as DataTemplate;

        // Measure off LibraryBody, not off either ScrollViewer. Only the visible ScrollViewer
        // has a real viewport, and the repository one measures its content at infinite width
        // (it reported 3074px inside a 2752px window), so its Viewport is meaningless.
        if (libraryBody is not null)
            libraryBody.SizeChanged += OnViewportChanged;

        if (browseScroll is not null)
            browseScroll.PointerPressed += OnBrowseCardPointerPressed;

        if (gridScroll is not null)
            gridScroll.ScrollChanged += OnScrollChanged;

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (vm is not null)
        {
            vm.PropertyChanged -= OnVmPropertyChanged;
            vm.FilteredMods.CollectionChanged -= OnFilteredChanged;
        }

        vm = DataContext as MainWindowViewModel;

        if (vm is not null)
        {
            vm.PropertyChanged += OnVmPropertyChanged;
            vm.FilteredMods.CollectionChanged += OnFilteredChanged;
        }

        lastStart = -1;
        Relayout();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.IsLibraryMode))
        {
            lastStart = -1;
            browsePanel = null;

            // The library ScrollViewer keeps whatever offset it had when the tab was hidden.
            // Sync() derives the pooled window from that offset, so without resetting it the
            // grid comes back part-way down instead of at the top-left corner.
            if (vm is { IsLibraryMode: true } && gridScroll is not null)
                gridScroll.Offset = default;

            Relayout();
            return;
        }

        if (e.PropertyName is nameof(MainWindowViewModel.HasLibraryMods)
            or nameof(MainWindowViewModel.HasInstalledMods))
        {
            lastStart = -1;
            browsePanel = null;
            Relayout();
        }
    }

    private void OnFilteredChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (InstalledModInfo mod in e.OldItems)
                pool.Remove(mod);
        }

        lastStart = -1;
        Relayout();
    }

    private void OnViewportChanged(object? sender, SizeChangedEventArgs e) => Relayout();

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e) => Sync();

    private const double ScrollbarAllowance = 16;

    private void Relayout()
    {
        if (gridScroll is null || gridRoot is null || cardsPanel is null || vm is null)
            return;

        // LibraryBody hosts both ScrollViewers, so it is laid out no matter which tab is
        // showing and always reports the same width for both grids. The small allowance keeps
        // the vertical scrollbar from overlapping the last card's gutter.
        double viewport = (libraryBody?.Bounds.Width ?? 0) - ScrollbarAllowance;

        if (viewport <= 0)
        {
            // Not measured yet. Returning here is deliberate: re-posting would spin forever
            // whenever this view is never given a size at all (e.g. it stays IsVisible=false
            // because setup is still required). OnViewportChanged already fires Relayout the
            // moment a real width arrives, so there is nothing to retry.
            return;
        }

        int cols = Math.Clamp(
            (int)Math.Floor((viewport + GridGap) / (MaxCardWidth + GridGap)),
            1,
            MaxColumns);


        // the card never scales with the window: it clamps between Min and Max and the row is
        // left-aligned at a fixed inset, so widening the window grows the shell around a
        // fixed-size card instead of stretching the card itself. Both grids share this
        // alignment, otherwise switching tabs makes the whole row jump sideways.
        double avail = (viewport - GridGap * cols) / cols;
        double width = Math.Clamp(avail, Math.Min(MinCardWidth, viewport - GridGap), MaxCardWidth);

        // Each card carries a 12px margin on all sides, so one column band is
        // cardWidth + 24. A WrapPanel lays bands out edge to edge and keeps the trailing
        // margin, so the panel needs the full cols * band. Sizing it to the *visual* row
        // (which drops the last gutter) leaves it 24px too narrow, and the panel then only
        // fits a single band per line and stacks every card vertically.
        double bandWidth = width + GridGap;
        double panelWidth = cols * bandWidth;

        if (cols != columns || Math.Abs(width - cardWidth) > 0.5)
        {
            columns = cols;
            cardWidth = width;
            cardHeight = DefaultCardHeight;

            foreach (ContentControl host in pool.Values)
            {
                host.Width = cardWidth + GridGap;
                host.Height = cardHeight + GridGap;
            }

            // both card templates bind their surface to these, so the library grid and
            // the repository grid can only ever be the same size
            vm.CardWidth = cardWidth;
            vm.CardHeight = cardHeight;

            lastStart = -1;
        }

        gridRoot.Width = viewport;
        gridRoot.HorizontalAlignment = HorizontalAlignment.Left;
        gridRoot.VerticalAlignment = VerticalAlignment.Top;
        cardsPanel.Width = panelWidth;
        cardsPanel.HorizontalAlignment = HorizontalAlignment.Left;
        cardsPanel.VerticalAlignment = VerticalAlignment.Top;
        cardsPanel.Margin = new Thickness(0, 0, 0, 0);

        // the repository grid lives inside an ItemsPanel, so reach it once it is realized
        browsePanel ??= this.GetVisualDescendants()
            .OfType<WrapPanel>()
            .FirstOrDefault(p => p.Name == "BrowsePanel");

        if (browsePanel is not null)
        {
            // same band and same panel width as the library grid, so the two tabs can only
            // ever show the same number of equally sized cards
            browsePanel.ItemWidth = bandWidth;
            browsePanel.ItemHeight = cardHeight + GridGap;
            browsePanel.Width = panelWidth;
            browsePanel.HorizontalAlignment = HorizontalAlignment.Left;
            browsePanel.Margin = new Thickness(0, 0, 0, 0);
        }

        Sync(force: true);

    }

    private void Sync(bool force = false)
    {
        if (gridScroll is null || gridRoot is null || cardsPanel is null
            || vm is null || cardTemplate is null)
        {
            return;
        }

        IReadOnlyList<InstalledModInfo> items = vm.FilteredMods;
        int total = items.Count;
        double rowHeight = cardHeight + GridGap;

        if (total == 0)
        {
            gridRoot.Height = 0;
            lastStart = -1;
            return;
        }

        int rows = (int)Math.Ceiling(total / (double)columns);
        gridRoot.Height = rows * rowHeight - GridGap;

        double viewportHeight = gridScroll.Viewport.Height;

        if (viewportHeight <= 0)
            viewportHeight = gridScroll.Bounds.Height;

        int visibleRows = (int)Math.Ceiling((viewportHeight + GridGap) / rowHeight) + BufferRows;
        int start = Math.Max(0, (int)Math.Floor(Math.Max(0, gridScroll.Offset.Y) / rowHeight) - 1);
        int end = Math.Min(rows, start + visibleRows + 1);

        if (!force && start == lastStart && end == lastEnd)
            return;

        lastStart = start;
        lastEnd = end;

        int first = start * columns;
        int last = Math.Min(total, end * columns);

        var ordered = new List<Control>(last - first);

        for (int i = first; i < last; i++)
        {
            InstalledModInfo mod = items[i];

            if (!pool.TryGetValue(mod, out ContentControl? host))
            {
                host = new ContentControl
                {
                    ContentTemplate = cardTemplate,
                    Content = mod
                };

                host.KeyDown += OnCardKeyDown;
                host.GotFocus += OnCardGotFocus;
                host.PointerPressed += OnInstalledCardPointerPressed;

                pool[mod] = host;
            }

            host.Width = cardWidth + GridGap;
            host.Height = cardHeight + GridGap;

            ordered.Add(host);
        }

        // replace in one pass so WrapPanel always lays cards out in library order
        cardsPanel.Children.Clear();

        foreach (Control child in ordered)
            cardsPanel.Children.Add(child);

        cardsPanel.Margin = new Thickness(0, start * rowHeight, 0, 0);

        foreach (Control child in ordered)
        {
            if (child is ContentControl { Content: InstalledModInfo mod })
                vm.EnsureCardIcon(mod);
        }
    }

    /// <summary>
    /// True when the press landed on a control that already handles the click itself
    /// (Install, Update, the context menu, and so on), so opening the detail view must
    /// not also fire.
    /// </summary>
    private static bool PressedInteractiveControl(PointerPressedEventArgs e, Control cardRoot)
    {
        for (Visual? v = e.Source as Visual; v is not null && v != cardRoot; v = v.GetVisualParent())
        {
            if (v is Button or ToggleButton or MenuItem)
                return true;
        }

        return false;
    }

    private void OnInstalledCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (vm is null || sender is not ContentControl host)
            return;

        if (e.GetCurrentPoint(host).Properties.IsLeftButtonPressed is not true)
            return;

        if (PressedInteractiveControl(e, host) || host.Content is not InstalledModInfo mod)
            return;

        vm.SelectInstalledModCommand.Execute(mod);
    }

    /// <summary>
    /// One handler for every browse card: the ItemsControl realizes its presenters
    /// internally, so instead of wiring each item we walk back up from the press to find
    /// the presenter that produced it and read the mod off its DataContext.
    /// </summary>
    private void OnBrowseCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (vm is null || sender is not Control root)
            return;

        if (e.GetCurrentPoint(root).Properties.IsLeftButtonPressed is not true)
            return;

        for (Visual? v = e.Source as Visual; v is not null && v != root; v = v.GetVisualParent())
        {
            if (PressedInteractiveControl(e, root))
                return;

            if (v is ContentPresenter presenter && presenter.DataContext is OnlineModInfo mod)
            {
                vm.SelectOnlineModCommand.Execute(mod);
                return;
            }
        }
    }

    private void OnCardKeyDown(object? sender, KeyEventArgs e)
    {
        if (vm is null || sender is not ContentControl host || host.Content is not InstalledModInfo mod)
            return;

        int index = vm.FilteredMods.IndexOf(mod);

        if (index < 0)
            return;

        int column = index % columns;
        int row = index / columns;
        int target = -1;

        switch (e.Key)
        {
            case Key.Left when column > 0:
                target = index - 1;
                break;
            case Key.Right when column < columns - 1 && index + 1 < vm.FilteredMods.Count:
                target = index + 1;
                break;
            case Key.Up when row > 0:
                target = index - columns;
                break;
            case Key.Down when index + columns < vm.FilteredMods.Count:
                target = index + columns;
                break;
            case Key.Enter:
                vm.SelectInstalledModCommand.Execute(mod);
                e.Handled = true;
                return;
            case Key.Space:
                _ = vm.ToggleModCommand.ExecuteAsync(mod);
                e.Handled = true;
                return;
        }

        if (target >= 0)
        {
            FocusCard(vm.FilteredMods[target]);
            e.Handled = true;
        }
    }

    private void OnCardGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (gridScroll is null || vm is null
            || sender is not ContentControl host || host.Content is not InstalledModInfo mod)
        {
            return;
        }

        int index = vm.FilteredMods.IndexOf(mod);

        if (index < 0)
            return;

        double rowHeight = cardHeight + GridGap;
        double top = (index / columns) * rowHeight;
        double bottom = top + cardHeight;
        double viewTop = Math.Max(0, gridScroll.Offset.Y);
        double viewHeight = gridScroll.Viewport.Height;

        if (viewHeight <= 0)
            viewHeight = gridScroll.Bounds.Height;

        if (top < viewTop)
            gridScroll.Offset = new Vector(0, top);
        else if (bottom > viewTop + viewHeight)
            gridScroll.Offset = new Vector(0, bottom - viewHeight);
    }

    private void FocusCard(InstalledModInfo mod)
    {
        if (pool.TryGetValue(mod, out ContentControl? host))
            host.Focus();
    }

    private void OnCardMenuClick(object? sender, RoutedEventArgs e)
    {
        if (vm is null || sender is not MenuItem item || item.DataContext is not InstalledModInfo mod)
            return;

        switch (item.Tag as string)
        {
            case "toggle":
                _ = vm.ToggleModCommand.ExecuteAsync(mod);
                break;
            case "update":
                _ = vm.UpdateModCommand.ExecuteAsync(mod);
                break;
            case "folder":
                vm.OpenModFolderCommand.Execute(mod);
                break;
            case "remove":
                vm.RequestRemoveModCommand.Execute(mod);
                break;
        }
    }
}
