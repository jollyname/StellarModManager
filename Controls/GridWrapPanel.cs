using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace StellarModManager.Controls;

// based on WrapPanel code
public class GridWrapPanel : Panel, INavigableContainer
{
    public static Orientation Orientation => Orientation.Horizontal;


    public static readonly StyledProperty<double> ItemSpacingProperty =
        AvaloniaProperty.Register<WrapPanel, double>(nameof(ItemSpacing));

    public static readonly StyledProperty<double> LineSpacingProperty =
        AvaloniaProperty.Register<WrapPanel, double>(nameof(LineSpacing));

    public static readonly StyledProperty<double> ItemWidthProperty =
        AvaloniaProperty.Register<WrapPanel, double>(nameof(ItemWidth), double.NaN);

    public static readonly StyledProperty<double> ItemHeightProperty =
        AvaloniaProperty.Register<WrapPanel, double>(nameof(ItemHeight), double.NaN);


    static GridWrapPanel()
    {
        AffectsMeasure<WrapPanel>(ItemSpacingProperty, LineSpacingProperty, ItemWidthProperty, ItemHeightProperty);
    }


    public double ItemSpacing
    {
        get => GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    public double LineSpacing
    {
        get => GetValue(LineSpacingProperty);
        set => SetValue(LineSpacingProperty, value);
    }

    public double ItemWidth
    {
        get => GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }


    IInputElement? INavigableContainer.GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
    {
        bool horiz = Orientation == Orientation.Horizontal;
        int index = from is not null ? Children.IndexOf((Control)from) : -1;

        switch (direction)
        {
            case NavigationDirection.First:
                index = 0;
                break;
            case NavigationDirection.Last:
                index = Children.Count - 1;
                break;
            case NavigationDirection.Next:
                ++index;
                break;
            case NavigationDirection.Previous:
                --index;
                break;
            case NavigationDirection.Left:
                index = horiz ? index - 1 : -1;
                break;
            case NavigationDirection.Right:
                index = horiz ? index + 1 : -1;
                break;
            case NavigationDirection.Up:
                index = horiz ? -1 : index - 1;
                break;
            case NavigationDirection.Down:
                index = horiz ? -1 : index + 1;
                break;
        }

        if (index >= 0 && index < Children.Count)
        {
            return Children[index];
        }
        else
        {
            return null;
        }
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var panelSize = new UVSize(Orientation);
        var uvConstraint = new UVSize(Orientation, constraint.Width, constraint.Height);
        panelSize.U = uvConstraint.U;

        var childConstraint = new UVSize(Orientation,
            !double.IsNaN(ItemWidth) ? ItemWidth : constraint.Width,
            !double.IsNaN(ItemHeight) ? ItemHeight : constraint.Height); 

        var visibleItemsPerLine = Math.Max(Math.Floor(uvConstraint.U / childConstraint.U), 1);
        var itemU = (uvConstraint.U - ItemSpacing*(visibleItemsPerLine-1)) / visibleItemsPerLine;
        childConstraint.U = itemU;

        var curLineSize = new UVSize(Orientation);
        var curLineVisibleItems = visibleItemsPerLine;
        var curLinePrevWasVisible = false; 
        var curLineIsntFirst = false;
            

        for (int i = 0, count = Children.Count; i < count; ++i)
        {
            var child = Children[i];
            if (!child.IsVisible)
            {
                curLinePrevWasVisible = false;
                continue;
            }

            if (curLineVisibleItems == visibleItemsPerLine) // new line
            {
                panelSize.V += curLineSize.V + (curLineIsntFirst? LineSpacing : 0);

                curLineSize.U = 0;
                curLineSize.V = 0;
                curLineVisibleItems = 0;
                curLinePrevWasVisible = false;
                curLineIsntFirst = true;
            }
            curLineVisibleItems++;


            // Flow passes its own constraint to children
            child.Measure(new Size(childConstraint.Width, childConstraint.Height));

            var childDesiredSize = new UVSize(Orientation,
                child.DesiredSize.Width,
                child.DesiredSize.Height
            );

            if (curLinePrevWasVisible)
            {
                curLineSize.U += ItemSpacing;
            }
            curLineSize.U += itemU;
            curLineSize.V = Math.Max(curLineSize.V, Math.Min(childConstraint.V, childDesiredSize.V));

            curLinePrevWasVisible = true;
        }

        // add last line
        panelSize.V += curLineSize.V + (curLineIsntFirst? LineSpacing : 0);

        // Go from UV space to W/H space
        return new Size(panelSize.Width, panelSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var uvConstraint = new UVSize(Orientation, finalSize.Width, finalSize.Height);

        var childConstraint = new UVSize(Orientation,
            !double.IsNaN(ItemWidth) ? ItemWidth : finalSize.Width,
            !double.IsNaN(ItemHeight) ? ItemHeight : finalSize.Height); 

        var visibleItemsPerLine = Math.Max(Math.Floor(uvConstraint.U / childConstraint.U), 1);
        var itemU = (uvConstraint.U - ItemSpacing*(visibleItemsPerLine-1)) / visibleItemsPerLine;
        childConstraint.U = itemU;

        double accumulatedV = 0;

        var curLineFirstIndex = 0;
        var curLineSize = new UVSize(Orientation);
        var curLineVisibleItems = 0;
        var curLinePrevWasVisible = false; 
            

        for (int i = 0, count = Children.Count; i < count; ++i)
        {
            var child = Children[i];
            if (!child.IsVisible)
            {
                curLinePrevWasVisible = false;
                continue;
            }

            if (curLineVisibleItems == visibleItemsPerLine) // new line
            {
                ArrangeLine(curLineSize, curLineFirstIndex, i);

                accumulatedV += curLineSize.V + LineSpacing;
                curLineSize.U = 0;
                curLineSize.V = 0;
                curLineVisibleItems = 0;
                curLinePrevWasVisible = false;
                curLineFirstIndex = i;
            }
            curLineVisibleItems++;

            var childDesiredSize = new UVSize(Orientation,
                child.DesiredSize.Width,
                child.DesiredSize.Height
            );

            if (curLinePrevWasVisible)
            {
                curLineSize.U += ItemSpacing;
            }
            curLineSize.U += itemU;
            curLineSize.V = Math.Max(curLineSize.V, Math.Min(childConstraint.V, childDesiredSize.V));

            curLinePrevWasVisible = true;
        }

        // arrange last line
        ArrangeLine(curLineSize, curLineFirstIndex, Children.Count);

        return finalSize;

        void ArrangeLine(UVSize lineSize, int start, int end)
        {            
            double u = 0;

            int visibleItems = Children.GetRange(start, end - start).Count(c => c.IsVisible);

            for (int i = start; i < end; ++i)
            {
                Children[i].Arrange(Orientation is Orientation.Horizontal ? new(u, accumulatedV, itemU, lineSize.V) : new(accumulatedV, u, lineSize.V, itemU));
                u += itemU + (!Children[i].IsVisible ? 0 : ItemSpacing);
            }

            return;
        }
    }
}

file struct UVSize
{
    private readonly Orientation _orientation;

    public double U;
    public double V;
    
    
    public UVSize(Orientation orientation, double width, double height)
    {
        U = V = 0d;
        _orientation = orientation;
        Width = width;
        Height = height;
    }

    public UVSize(Orientation orientation)
    {
        U = V = 0d;
        _orientation = orientation;
    }


    public double Width
    {
        readonly get => _orientation == Orientation.Horizontal ? U : V;
        set { if (_orientation == Orientation.Horizontal) U = value; else V = value; }
    }
    
    public double Height
    {
        readonly get => _orientation == Orientation.Horizontal ? V : U;
        set { if (_orientation == Orientation.Horizontal) V = value; else U = value; }
    }
}

file static class MathUtilities
{
    // Avalonia.Utilities.MathUtilities
    internal const double DoubleEpsilon = 2.2204460492503131e-016;

    // Avalonia.Utilities.MathUtilities
    public static bool AreClose(double value1, double value2)
    {
        //in case they are Infinities (then epsilon check does not work)
        if (value1 == value2) return true;
        double eps = (Math.Abs(value1) + Math.Abs(value2) + 10.0) * DoubleEpsilon;
        double delta = value1 - value2;
        return (-eps < delta) && (eps > delta);
    }

    // Avalonia.Utilities.MathUtilities
    public static bool GreaterThan(double value1, double value2)
    {
        return (value1 > value2) && !AreClose(value1, value2);
    }
}