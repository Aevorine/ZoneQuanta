using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace ZoneQuanta.UI.Controls;

// Equal slots across the whole width: a fixed column count, or as many columns as MinItemWidth allows,
// with the leftover width shared between them instead of piling up at the right edge as in a WrapPanel.
public sealed class UniformWrapPanel : System.Windows.Controls.Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(UniformWrapPanel),
        new FrameworkPropertyMetadata(120.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(UniformWrapPanel),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows), typeof(int), typeof(UniformWrapPanel),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty StretchItemsProperty = DependencyProperty.Register(
        nameof(StretchItems), typeof(bool), typeof(UniformWrapPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsArrange));

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    // Fixed column count; 0 fits as many as MinItemWidth allows.
    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    // Fixed row count: the height is split into this many equal rows, items centred in theirs.
    // 0 makes each row as tall as its tallest item.
    public int Rows
    {
        get => (int)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    // Items fill their slot (list rows) rather than keeping their own width centred in it (tiles).
    public bool StretchItems
    {
        get => (bool)GetValue(StretchItemsProperty);
        set => SetValue(StretchItemsProperty, value);
    }

    protected override Size MeasureOverride(Size available)
    {
        var kids = Visible();
        var (cols, slot) = Grid(available.Width, kids.Count);
        double height = 0, row = 0;
        for (int i = 0; i < kids.Count; i++)
        {
            kids[i].Measure(new Size(slot, double.PositiveInfinity));
            row = Math.Max(row, kids[i].DesiredSize.Height);
            if (i % cols == cols - 1 || i == kids.Count - 1)
            {
                height += row;
                row = 0;
            }
        }
        double width = double.IsInfinity(available.Width) ? Math.Min(cols, Math.Max(1, kids.Count)) * slot : available.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var kids = Visible();
        var (cols, slot) = Grid(final.Width, kids.Count);
        bool stretch = StretchItems;
        int fixedRows = Rows;
        double y = 0;
        for (int start = 0; start < kids.Count; start += cols)
        {
            int end = Math.Min(start + cols, kids.Count);
            double row = 0;
            for (int i = start; i < end; i++) row = Math.Max(row, kids[i].DesiredSize.Height);
            if (fixedRows > 0) row = Math.Max(row, final.Height / fixedRows);
            for (int i = start; i < end; i++)
            {
                double x = (i - start) * slot;
                double w = stretch ? slot : Math.Min(kids[i].DesiredSize.Width, slot);
                double h = fixedRows > 0 ? Math.Min(kids[i].DesiredSize.Height, row) : row;
                kids[i].Arrange(new Rect(x + (slot - w) / 2, y + (row - h) / 2, w, h));
            }
            y += row;
        }
        return final;
    }

    private (int Cols, double Slot) Grid(double width, int count)
    {
        double min = Math.Max(1, MinItemWidth);
        int fixedCols = Columns;
        if (double.IsInfinity(width) || double.IsNaN(width))
            return (fixedCols > 0 ? fixedCols : Math.Max(1, count), min);
        int cols = fixedCols > 0 ? fixedCols : Math.Max(1, (int)Math.Floor((width + 0.5) / min));
        return (cols, width / cols);
    }

    private List<UIElement> Visible()
    {
        var list = new List<UIElement>(InternalChildren.Count);
        foreach (UIElement c in InternalChildren)
            if (c.Visibility != Visibility.Collapsed) list.Add(c);
        return list;
    }
}
