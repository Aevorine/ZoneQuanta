using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ZoneQuanta.UI.Controls;

// Icon-cell grid, like a launcher home screen: columns of at least CellWidth share the width, rows are
// CellHeight, each child covers ColSpan × RowSpan cells. Pinned children (PinCol / PinRow) sit at their
// cell, highest PinRank first, pushed down only when another pinned child already holds it; the rest
// fill the first free spot from the top in order. The column count is picked between the widest fit and
// 80 % of it to leave the fewest empty cells, so cards of even widths do not strand a column at the
// right edge. Children that change cell glide there.
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

    public static readonly DependencyProperty ColSpanProperty = Attached("ColSpan", 1);
    public static readonly DependencyProperty RowSpanProperty = Attached("RowSpan", 1);
    public static readonly DependencyProperty PinColProperty = Attached("PinCol", -1);
    public static readonly DependencyProperty PinRowProperty = Attached("PinRow", -1);
    public static readonly DependencyProperty PinRankProperty = DependencyProperty.RegisterAttached(
        "PinRank", typeof(long), typeof(CellGridPanel),
        new FrameworkPropertyMetadata(0L, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    private static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached(
        "Offset", typeof(TranslateTransform), typeof(CellGridPanel));

    private static readonly Duration GlideTime = TimeSpan.FromMilliseconds(180);
    private readonly List<Rect> _rects = new();
    private Dictionary<UIElement, Rect> _shown = new();
    private double _arrangedWidth = double.NaN;

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

    // Keeps the current column count (while a child is dragged, so the grid under the pointer holds still).
    public bool Frozen { get; set; }

    // Columns, their width (a column step is Track + Gap) and rows in use at the last layout.
    public int Columns { get; private set; } = 1;
    public double Track { get; private set; }
    public int RowsUsed { get; private set; }

    public static int GetColSpan(UIElement e) => (int)e.GetValue(ColSpanProperty);
    public static void SetColSpan(UIElement e, int value) => e.SetValue(ColSpanProperty, value);
    public static int GetRowSpan(UIElement e) => (int)e.GetValue(RowSpanProperty);
    public static void SetRowSpan(UIElement e, int value) => e.SetValue(RowSpanProperty, value);
    public static int GetPinCol(UIElement e) => (int)e.GetValue(PinColProperty);
    public static int GetPinRow(UIElement e) => (int)e.GetValue(PinRowProperty);
    public static long GetPinRank(UIElement e) => (long)e.GetValue(PinRankProperty);

    // Pins a child at a cell (col or row < 0 lets it flow again); a higher rank wins an overlap.
    public static void SetPin(UIElement e, int col, int row, long rank)
    {
        e.SetValue(PinColProperty, col);
        e.SetValue(PinRowProperty, row);
        e.SetValue(PinRankProperty, rank);
    }

    // Top-left cell for a child of the given width whose top-left corner is at p (nearest cell), at most
    // maxRow down (a fixed bound: one that grew with the dragged child would let it run away downwards).
    public (int Col, int Row) CellAt(Point p, int colSpan, int maxRow)
    {
        double step = Track + Gap;
        int col = step > 0 ? (int)Math.Round(p.X / step) : 0;
        int row = (int)Math.Round(p.Y / Math.Max(1, CellHeight));
        return (Math.Clamp(col, 0, Math.Max(0, Columns - Math.Min(Math.Max(1, colSpan), Columns))), Math.Clamp(row, 0, Math.Max(0, maxRow)));
    }

    // Rows the other children fill at the current column count: the lowest a dragged child should go,
    // so dragging the bottom card down again does not walk it further each time.
    public int RowsWithout(UIElement child)
    {
        var kids = Visible();
        kids.Remove(child);
        return Pack(kids, Columns, Columns).Rows;
    }

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
        // A content change glides; a width change (window resize) snaps, since everything moves at once.
        bool glide = Math.Abs(final.Width - _arrangedWidth) < 0.5;
        _arrangedWidth = final.Width;
        var shown = new Dictionary<UIElement, Rect>(kids.Count);
        for (int i = 0; i < kids.Count && i < _rects.Count; i++)
        {
            var rect = _rects[i];
            if (glide && _shown.TryGetValue(kids[i], out var was) && (Math.Abs(was.X - rect.X) > 0.5 || Math.Abs(was.Y - rect.Y) > 0.5))
                Glide(kids[i], was.X - rect.X, was.Y - rect.Y);
            kids[i].Arrange(rect);
            shown[kids[i]] = rect;
        }
        _shown = shown;
        return final;
    }

    private sealed record Layout(int Columns, (int Row, int Col, int C, int R)[] Cells, int Rows, double Cost);

    private double Place(double width)
    {
        var kids = Visible();
        double gap = Gap, cellH = Math.Max(1, CellHeight);
        int widest = Math.Max(1, (int)Math.Floor((width + gap + 0.5) / (Math.Max(1, CellWidth) + gap)));
        Layout? best = null;
        if (Frozen && Columns <= widest) best = Pack(kids, Columns, widest);
        else
            for (int n = widest; n >= Math.Max(1, (int)Math.Ceiling(widest * 0.8)); n--)
            {
                var l = Pack(kids, n, widest);
                if (best is null || l.Cost < best.Cost) best = l; // a tie keeps the wider grid
            }
        int cols = best!.Columns;
        double track = Math.Max(0, (width - gap * (cols - 1)) / cols);
        Columns = cols;
        Track = track;
        RowsUsed = best.Rows;
        _rects.Clear();
        for (int i = 0; i < kids.Count; i++)
        {
            var (row, col, c, r) = best.Cells[i];
            var rect = new Rect(col * (track + gap), row * cellH, c * track + (c - 1) * gap, r * cellH);
            kids[i].Measure(rect.Size);
            _rects.Add(rect);
        }
        return best.Rows * cellH;
    }

    // Cost = empty cells above the last row + 4 per cell a pinned child is pushed from its spot
    // + 0.5 per column given up, so fewer columns are only taken when they close real gaps.
    private static Layout Pack(List<UIElement> kids, int n, int widest)
    {
        var used = new List<bool[]>();
        var cells = new (int, int, int, int)[kids.Count];
        var pinned = new bool[kids.Count];
        double moved = 0;
        var order = Enumerable.Range(0, kids.Count)
            .Where(i => GetPinCol(kids[i]) >= 0 && GetPinRow(kids[i]) >= 0)
            .OrderByDescending(i => GetPinRank(kids[i]));
        foreach (int i in order)
        {
            pinned[i] = true;
            int c = Math.Clamp(GetColSpan(kids[i]), 1, n), r = Math.Max(1, GetRowSpan(kids[i]));
            int wantCol = GetPinCol(kids[i]), wantRow = GetPinRow(kids[i]);
            int col = Math.Min(wantCol, n - c), row = wantRow;
            while (!Free(used, n, row, col, c, r)) row++;
            Mark(used, n, row, col, c, r);
            cells[i] = (row, col, c, r);
            moved += Math.Abs(col - wantCol) + (row - wantRow);
        }
        for (int i = 0; i < kids.Count; i++)
        {
            if (pinned[i]) continue;
            int c = Math.Clamp(GetColSpan(kids[i]), 1, n), r = Math.Max(1, GetRowSpan(kids[i]));
            var (row, col) = FirstFree(used, n, c, r);
            Mark(used, n, row, col, c, r);
            cells[i] = (row, col, c, r);
        }
        int holes = 0;
        for (int y = 0; y < used.Count - 1; y++)
            foreach (bool b in used[y])
                if (!b) holes++;
        return new Layout(n, cells, used.Count, holes + 4 * moved + 0.5 * (widest - n));
    }

    private static bool Free(List<bool[]> used, int n, int row, int col, int c, int r)
    {
        for (int y = row; y < row + r && y < used.Count; y++)
            for (int x = col; x < col + c && x < n; x++)
                if (used[y][x]) return false;
        return true;
    }

    private static void Mark(List<bool[]> used, int n, int row, int col, int c, int r)
    {
        for (int y = row; y < row + r; y++)
        {
            while (used.Count <= y) used.Add(new bool[n]);
            for (int x = col; x < col + c; x++) used[y][x] = true;
        }
    }

    private static (int Row, int Col) FirstFree(List<bool[]> used, int n, int c, int r)
    {
        for (int row = 0; ; row++)
            for (int col = 0; col + c <= n; col++)
                if (Free(used, n, row, col, c, r)) return (row, col);
    }

    // Starts from where the child is shown now (an unfinished glide included) and eases into its cell.
    private static void Glide(UIElement kid, double dx, double dy)
    {
        var t = Offset(kid);
        dx += t.X;
        dy += t.Y;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dx, 0, GlideTime) { EasingFunction = ease });
        t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dy, 0, GlideTime) { EasingFunction = ease });
    }

    // Our own translate, composed with whatever render transform the child already has (a tile's pop scale).
    private static TranslateTransform Offset(UIElement e)
    {
        if (e.GetValue(OffsetProperty) is TranslateTransform t) return t;
        t = new TranslateTransform();
        var current = e.RenderTransform;
        if (current is null || current == Transform.Identity) e.RenderTransform = t;
        else e.RenderTransform = new TransformGroup { Children = { current, t } };
        e.SetValue(OffsetProperty, t);
        return t;
    }

    private static DependencyProperty Attached(string name, int fallback) => DependencyProperty.RegisterAttached(
        name, typeof(int), typeof(CellGridPanel),
        new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    private List<UIElement> Visible()
    {
        var list = new List<UIElement>(InternalChildren.Count);
        foreach (UIElement c in InternalChildren)
            if (c.Visibility != Visibility.Collapsed) list.Add(c);
        return list;
    }
}
