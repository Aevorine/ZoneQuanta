using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ZoneQuanta.Core.Quick;
using ZoneQuanta.UI.Controls;
using Shapes = System.Windows.Shapes;

namespace ZoneQuanta.UI.Quick;

// Arranging the board while something is dragged over it. The layout is frozen into a snapshot when the drag starts;
// a dragged card floats under the pointer and a dashed frame marks the cells it would take, at once. The others make
// room only once the pointer has rested on that spot (Dwell), and only those the frame overlaps move (CellReflow);
// the spot changes only once the pointer is Hysteresis cells past a cell's edge, so it never flickers there. A drop
// stores the board exactly as shown (one undo step); Esc or a drop elsewhere lets everything glide back.
public sealed partial class QuickGrid
{
    private const double Hysteresis = 0.2;
    private readonly DispatcherTimer _dwell = new() { Interval = TimeSpan.FromMilliseconds(240) };
    private readonly Grid _cardFrame = DropFrame(18, new Thickness(0));
    // Everything on the board but the dragged element, as laid out when the drag started.
    private List<(UIElement Child, CellBox Box)>? _base;
    // Pins as they were, put back when the drag ends without a drop.
    private readonly List<(UIElement Child, int Col, int Row, long Rank)> _pins = new();
    private UIElement? _moving;
    private CellBox? _from, _aim, _roomFor;
    private FrameworkElement? _frame;
    private bool _committed;

    private bool Arranging => _base is not null;

    // Accent fill and dashed outline: where a dragged card or icon lands.
    private static Grid DropFrame(double radius, Thickness margin)
    {
        var frame = new Grid { IsHitTestVisible = false };
        var fill = new Border { CornerRadius = new CornerRadius(radius), Margin = margin, Opacity = 0.14 };
        fill.SetResourceReference(Border.BackgroundProperty, "Accent1Brush");
        var outline = new Shapes.Rectangle { RadiusX = radius, RadiusY = radius, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 }, Margin = margin };
        outline.SetResourceReference(Shapes.Shape.StrokeProperty, "Accent1Brush");
        frame.Children.Add(fill);
        frame.Children.Add(outline);
        return frame;
    }

    // moving: the dragged card or loose icon (null for an icon from a card, the folder view or another program).
    private void BeginArrange(UIElement? moving)
    {
        EndArrange();
        var snap = _board.Snapshot();
        _moving = moving;
        _from = null;
        foreach (var s in snap)
            if (s.Child == moving) _from = s.Box;
        _base = snap.Where(s => s.Child != moving && s.Child != _ghost && s.Child != _cardFrame).ToList();
        _pins.Clear();
        foreach (var s in snap)
            if (s.Child != _ghost && s.Child != _cardFrame)
                _pins.Add((s.Child, CellGridPanel.GetPinCol(s.Child), CellGridPanel.GetPinRow(s.Child), CellGridPanel.GetPinRank(s.Child)));
        _aim = _roomFor = null;
        _committed = false;
        _dwell.Stop();
        Apply(null);
    }

    // The cells a dragged block of c × r would take with its top-left corner at (col, row) in cell units: the nearest
    // ones, kept while the corner stays within half a cell plus Hysteresis of the current spot.
    private CellBox Spot(double col, double row, int c, int r, int maxRow)
    {
        if (_aim is { } a && a.C == c && a.R == r && Math.Abs(col - a.Col) < 0.5 + Hysteresis && Math.Abs(row - a.Row) < 0.5 + Hysteresis) return a;
        int maxCol = Math.Max(0, _board.Columns - c);
        return new CellBox(Math.Clamp((int)Math.Round(col), 0, maxCol), Math.Clamp((int)Math.Round(row), 0, Math.Max(0, maxRow)), c, r);
    }

    // Lowest row the others reach: a dragged block may go just under them, no further.
    private int BaseRows => _base is { Count: > 0 } b ? b.Max(s => s.Box.Bottom) : 0;

    // The frame goes to the spot at once; the others follow once the pointer rests there.
    private void Aim(CellBox? spot, FrameworkElement? frame)
    {
        // After a drop the layout stays as stored until the drag has ended.
        if (_base is null || _committed || (spot == _aim && frame == _frame)) return;
        _aim = spot;
        if (_frame is not null && (_frame != frame || spot is null)) RemoveFrame();
        if (spot is { } s && frame is not null)
        {
            _frame = frame;
            CellGridPanel.SetColSpan(frame, s.C);
            CellGridPanel.SetRowSpan(frame, s.R);
            if (frame.Parent is null) _board.Children.Add(frame);
        }
        Apply(_roomFor);
        _dwell.Stop();
        if (spot != _roomFor) _dwell.Start();
    }

    private void OnDwell()
    {
        _dwell.Stop();
        if (_base is null || _roomFor == _aim) return;
        _roomFor = _aim;
        Apply(_roomFor);
    }

    private CellBox[] Room(CellBox? spot)
    {
        var boxes = _base!.Select(s => s.Box).ToArray();
        return spot is { } s ? CellReflow.MakeRoom(_board.Columns, boxes, s, _from) : boxes;
    }

    // Holds the others where making room for `spot` puts them, and the frame at the aim.
    private void Apply(CellBox? spot)
    {
        var boxes = Room(spot);
        var hold = new Dictionary<UIElement, (int Col, int Row)>(boxes.Length + 1);
        for (int i = 0; i < boxes.Length; i++) hold[_base![i].Child] = (boxes[i].Col, boxes[i].Row);
        if (_frame is not null && _aim is { } a) hold[_frame] = (a.Col, a.Row);
        _board.Hold(hold);
    }

    // The drop: the others where they make room for the aim, the dragged card or icon at it, stored in reading order.
    // Dropped back where it was with nothing moved: no change, no undo step.
    private bool CommitArrange(QuickGroup? group, QuickItem? item, string name)
    {
        if (_base is null || _aim is not { } spot || (group is null && item is null)) return false;
        var boxes = Room(spot);
        int moved = 0;
        var cells = new List<(QuickGroup? Group, QuickItem? Item, int Col, int Row)>(boxes.Length + 1);
        for (int i = 0; i < boxes.Length; i++)
        {
            if (boxes[i] != _base[i].Box) moved++;
            if (_base[i].Child is GroupCard c) cells.Add((c.Group, null, boxes[i].Col, boxes[i].Row));
            else if (_base[i].Child is Tile { Parent: CellGridPanel } t && t.Item != item) cells.Add((null, t.Item, boxes[i].Col, boxes[i].Row));
        }
        if (spot == _from && moved == 0) return false;
        cells.Add((group, item, spot.Col, spot.Row));
        cells.Sort((a, b) => a.Row != b.Row ? a.Row.CompareTo(b.Row) : a.Col.CompareTo(b.Col));
        _actions.ArrangeBoard(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"place \"{name}\" at {spot.Col},{spot.Row}{(moved > 0 ? $" ({moved} made room)" : "")}"), cells);
        _committed = true;
        return true;
    }

    // Back to pins. After a drop they come from the store and are the layout just shown, so only the dragged card glides
    // into its cell; otherwise the pins from before the drag return and everything glides back.
    private void EndArrange()
    {
        _dwell.Stop();
        if (_base is null) return;
        if (_committed)
        {
            foreach (var (child, _) in _base) Repin(child);
            if (_moving is not null) Repin(_moving);
        }
        else foreach (var (child, col, row, rank) in _pins) CellGridPanel.SetPin(child, col, row, rank);
        _pins.Clear();
        _committed = false;
        RemoveFrame();
        _base = null;
        _moving = null;
        _from = _aim = _roomFor = null;
        _board.Float(null, default);
        _board.Hold(null);
    }

    private void RemoveFrame()
    {
        if (_frame?.Parent == _board) _board.Children.Remove(_frame);
        _frame = null;
    }

    private static void Repin(UIElement e)
    {
        if (e is GroupCard c) CellGridPanel.SetPin(c, c.Group.Col ?? -1, c.Group.Row ?? -1, c.Group.PinnedAt);
        else if (e is Tile { Parent: CellGridPanel } t) CellGridPanel.SetPin(t, t.Item.Col ?? -1, t.Item.Row ?? -1, t.Item.PinnedAt);
    }
}
