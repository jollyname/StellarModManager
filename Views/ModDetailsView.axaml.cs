using Avalonia;
using Avalonia.Controls;

namespace StellarModManager.Views;

public partial class ModDetailsView : UserControl
{
    public ModDetailsView()
    {
        InitializeComponent();
    }

    // fix auto scroll down on expanded view
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty && IsVisible)
            DetailsScroll.ScrollToHome();
    }
}