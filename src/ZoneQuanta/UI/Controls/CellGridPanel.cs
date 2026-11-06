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
// right edge; it is picked once per width and kept while content changes. Children that change cell glide.
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

    public static readonly DependencyProperty StretchProperty = DependencyProperty.Register(
        nameof(Stretch), typeof(bool), typeof(CellGridPanel),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsMeasure));

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
    private readonly Dictionary<UIElement, (int Col, int Row)> _cellOf = new();
    private double _arrangedWidth = double.NaN;
    // The column count is chosen once per width: a card growing or new items arriving never reflows the grid,
    // so what the user placed stays in its column.
    private double _pickedWidth = double.NaN, _pickedCell = double.NaN;
    private int _pickedCols;

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

    // Columns widen to fill the width (true), or keep CellWidth with the block centred (exact spacing).
    public bool Stretch
    {
        get => (bool)GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    // Left edge of the first column (non-zero only when not stretched).
    private double _offset;

    // Keeps the current column count (while a child is dragged, so the grid under the pointer holds still).
    public bool Frozen { get; set; }

    // While a drag arranges the grid: each held child sits exactly at its cell (no packing, column count kept), any other
    // takes the first free cell, and the floating child is drawn at its point on top without taking a cell.
    private IReadOnlyDictionary<UIElement, (int Col, int Row)>? _hold;
    private UIElement? _floating;
    private Point _floatAt;

    public void Hold(IReadOnlyDictionary<UIElement, (int Col, int Row)>? cells)
    {
        _hold = cells;
        InvalidateMeasure();
    }

    public void Float(UIElement? child, Point at)
    {
        bool same = _floating == child;
        _floating = child;
        _floatAt = at;
        if (same) InvalidateArrange();
        else InvalidateMeasure();
    }

    // Every visible child with the cells it covered at the last layout.
    public List<(UIElement Child, CellBox Box)> Snapshot()
    {
        var list = new List<(UIElement, CellBox)>();
        foreach (var kid in Visible())
            if (_cellOf.TryGetValue(kid, out var c))
                list.Add((kid, new CellBox(c.Col, c.Row, Math.Clamp(GetColSpan(kid), 1, Math.Max(1, Columns)), Math.Max(1, GetRowSpan(kid)))));
        return list;
    }

    // A point in cell units: (0, 0) is the top-left corner of the first cell, (1, 1) that of the next one diagonally.
    public (double Col, double Row) CellCoords(Point p) => ((p.X - _offset) / Math.Max(1, Track + Gap), p.Y / Math.Max(1, CellHeight));

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

    // Top-left cell a child was laid out at (null before its first layout).
    public (int Col, int Row)? CellOf(UIElement child) => _cellOf.TryGetValue(child, out var c) ? c : null;

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
            if (kids[i] == _floating)
            {
                // Follows the pointer as is; a glide still running would lag behind it.
                rect = new Rect(_floatAt, rect.Size);
                if (kids[i].GetValue(OffsetProperty) is TranslateTransform t)
                {
                    t.BeginAnimation(TranslateTransform.XProperty, null);
                    t.BeginAnimation(TranslateTransform.YProperty, null);
                }
            }
            else if (glide && _shown.TryGetValue(kids[i], out var was) && (Math.Abs(was.X - rect.X) > 0.5 || Math.Abs(was.Y - rect.Y) > 0.5))
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
        bool samePick = Math.Abs(width - _pickedWidth) < 0.5 && Math.Abs(CellWidth - _pickedCell) < 0.01 && _pickedCols <= widest;
        if (_hold is not null && Columns <= widest) best = Held(kids, Columns);
        else if (Frozen && Columns <= widest) best = Pack(kids, Columns, widest);
        else if (samePick) best = Pack(kids, _pickedCols, widest);
        else
        {
            for (int n = widest; n >= Math.Max(1, (int)Math.Ceiling(widest * 0.8)); n--)
            {
                var l = Pack(kids, n, widest);
                if (best is null || l.Cost < best.Cost) best = l; // a tie keeps the wider grid
            }
            (_pickedWidth, _pickedCell, _pickedCols) = (width, CellWidth, best!.Columns);
        }
        int cols = best!.Columns;
        double track = Math.Max(0, (width - gap * (cols - 1)) / cols);
        _offset = 0;
        if (!Stretch && track > CellWidth)
        {
            _offset = Math.Floor((width - (cols * CellWidth + gap * (cols - 1))) / 2);
            track = CellWidth;
        }
        Columns = cols;
        Track = track;
        RowsUsed = best.Rows;
        _rects.Clear();
        _cellOf.Clear();
        for (int i = 0; i < kids.Count; i++)
        {
            var (row, col, c, r) = best.Cells[i];
            var rect = new Rect(_offset + col * (track + gap), row * cellH, c * track + (c - 1) * gap, r * cellH);
            kids[i].Measure(rect.Size);
            _rects.Add(rect);
            _cellOf[kids[i]] = (col, row);
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

    // Held children at their cells, the floating one measured at its own cell, the rest in the first free spots.
    private Layout Held(List<UIElement> kids, int n)
    {
        var used = new List<bool[]>();
        var cells = new (int, int, int, int)[kids.Count];
        var rest = new List<int>();
        int rows = 0;
        for (int i = 0; i < kids.Count; i++)
        {
            int c = Math.Clamp(GetColSpan(kids[i]), 1, n), r = Math.Max(1, GetRowSpan(kids[i]));
            if (_hold!.TryGetValue(kids[i], out var at))
            {
                int col = Math.Clamp(at.Col, 0, n - c), row = Math.Max(0, at.Row);
                Mark(used, n, row, col, c, r);
                cells[i] = (row, col, c, r);
                rows = Math.Max(rows, row + r);
            }
            else if (kids[i] == _floating) cells[i] = _cellOf.TryGetValue(kids[i], out var was) ? (was.Row, Math.Min(was.Col, n - c), c, r) : (0, 0, c, r);
            else rest.Add(i);
        }
        foreach (int i in rest)
        {
            int c = Math.Clamp(GetColSpan(kids[i]), 1, n), r = Math.Max(1, GetRowSpan(kids[i]));
            var (row, col) = FirstFree(used, n, c, r);
            Mark(used, n, row, col, c, r);
            cells[i] = (row, col, c, r);
            rows = Math.Max(rows, row + r);
        }
        return new Layout(n, cells, rows, 0);
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
