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

// Icon-cell grid, like a launcher home screen: as many equal columns as CellWidth allows (the spare width
// shared between them), rows of CellHeight, each child covering ColSpan × RowSpan cells. Children are
// placed in order at the first free spot from the top, so a later small child fills a gap a large one
// left. A child keeps any spacing it wants inside its own cells.
public sealed class CellGridPanel : System.Windows.Controls.Panel
{
    public static readonly DependencyProperty CellWidthProperty = DependencyProperty.Register(
        nameof(CellWidth), typeof(double), typeof(CellGridPanel),
        new FrameworkPropertyMetadata(84.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty CellHeightProperty = DependencyProperty.Register(
        nameof(CellHeight), typeof(double), typeof(CellGridPanel),
        new FrameworkPropertyMetadata(130.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(CellGridPanel),
        new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ColSpanProperty = DependencyProperty.RegisterAttached(
        "ColSpan", typeof(int), typeof(CellGridPanel),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static readonly DependencyProperty RowSpanProperty = DependencyProperty.RegisterAttached(
        "RowSpan", typeof(int), typeof(CellGridPanel),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    private readonly List<Rect> _rects = new();

    // Narrowest column; the actual columns share out the width this leaves over.
    public double CellWidth
    {
        get => (double)GetValue(CellWidthProperty);
        set => SetValue(CellWidthProperty, value);
    }

    public double CellHeight
    {
        get => (double)GetValue(CellHeightProperty);
        set => SetValue(CellHeightProperty, value);
    }

    // Horizontal space between columns; rows have none (each child leaves its own room at the bottom).
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    // Columns and their width at the last layout (a column step is Track + Gap).
    public int Columns { get; private set; } = 1;
    public double Track { get; private set; }

    public static int GetColSpan(UIElement e) => (int)e.GetValue(ColSpanProperty);
    public static void SetColSpan(UIElement e, int value) => e.SetValue(ColSpanProperty, value);
    public static int GetRowSpan(UIElement e) => (int)e.GetValue(RowSpanProperty);
    public static void SetRowSpan(UIElement e, int value) => e.SetValue(RowSpanProperty, value);

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? CellWidth * 8 + Gap * 7 : available.Width;
        return new Size(width, Place(width));
    }

    protected override Size ArrangeOverride(Size final)
    {
        // Re-placing at the final width is cheap: children already measured at that size skip it.
        Place(final.Width);
        var kids = Visible();
        for (int i = 0; i < kids.Count && i < _rects.Count; i++) kids[i].Arrange(_rects[i]);
        return final;
    }

    private double Place(double width)
    {
        var kids = Visible();
        double gap = Gap, cellH = Math.Max(1, CellHeight);
        int n = Math.Max(1, (int)Math.Floor((width + gap + 0.5) / (Math.Max(1, CellWidth) + gap)));
        double track = Math.Max(0, (width - gap * (n - 1)) / n);
        Columns = n;
        Track = track;
        var used = new List<bool[]>();
        _rects.Clear();
        foreach (var kid in kids)
        {
            int c = Math.Clamp(GetColSpan(kid), 1, n), r = Math.Max(1, GetRowSpan(kid));
            var (row, col) = FirstFree(used, n, c, r);
            for (int y = row; y < row + r; y++)
            {
                while (used.Count <= y) used.Add(new bool[n]);
                for (int x = col; x < col + c; x++) used[y][x] = true;
            }
            var rect = new Rect(col * (track + gap), row * cellH, c * track + (c - 1) * gap, r * cellH);
            kid.Measure(rect.Size);
            _rects.Add(rect);
        }
        return used.Count * cellH;
    }

    private static (int Row, int Col) FirstFree(List<bool[]> used, int n, int c, int r)
    {
        for (int row = 0; ; row++)
            for (int col = 0; col + c <= n; col++)
            {
                bool free = true;
                for (int y = row; y < row + r && y < used.Count && free; y++)
                    for (int x = col; x < col + c; x++)
                        if (used[y][x]) { free = false; break; }
                if (free) return (row, col);
            }
    }

    private List<UIElement> Visible()
    {
        var list = new List<UIElement>(InternalChildren.Count);
        foreach (UIElement c in InternalChildren)
            if (c.Visibility != Visibility.Collapsed) list.Add(c);
        return list;
    }
}
