using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ZoneQuanta.Core.Quick;
using ZoneQuanta.UI.Controls;
using Shapes = System.Windows.Shapes;

namespace ZoneQuanta.UI.Quick;

// The grouped board, laid out like a launcher home screen: a grid of icon cells in which each group is a
// rounded card covering columns × rows cells (chosen per group, or a default) with its name underneath,
// and each ungrouped item one cell. Everything is placed in order at the first free spot from the top,
// so loose icons and small cards fill gaps that larger cards leave; what the user dropped somewhere stays there.
// Dragging an icon shows where it will land: over a card, a bar at the slot it will take; over an icon's middle,
// that icon ringed (the two make a group); anywhere else, a placeholder in the cell the icon will be pinned to
// while the icons around make room. Drag a card anywhere: it follows the pointer cell by cell while the others
// make room, and stays pinned where it is dropped. Unfolding a card keeps it where it is.
public sealed partial class QuickGrid
{
    // Chrome of a one-column (small icon) card: padding 8 + border 1 on each side.
    private const double CardChrome = 18;
    // Room under each card for its name (and the space before the next row of cards).
    private const double LabelStrip = BoardMetrics.LabelStrip;
    // Spacing from the settings, refreshed on every build.
    private BoardMetrics _m;
    // The middle part of an icon (fraction of its size) where a drop makes a group instead of taking the cell.
    private const double MergeZone = 0.56;
    // The vertical gutter is always reserved: a scroll bar appearing must not change the width and reflow the grid.
    private readonly ScrollViewer _boardScroll = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Focusable = false,
        Background = Brushes.Transparent,
        AllowDrop = true,
    };
    private readonly CellGridPanel _board = new() { Background = Brushes.Transparent, Margin = new Thickness(2, 2, 2, 4) };
    private readonly Grid _boardHost = new();
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly Border _slotBar = new() { Width = 3, CornerRadius = new CornerRadius(1.5), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly Grid _ghost = new() { IsHitTestVisible = false };
    private readonly Dictionary<QuickItem, Tile> _boardTiles = new();
    private readonly List<GroupCard> _cards = new();
    private readonly Dictionary<string, (int Col, int Row)> _lastCells = new(StringComparer.OrdinalIgnoreCase);
    private GroupCard? _downCard, _dropCard, _lifted;
    private Point _grab;
    private (int Col, int Row)? _liftTarget;
    private int _liftRows;
    private Tile? _focused;

    // Where a dragged icon lands, as shown while it is over the board.
    private enum DropKind { None, IntoCard, Merge, Cell }
    private readonly record struct DropPlan(DropKind Kind, GroupCard? Card = null, int Slot = -1, Tile? With = null, int Col = -1, int Row = -1);
    private DropPlan _plan;
    private (int Col, int Row)? _ghostAt;
    private Tile? _carried;
    private readonly DispatcherTimer _leaveTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };

    // Card fill: the launcher window sits on SurfaceBrush, the panel page on CardBrush.
    public string GroupBackground { get; set; } = "CardBrush";

    // A grid row is one row of loose icons. A card with n icon rows covers at most n + 1 grid rows (the extra
    // one holds its border and name), fewer when its rows are tighter, so cards still line up on whole rows.
    private double RowHeight => _m.CellHeight;
    private double ColumnGap => _m.Group;

    // Columns × rows of a group card: its own size, else the default, else by item count.
    private (int Cols, int Rows) CardSize(QuickGroup group, int count)
    {
        if (group.Cols > 0 && group.Rows > 0) return (group.Cols, group.Rows);
        var s = _actions.Settings;
        if (s.QuickCardCols > 0 && s.QuickCardRows > 0) return (s.QuickCardCols, s.QuickCardRows);
        var (c0, r0) = count <= 2 ? (2, 1) : count <= 4 ? (count, 1) : (4, 2);
        if (_m.Standard) return (c0, r0);
        // Tighter spacing: the fewest cells that still show as many icons as the usual size would.
        int need = Math.Min(count, _m.ColsFor(c0) * _m.RowsFor(r0));
        var best = (c0, r0);
        for (int r = 1; r <= r0; r++)
            for (int c = 2; c <= c0; c++)
                if (_m.ColsFor(c) * _m.RowsFor(r) >= need && (c * r < best.Item1 * best.Item2 || (c * r == best.Item1 * best.Item2 && c > best.Item1)))
                    best = (c, r);
        return best;
    }

    private void InitBoard()
    {
        _m = BoardMetrics.From(_actions.Settings);
        _boardHost.Children.Add(_board);
        _overlay.Children.Add(_slotBar);
        _boardHost.Children.Add(_overlay);
        _slotBar.SetResourceReference(Border.BackgroundProperty, "Accent1Brush");
        var outline = new Shapes.Rectangle { RadiusX = 14, RadiusY = 14, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 }, Margin = new Thickness(6, 4, 6, 10) };
        outline.SetResourceReference(Shapes.Shape.StrokeProperty, "Accent1Brush");
        var fill = new Border { CornerRadius = new CornerRadius(14), Margin = new Thickness(6, 4, 6, 10), Opacity = 0.14 };
        fill.SetResourceReference(Border.BackgroundProperty, "Accent1Brush");
        _ghost.Children.Add(fill);
        _ghost.Children.Add(outline);
        _boardScroll.Content = _boardHost;
        _boardRoot.Children.Add(_boardScroll);
        _boardScroll.PreviewMouseLeftButtonDown += OnBoardDown;
        _boardScroll.PreviewMouseMove += OnBoardMove;
        _boardScroll.PreviewMouseLeftButtonUp += OnBoardUp;
        _boardScroll.PreviewKeyDown += OnBoardKey;
        _boardScroll.DragOver += OnBoardDragOver;
        // Leaving one child for another also raises DragLeave, immediately followed by DragOver; only a leave no
        // DragOver follows (out of the window, Esc, a drag from another program cancelled) clears the plan.
        _leaveTimer.Tick += (_, _) =>
        {
            _leaveTimer.Stop();
            ShowPlan(default);
        };
        _boardScroll.DragLeave += (_, _) => _leaveTimer.Start();
        _boardScroll.Drop += OnBoardDrop;
    }

    private void BuildBoard()
    {
        ClearBoard();
        var before = _m;
        _m = BoardMetrics.From(_actions.Settings);
        if (_m != before)
        {
            double room = 2 * _m.CellHeight - LabelStrip;
            Core.Log.Write(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"quick board spacing: icon gap {_m.GapX:0}x{_m.GapY:0}, border {_m.PadX:0}x{_m.PadY:0}, card gap {_m.Group:0}; one-row card {_m.CardRows(1, room).Face:0.#} of {room:0.#}"));
        }
        _board.CellWidth = _m.CellWidth;
        _board.CellHeight = _m.CellHeight;
        _board.Gap = _m.Group;
        _board.Stretch = _actions.Settings.QuickFill;
        var h = _actions.Highlight;
        foreach (var (group, items) in _actions.Sections())
        {
            if (group is null)
            {
                // A loose icon takes one cell, so it can fill a gap beside or under a card; one the user dropped
                // on a cell stays there.
                foreach (var item in items)
                {
                    var tile = BoardTile(item, "Card");
                    if (item.Col is int c && item.Row is int r) CellGridPanel.SetPin(tile, c, r, item.PinnedAt);
                    else CellGridPanel.SetPin(tile, -1, -1, 0);
                    _board.Children.Add(tile);
                }
                continue;
            }
            bool peek = h is not null && items.Contains(h);
            var card = new GroupCard(this, group, items, peek);
            if (group.Col is int col && group.Row is int row) CellGridPanel.SetPin(card, col, row, group.PinnedAt);
            // Unfolded for a moment to show a new item: held at its cell, so nothing else moves for it.
            else if (peek && _lastCells.TryGetValue(group.Name, out var was)) CellGridPanel.SetPin(card, was.Col, was.Row, long.MaxValue - 1);
            _cards.Add(card);
            _board.Children.Add(card);
        }

        if (h is not null && _boardTiles.TryGetValue(h, out var fresh) && fresh.Parent is not null)
        {
            // A new item that automatic grouping is about to place keeps the highlight until it lands.
            if (!(QuickStore.NeedsGroup(h) && _actions.Settings.QuickAutoGroup)) _actions.ClearHighlight();
            _focused = fresh;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ((FrameworkElement?)CardAt(fresh) ?? fresh).BringIntoView();
                fresh.Pop();
            }), DispatcherPriority.Loaded);
        }
        if (_actions.PendingRename is { } pending && _cards.FirstOrDefault(c => c.Group == pending) is { } named)
        {
            _actions.ClearPendingRename();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                named.BringIntoView();
                named.BeginRename();
            }), DispatcherPriority.Loaded);
        }
    }

    private void ClearBoard()
    {
        foreach (var c in _cards)
        {
            if (_board.CellOf(c) is { } at) _lastCells[c.Group.Name] = at;
            c.Detach();
        }
        _cards.Clear();
        _board.Children.Clear();
        _dropCard = null;
        _dropTile = null;
        _ghostAt = null;
        _plan = default;
        _slotBar.Visibility = Visibility.Collapsed;
    }

    private Tile BoardTile(QuickItem item, string view)
    {
        if (!_boardTiles.TryGetValue(item, out var tile))
        {
            tile = new Tile(item, _actions.Settings);
            tile.EnableHover();
            tile.ContextMenu = new ContextMenu();
            tile.ContextMenuOpening += OnMenu;
            tile.GotKeyboardFocus += (_, _) => _focused = tile;
            _boardTiles[item] = tile;
        }
        Adopt(tile, "board");
        tile.Margin = view == "Grid" ? new Thickness(4, 6, 4, 6) : new Thickness(0);
        tile.VerticalAlignment = VerticalAlignment.Top;
        tile.Layout(view, _m);
        return tile;
    }

    private void FocusBoard()
    {
        var first = _focused is { Parent: not null } f ? f : BoardTiles().FirstOrDefault();
        first?.Focus();
        first?.BringIntoView();
    }

    private IEnumerable<Tile> BoardTiles() => _boardTiles.Values.Where(t => t.Parent is not null && t.IsVisible);

    private static GroupCard? CardAt(object source)
    {
        for (var d = source as DependencyObject; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is GroupCard c) return c;
        return null;
    }

    private void OnBoardDown(object sender, MouseButtonEventArgs e)
    {
        var tile = TileAt(e.OriginalSource);
        var card = CardAt(e.OriginalSource);
        if (e.ClickCount == 2)
        {
            if (tile is not null && !_singleClick && !tile.Renaming) _actions.Open(tile.Item);
            else if (tile is null && card is not null && card.IsLabel(e.OriginalSource)) card.BeginRename();
            return;
        }
        _downTile = tile;
        _downCard = tile is null && card is not null && !card.Renaming && !card.IsControl(e.OriginalSource) ? card : null;
        if (_downCard is not null) _grab = e.GetPosition(_downCard);
        _downAt = e.GetPosition(_boardScroll);
        _dragging = false;
    }

    private void OnBoardMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragging || (_downTile is null && _downCard is null)) return;
        if (_downTile?.Renaming == true) return;
        var d = e.GetPosition(_boardScroll) - _downAt;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragging = true;
        try
        {
            if (_downTile is { } tile)
            {
                var data = new DataObject();
                data.SetData(DragFormat, tile.Item.Path);
                if (QuickActions.Exists(tile.Item.Path)) data.SetData(DataFormats.FileDrop, new[] { tile.Item.Path });
                _board.Frozen = true;
                // A loose icon leaves its cell while carried (the placeholder shows where it goes); one in a card dims.
                bool loose = tile.Parent == _board;
                _carried = tile;
                if (loose) tile.Visibility = Visibility.Collapsed;
                else tile.Opacity = 0.35;
                DragDrop.DoDragDrop(tile, data, DragDropEffects.Copy | DragDropEffects.Link);
            }
            else if (_downCard is { } card)
            {
                Lift(card);
                DragDrop.DoDragDrop(card, new DataObject(GroupDragFormat, card.Group.Name), DragDropEffects.Move);
            }
        }
        finally
        {
            Settle();
            MarkBoardDrop(null, null);
            _downTile = null;
            _downCard = null;
        }
    }

    private void OnBoardUp(object sender, MouseButtonEventArgs e)
    {
        var tile = TileAt(e.OriginalSource);
        bool click = !_dragging && tile is not null && tile == _downTile && !tile.Renaming;
        var card = !_dragging && tile is null && _downCard is { } dc && CardAt(e.OriginalSource) == dc && dc.IsFace(e.OriginalSource) ? dc : null;
        _downTile = null;
        _downCard = null;
        if (click && _singleClick) _actions.Open(tile!.Item);
        else if (card is { Folded: true }) OpenFolder(card);
    }

    private void OnBoardKey(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        if ((Keyboard.FocusedElement as Tile ?? _focused) is not { Parent: not null } tile) return;
        if (Keyboard.Modifiers == ModifierKeys.None && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            Step(tile, e.Key);
            e.Handled = true;
            return;
        }
        if (ItemKey(tile, e)) e.Handled = true;
    }

    // Spatial arrow navigation across cards and loose tiles: nearest tile in that direction,
    // straying sideways counts double.
    private void Step(Tile from, Key key)
    {
        Point Center(Tile t) => t.TranslatePoint(new Point(t.ActualWidth / 2, t.ActualHeight / 2), _board);
        var o = Center(from);
        Tile? best = null;
        double bestScore = double.MaxValue;
        foreach (var t in BoardTiles())
        {
            if (t == from) continue;
            var c = Center(t);
            double dx = c.X - o.X, dy = c.Y - o.Y;
            double along = key switch { Key.Right => dx, Key.Left => -dx, Key.Down => dy, _ => -dy };
            double side = key is Key.Left or Key.Right ? Math.Abs(dy) : Math.Abs(dx);
            if (along < 4) continue;
            double score = along + side * 2;
            if (score < bestScore)
            {
                bestScore = score;
                best = t;
            }
        }
        if (best is not null)
        {
            best.Focus();
            best.BringIntoView();
        }
        else if (key == Key.Up) LeftTop?.Invoke();
    }

    private void OnBoardDragOver(object sender, DragEventArgs e)
    {
        _leaveTimer.Stop();
        EdgeScroll(e);
        if (e.Data.GetDataPresent(GroupDragFormat))
        {
            e.Effects = _lifted is not null ? DragDropEffects.Move : DragDropEffects.None;
            if (_lifted is not null) Follow(e);
            MarkBoardDrop(null, null);
        }
        else if (e.Data.GetDataPresent(DragFormat) || e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var dragged = e.Data.GetData(DragFormat) is string p ? _actions.Store.Find(p) : null;
            ShowPlan(PlanAt(e, dragged));
            e.Effects = _plan.Kind == DropKind.None ? DragDropEffects.None : DragDropEffects.Link;
        }
        else
        {
            e.Effects = DragDropEffects.None;
            ShowPlan(default);
        }
        e.Handled = true;
    }

    // What a drop at the pointer would do. Over a card: the slot nearest the pointer. Over the middle of a loose
    // icon: a group of the two. Anywhere else: the cell under the pointer (the placeholder, once shown, holds it).
    private DropPlan PlanAt(DragEventArgs e, QuickItem? dragged)
    {
        var tile = TileAt(e.OriginalSource);
        if (CardAt(e.OriginalSource) is { } card) return new DropPlan(DropKind.IntoCard, card, card.SlotAt(e.GetPosition(card)));
        if (tile is not null && tile.Item != dragged && tile.Parent == _board)
        {
            var at = e.GetPosition(tile);
            double mx = tile.ActualWidth * (1 - MergeZone) / 2, my = tile.ActualHeight * (1 - MergeZone) / 2;
            bool middle = at.X > mx && at.X < tile.ActualWidth - mx && at.Y > my && at.Y < tile.ActualHeight - my;
            if (middle) return new DropPlan(DropKind.Merge, With: tile);
            var cell = _board.CellOf(tile);
            if (cell is { } c) return new DropPlan(DropKind.Cell, Col: c.Col, Row: c.Row);
        }
        var (col, row) = _board.CellUnder(e.GetPosition(_board));
        return new DropPlan(DropKind.Cell, Col: col, Row: row);
    }

    private void ShowPlan(DropPlan plan)
    {
        _plan = plan;
        MarkBoardDrop(plan.Kind == DropKind.IntoCard ? plan.Card : null, plan.Kind == DropKind.Merge ? plan.With : null);
        if (plan.Kind == DropKind.IntoCard && plan.Card is { } card && card.SlotBar(plan.Slot, _overlay) is { } bar)
        {
            Canvas.SetLeft(_slotBar, bar.X - 1.5);
            Canvas.SetTop(_slotBar, bar.Y);
            _slotBar.Height = bar.Height;
            _slotBar.Visibility = Visibility.Visible;
        }
        else _slotBar.Visibility = Visibility.Collapsed;
        if (plan.Kind == DropKind.Cell) ShowGhost(plan.Col, plan.Row);
        // Over a card or an icon the placeholder keeps its cell (moving it would shift what is under the pointer)
        // but is not drawn.
        _ghost.Opacity = plan.Kind == DropKind.Cell ? 1 : 0;
        if (plan.Kind == DropKind.None) HideGhost();
    }

    // The placeholder is a cell of the grid pinned above everything, so the icons around make room for it live.
    private void ShowGhost(int col, int row)
    {
        if (_ghostAt == (col, row) && _ghost.Parent == _board) return;
        _ghostAt = (col, row);
        if (_ghost.Parent is null) _board.Children.Add(_ghost);
        CellGridPanel.SetPin(_ghost, col, row, long.MaxValue);
    }

    private void HideGhost()
    {
        _ghostAt = null;
        if (_ghost.Parent == _board) _board.Children.Remove(_ghost);
    }

    private void OnBoardDrop(object sender, DragEventArgs e)
    {
        var plan = _plan;
        MarkBoardDrop(null, null);
        e.Handled = true;

        if (e.Data.GetData(GroupDragFormat) is string)
        {
            if (_lifted is { } lifted && _liftTarget is { } at)
            {
                _liftTarget = null;
                _actions.PlaceGroup(lifted.Group, at.Col, at.Row);
            }
            return;
        }
        var dragged = e.Data.GetData(DragFormat) is string path ? _actions.Store.Find(path) : null;
        if (dragged is null && e.Data.GetDataPresent(DragFormat)) return;
        if (plan.Kind == DropKind.None) plan = PlanAt(e, dragged);
        ShowPlan(default);
        if (dragged is not null)
        {
            DropAt(dragged, plan);
            return;
        }
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            // Files from outside join the card they were dropped on; they take the exact slot only in the custom
            // order (another sort the user chose stays as it is).
            var added = _actions.Add(files, -1, plan.Kind == DropKind.IntoCard ? plan.Card!.Group.Name : null);
            bool custom = _actions.Settings.QuickSort == "Custom";
            foreach (var item in added)
                if (plan.Kind != DropKind.IntoCard || custom) DropAt(item, plan);
        }
    }

    private void DropAt(QuickItem item, DropPlan plan)
    {
        switch (plan.Kind)
        {
            case DropKind.IntoCard when plan.Card is { } card:
                _actions.PlaceInGroup(item, card.Group.Name, card.ItemAt(plan.Slot, item));
                break;
            case DropKind.Merge when plan.With is { } with && with.Item != item:
                _actions.NewGroup(new[] { with.Item, item });
                break;
            case DropKind.Cell:
                _actions.PlaceItem(item, plan.Col, plan.Row);
                break;
        }
    }

    // The card being dragged is laid out first, at the cell under the pointer; the column count holds still.
    private void Lift(GroupCard card)
    {
        _lifted = card;
        _liftTarget = null;
        _liftRows = _board.RowsWithout(card); // lowest drop: just under all the other cards
        _board.Frozen = true;
        card.Opacity = 0.8;
        System.Windows.Controls.Panel.SetZIndex(card, 1);
    }

    private void Follow(DragEventArgs e)
    {
        var card = _lifted!;
        var p = e.GetPosition(_board);
        var at = _board.CellAt(new Point(p.X - _grab.X, p.Y - _grab.Y), CellGridPanel.GetColSpan(card), _liftRows);
        if (_liftTarget != at)
        {
            _liftTarget = at;
            CellGridPanel.SetPin(card, at.Col, at.Row, long.MaxValue);
        }
    }

    // Any drag near the top or bottom edge scrolls the board, faster the closer it gets; DragOver keeps
    // coming while the pointer rests, so holding there keeps scrolling.
    private void EdgeScroll(DragEventArgs e)
    {
        double y = e.GetPosition(_boardScroll).Y, edge = 48;
        if (y < edge) _boardScroll.ScrollToVerticalOffset(_boardScroll.VerticalOffset - (edge - y) / 2);
        else if (y > _boardScroll.ActualHeight - edge) _boardScroll.ScrollToVerticalOffset(_boardScroll.VerticalOffset + (y - _boardScroll.ActualHeight + edge) / 2);
    }

    // Back to where the store says the card belongs: its new cell after a drop, the old one after Esc
    // or a release outside the board.
    private void Settle()
    {
        _board.Frozen = false;
        ShowPlan(default);
        if (_carried is { } carried)
        {
            _carried = null;
            carried.Visibility = Visibility.Visible;
            carried.ClearValue(OpacityProperty);
            carried.RefreshMeta(true);
        }
        if (_lifted is not { } card) return;
        _lifted = null;
        _liftTarget = null;
        card.Opacity = 1;
        System.Windows.Controls.Panel.SetZIndex(card, 0);
        var g = card.Group;
        CellGridPanel.SetPin(card, g.Col ?? -1, g.Row ?? -1, g.PinnedAt);
    }

    private void MarkBoardDrop(GroupCard? card, Tile? tile)
    {
        if (_dropCard != card)
        {
            _dropCard?.SetDropTarget(false);
            _dropCard = card;
            _dropCard?.SetDropTarget(true);
        }
        MarkDrop(tile);
    }

    private void OnGroupMenu(object sender, ContextMenuEventArgs e)
    {
        var card = (GroupCard)sender;
        // Right-click on a tile bubbles here too; that tile's own menu is the one that opens.
        // (Setting Handled would cancel it.)
        if (TileAt(e.OriginalSource) is not null) return;
        FillGroupMenu(card.ContextMenu!, card, card.BeginRename, inFolder: false);
    }

    // The group's menu, the same on its card and in its folder view (there, unfolding in place closes the view).
    private void FillGroupMenu(ContextMenu menu, GroupCard card, Action rename, bool inFolder)
    {
        menu.Items.Clear();
        Add(menu, "撤销", "Icon.Reset", "Ctrl+Z", () => _actions.Undo(), _actions.Store.CanUndo);
        Add(menu, "重做", "Icon.Right", "Ctrl+Y", () => _actions.Redo(), _actions.Store.CanRedo);
        menu.Items.Add(new Separator());
        Add(menu, "重命名分组", "Icon.Edit", "", rename, true);
        var group = card.Group;
        menu.Items.Add(CardSizeMenu(menu, group.Cols, group.Rows, "跟随默认", (c, r) => _actions.SetSize(group, c, r)));
        if (card.CanExpand)
        {
            if (card.Folded && !inFolder) Add(menu, "查看全部", "Icon.Grid", "", () => OpenFolder(card), true);
            Add(menu, group.Expanded ? "收起" : "原地展开", group.Expanded ? "Icon.Up" : "Icon.Down", "", () =>
            {
                if (inFolder && !group.Expanded) CloseFolder(animate: false);
                card.Toggle();
            }, true);
        }
        Add(menu, "按内容命名", "Icon.Bolt", "", () => _actions.NameByContent(group), true);
        if (group.Col is not null) Add(menu, "取消固定位置", "Icon.Pin", "", () => _actions.Unpin(group), true);
        if (_actions.Store.Groups.Any(g => g.Col is not null) || _actions.Store.Items.Any(i => i.Col is not null)) Add(menu, "全部自动排列", "Icon.Groups", "", () => _actions.Unpin(null), true);
        Add(menu, "解散分组", "Icon.Close", "", () => _actions.Dissolve(group), true);
        menu.Items.Add(new Separator());
        var auto = Add(menu.Items, "自动分类", "Icon.Groups", "", () => _actions.Settings.QuickAutoGroup = !_actions.Settings.QuickAutoGroup, true);
        auto.IsChecked = _actions.Settings.QuickAutoGroup;
    }

    // "卡片大小": the columns × rows picker, then the way back to automatic sizing (0 × 0).
    public static MenuItem CardSizeMenu(ContextMenu root, int cols, int rows, string autoText, Action<int, int> set)
    {
        var sub = new MenuItem { Header = "卡片大小", Tag = Application.Current.TryFindResource("Icon.Grid") };
        var picker = new SizePicker(QuickGroup.MaxCols, QuickGroup.MaxRows, cols, rows, autoText);
        picker.Picked += (c, r) =>
        {
            root.IsOpen = false;
            set(c, r);
        };
        var host = new MenuItem { Header = picker };
        host.SetResourceReference(FrameworkElement.StyleProperty, "PlainMenuItem");
        sub.Items.Add(host);
        sub.Items.Add(new Separator());
        var auto = Add(sub.Items, autoText, "", "", () => set(0, 0), true);
        auto.IsChecked = cols <= 0 || rows <= 0;
        return sub;
    }

    // One group: a rounded card in its columns × rows of grid cells, with the name underneath. Icons sit at the
    // pitch the spacing settings give (small icons in a one-column card, as many as fit); the card is as tall
    // as its icon rows plus the border distance above and below (BoardMetrics.CardRows), the name right under it.
    // The pill beside the name ("+N") opens the folder view with every member; Ctrl+click or the menu unfolds them in place, remembered per group.
    private sealed class GroupCard : DockPanel
    {
        private readonly QuickGrid _owner;
        private readonly UniformWrapPanel _cells = new();
        private readonly Border _card;
        private readonly Grid _face = new() { VerticalAlignment = VerticalAlignment.Top };
        private double _slot, _rowH;
        private readonly Grid _labelHost = new() { Height = LabelStrip, Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        private readonly StackPanel _labelRow = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4, 6, 4, 0) };
        private readonly TextBlock _label;
        private readonly Button _more = new() { Margin = new Thickness(6, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        private readonly List<QuickItem> _items;
        private readonly System.Windows.Controls.Primitives.Thumb _grip;
        private readonly Border _badge;
        private readonly TextBlock _badgeText = new() { FontSize = 12 };
        private int _wantCols, _wantRows;
        private TextBox? _editor;
        private bool _peek, _resizing;
        private Shape _shape;
        private Point _gripAt;
        private int _startCols, _startRows;

        // How the card draws at its current width: small icons or full tiles, icon columns, icon rows
        // while folded, icon rows drawn now, and the grid rows it covers; then the icon slot and row height.
        private readonly record struct Shape(bool Mini, int Cols, int Rows, int DrawnRows, int Span, double Slot = 0, double RowH = 0, double Inset = 0, double Face = 0)
        {
            public bool SameCells(Shape o) => Mini == o.Mini && Cols == o.Cols && Rows == o.Rows && DrawnRows == o.DrawnRows && Span == o.Span;
        }

        public GroupCard(QuickGrid owner, QuickGroup group, List<QuickItem> items, bool peek)
        {
            _owner = owner;
            _items = items;
            _peek = peek;
            Group = group;
            // No background: the cells below a card shorter than them belong to the board, not the card.
            Background = null;
            LastChildFill = false;
            (_wantCols, _wantRows) = owner.CardSize(group, items.Count);
            CellGridPanel.SetColSpan(this, _wantCols);

            _card = new Border
            {
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(8),
                BorderThickness = new Thickness(1),
                Child = _cells,
            };
            _card.SetResourceReference(Border.BackgroundProperty, owner.GroupBackground);
            _card.SetResourceReference(Border.BorderBrushProperty, "LineBrush");

            _label = new TextBlock
            {
                Text = group.Name,
                FontSize = 13,
                Margin = new Thickness(0, 1, 0, 0),
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = $"{group.Name} · {items.Count}",
            };
            _label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            _more.SetResourceReference(StyleProperty, "Pill");
            // Folded: the folder view (Ctrl+click unfolds in place, as before). Unfolded: fold back.
            _more.Click += (_, e) =>
            {
                if (Group.Expanded || _peek || (Keyboard.Modifiers & ModifierKeys.Control) != 0) Toggle();
                else _owner.OpenFolder(this);
                e.Handled = true;
            };
            _labelRow.Children.Add(_label);
            _labelRow.Children.Add(_more);
            _labelHost.Children.Add(_labelRow);

            // Bottom-right grip: drag to resize by whole cells, the size shown live beside it.
            _grip = new System.Windows.Controls.Primitives.Thumb
            {
                Margin = new Thickness(0, 0, 3, 3), Opacity = 0, ToolTip = "调整大小",
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            };
            _grip.SetResourceReference(StyleProperty, "CornerGrip");
            System.Windows.Automation.AutomationProperties.SetName(_grip, "调整大小");
            _badgeText.SetResourceReference(TextBlock.ForegroundProperty, "Accent1Brush");
            _badge = new Border
            {
                CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 3, 8, 3), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 28, 6),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Visibility = Visibility.Collapsed, IsHitTestVisible = false, Child = _badgeText,
            };
            _badge.SetResourceReference(Border.BackgroundProperty, "RaisedBrush");
            _badge.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
            _face.Children.Add(_card);
            _face.Children.Add(_badge);
            _face.Children.Add(_grip);
            SetDock(_face, Dock.Top);
            Children.Add(_face);
            SetDock(_labelHost, Dock.Top);
            Children.Add(_labelHost);
            MouseEnter += (_, _) => ShowGrip(true);
            MouseLeave += (_, _) => { if (!_resizing) ShowGrip(false); };
            _grip.DragStarted += (_, _) => BeginResize();
            _grip.DragDelta += (_, _) => TrackResize();
            _grip.DragCompleted += (_, e) => EndResize(commit: !e.Canceled);
            ContextMenu = new ContextMenu();
            ContextMenuOpening += owner.OnGroupMenu;
            // Shaped for the narrowest column width first; MeasureOverride corrects it for the real width.
            Apply(ShapeFor(NarrowWidth));
        }

        public QuickGroup Group { get; }
        public IReadOnlyList<QuickItem> Items => _items;
        public bool Renaming => _editor is not null;
        public bool CanExpand => _items.Count > _shape.Cols * _shape.Rows;
        public bool Folded => CanExpand && !Group.Expanded && !_peek;
        public int Columns => _shape.Mini ? Math.Max(2, _wantCols) : _shape.Cols;
        // The card face in the given element's coordinates (where the folder view grows from).
        public Rect FaceIn(Visual to) => _face.TransformToVisual(to).TransformBounds(new Rect(0, 0, _face.ActualWidth, _face.ActualHeight));

        // The face, not the name strip or the controls: a click there opens the folder view.
        public bool IsFace(object source)
        {
            if (IsControl(source)) return false;
            for (var d = source as DependencyObject; d is not null && d != this; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d == _face) return true;
            return false;
        }

        private double NarrowWidth => _wantCols * _owner._m.CellWidth + (_wantCols - 1) * _owner.ColumnGap;

        protected override Size MeasureOverride(Size constraint)
        {
            if (!double.IsInfinity(constraint.Width))
            {
                var shape = ShapeFor(constraint.Width);
                if (shape != _shape) Apply(shape);
                // The name gives way to the pill: it is trimmed, the pill always shows.
                double pill = _more.Visibility == Visibility.Visible ? 46 : 0;
                _label.MaxWidth = Math.Max(24, constraint.Width - 12 - pill);
            }
            return base.MeasureOverride(constraint);
        }

        // A card being resized previews its folded size.
        private bool Unfolded(int capacity) => !_resizing && (Group.Expanded || _peek) && _items.Count > capacity;

        // The resize grip and the unfold pill are controls of their own: pressing them never drags the card.
        public bool IsControl(object source)
        {
            for (var d = source as DependencyObject; d is not null && d != this; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d == _grip || d == _more) return true;
            return false;
        }

        // Insertion slot nearest a point (card coordinates) among the icons shown: before the first icon of the
        // nearest row whose centre lies right of the point, else after that row's last icon.
        public int SlotAt(Point p)
        {
            var tiles = _cells.Children.OfType<Tile>().ToList();
            if (tiles.Count == 0) return 0;
            var centres = tiles.Select(t => t.TranslatePoint(new Point(t.ActualWidth / 2, t.ActualHeight / 2), this)).ToList();
            double rowY = centres.Select(c => c.Y).OrderBy(y => Math.Abs(y - p.Y)).First();
            int lastInRow = -1;
            for (int i = 0; i < tiles.Count; i++)
            {
                if (Math.Abs(centres[i].Y - rowY) > 1) continue;
                if (centres[i].X > p.X) return i;
                lastInRow = i;
            }
            return lastInRow + 1;
        }

        // The item a slot inserts before (null: after the last member); the dragged item itself does not count.
        public QuickItem? ItemAt(int slot, QuickItem dragged)
        {
            for (int i = Math.Max(0, slot); i < _items.Count; i++)
                if (_items[i] != dragged) return _items[i];
            return null;
        }

        // Where the insertion bar for a slot goes, in the overlay's coordinates: the left edge of the icon it
        // displaces, or the right edge of the one it follows.
        public Rect? SlotBar(int slot, Visual overlay)
        {
            var tiles = _cells.Children.OfType<Tile>().ToList();
            if (tiles.Count == 0 || !IsVisible) return null;
            bool after = slot >= tiles.Count;
            var t = tiles[Math.Clamp(slot, 0, tiles.Count - 1)];
            var at = t.TranslatePoint(new Point(after ? t.ActualWidth : 0, 6), (UIElement)overlay);
            return new Rect(at.X, at.Y, 3, Math.Max(12, t.ActualHeight - 12));
        }

        private void ShowGrip(bool on) =>
            _grip.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(120)));

        private void BeginResize()
        {
            _resizing = true;
            _gripAt = Mouse.GetPosition(_owner._board);
            (_startCols, _startRows) = (_wantCols, _wantRows);
            ShowSize();
            _badge.Visibility = Visibility.Visible;
        }

        // The size follows the pointer by whole cells from where the drag started. Board coordinates,
        // not the thumb's own deltas: the card may move to another spot as it grows.
        private void TrackResize()
        {
            if (!_resizing) return;
            var d = Mouse.GetPosition(_owner._board) - _gripAt;
            double colStep = Math.Max(1, _owner._board.Track + _owner.ColumnGap);
            int cols = Math.Clamp(_startCols + (int)Math.Round(d.X / colStep), 1, Math.Min(QuickGroup.MaxCols, Math.Max(1, _owner._board.Columns)));
            int rows = Math.Clamp(_startRows + (int)Math.Round(d.Y / _owner.RowHeight), 1, QuickGroup.MaxRows);
            if (cols == _wantCols && rows == _wantRows) return;
            Resize(cols, rows);
            ShowSize();
        }

        private void Resize(int cols, int rows)
        {
            (_wantCols, _wantRows) = (cols, rows);
            CellGridPanel.SetColSpan(this, cols);
            Apply(ShapeFor(NarrowWidth));
        }

        private void ShowSize()
        {
            var m = _owner._m;
            int shown = _wantCols >= 2 ? m.ColsFor(_wantCols) * m.RowsFor(_wantRows) : MiniRows(_wantRows + 1);
            _badgeText.Text = m.Standard ? $"{_wantCols} 列 × {_wantRows} 行" : $"{_wantCols} 列 × {_wantRows} 行 · {shown} 个";
        }

        private void EndResize(bool commit)
        {
            if (!_resizing) return;
            _resizing = false;
            _badge.Visibility = Visibility.Collapsed;
            if (!IsMouseOver) ShowGrip(false);
            bool changed = _wantCols != _startCols || _wantRows != _startRows;
            if (!changed) return;
            if (commit) _owner._actions.SetSize(Group, _wantCols, _wantRows);
            else Resize(_startCols, _startRows);
        }

        // Small icon rows that fit a card covering this many grid rows.
        private int MiniRows(int span) =>
            Math.Max(1, (int)Math.Floor((span * _owner.RowHeight - LabelStrip - CardChrome + 0.5) / _owner._m.MiniCell));

        private Shape ShapeFor(double width)
        {
            var m = _owner._m;
            double inner = Math.Max(0, width - 2 - 2 * m.InsetX);
            // Cells the card covers now (fewer than chosen in a narrow panel), then the icons they hold.
            int cells = Math.Min(_wantCols, Math.Max(1, (int)Math.Floor((width + m.Group + 0.5) / (m.CellWidth + m.Group))));
            int cols = cells >= 2 ? Math.Min(m.ColsFor(cells), (int)Math.Floor((inner + 0.5) / (m.Icon + 4))) : 1;
            if (_wantCols >= 2 && cols >= 2)
            {
                // Icon rows the chosen rows hold; unfolded, the card grows by whole grid rows (rows + 1 grid rows:
                // the extra one holds the border and the name). Rows keep the exact spacing, not the cells' height.
                int rows = m.RowsFor(_wantRows), drawn = rows, span = _wantRows + 1;
                if (Unfolded(cols * rows))
                    while (drawn * cols < _items.Count) drawn = m.RowsFor(++span - 1);
                var fit = m.CardRows(drawn, span * m.CellHeight - LabelStrip);
                // A border distance the cells cannot hold: one more grid row, rather than a distance other than the one set.
                while (!fit.Fits && span < _wantRows + 1 + drawn + 4) fit = m.CardRows(drawn, ++span * m.CellHeight - LabelStrip);
                return new Shape(false, cols, rows, drawn, span, Math.Floor(inner / cols * 100) / 100, fit.RowH, fit.Inset, fit.Face);
            }
            double miniInner = Math.Max(0, width - CardChrome);
            int miniCols = Math.Max(1, (int)Math.Floor((miniInner + 0.5) / m.MiniCell));
            int miniSpan = _wantRows + 1;
            if (Unfolded(miniCols * MiniRows(miniSpan)))
                while (miniCols * MiniRows(miniSpan) < _items.Count) miniSpan++;
            return new Shape(true, miniCols, MiniRows(_wantRows + 1), MiniRows(miniSpan), miniSpan);
        }

        private void Apply(Shape shape)
        {
            bool refill = !shape.SameCells(_shape) || _cells.Children.Count == 0;
            _shape = shape;
            if (CellGridPanel.GetRowSpan(this) != shape.Span) CellGridPanel.SetRowSpan(this, shape.Span);
            _cells.Columns = shape.Cols;
            _cells.Rows = shape.DrawnRows;
            var m = _owner._m;
            _face.Height = shape.Mini ? Math.Max(0, shape.Span * m.CellHeight - LabelStrip) : shape.Face;
            // A border closer than half a gap pulls the icons outwards (negative margin).
            _card.Padding = new Thickness(shape.Mini ? 8 : 0);
            _cells.Margin = shape.Mini ? new Thickness(0) : new Thickness(m.InsetX, shape.Inset, m.InsetX, shape.Inset);
            if (refill) Fill();
            else if (shape.Slot != _slot || shape.RowH != _rowH) FitTiles();
        }

        // Full tiles take the slot width (never wider than a loose tile) and the row height.
        private void FitTiles()
        {
            (_slot, _rowH) = (_shape.Slot, _shape.RowH);
            if (_shape.Mini) return;
            double w = Math.Min(_owner._m.TileWidth, _shape.Slot);
            foreach (var t in _cells.Children.OfType<Tile>()) t.FitCard(w, _shape.RowH);
        }

        public bool IsLabel(object source)
        {
            if (IsControl(source)) return false;
            for (var d = source as DependencyObject; d is not null && d != this; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d == _labelHost) return true;
            return false;
        }

        // Unfolding holds the card at its cell (the store pins it there as the newest pin); folding keeps the pin.
        public void Toggle()
        {
            bool expand = !Group.Expanded;
            var at = _owner._board.CellOf(this);
            _owner._actions.SetExpanded(Group, expand, at);
            if (Group.Col is int c && Group.Row is int r) CellGridPanel.SetPin(this, c, r, Group.PinnedAt);
            _peek = false;
            Apply(ShapeFor(ActualWidth > 0 ? ActualWidth : NarrowWidth));
            // The log says whether the card held its cell (a mismatch here is the bug users see as "it jumped").
            Dispatcher.BeginInvoke(new Action(() =>
                Core.Log.Write($"quick card \"{Group.Name}\" {(expand ? "unfolded" : "folded")}: cell {Cell(at)} -> {Cell(_owner._board.CellOf(this))}, rows {CellGridPanel.GetRowSpan(this)}")),
                DispatcherPriority.Loaded);
        }

        private static string Cell((int Col, int Row)? c) => c is { } v ? $"{v.Col},{v.Row}" : "-";

        // Tiles are reused by the next build, so they must leave this card's panel first.
        public void Detach() => _cells.Children.Clear();

        public void SetDropTarget(bool on)
        {
            if (on) _card.SetResourceReference(Border.BorderBrushProperty, "Accent1Brush");
            else _card.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        }

        // Folded: exactly columns × rows icons, the count of the rest on the pill beside the name.
        private void Fill()
        {
            _cells.Children.Clear();
            string view = _shape.Mini ? "Mini" : "Card";
            int capacity = _shape.Cols * _shape.Rows;
            bool overflow = _items.Count > capacity;
            bool open = (Group.Expanded || _peek) && !_resizing;
            int shown = !overflow || open ? _items.Count : capacity;
            for (int i = 0; i < shown; i++) _cells.Children.Add(_owner.BoardTile(_items[i], view));
            FitTiles();
            _more.Visibility = overflow ? Visibility.Visible : Visibility.Collapsed;
            if (!overflow) return;
            bool folded = !open;
            _more.Content = folded ? $"+{_items.Count - shown}" : null;
            _more.Tag = folded ? null : Application.Current.TryFindResource("Icon.Up");
            _more.ToolTip = folded ? "查看全部" : "收起";
            System.Windows.Automation.AutomationProperties.SetName(_more, $"{(folded ? "查看全部" : "收起")} {Group.Name}");
        }

        public void BeginRename()
        {
            if (_editor is not null) return;
            var box = new TextBox { Text = Group.Name, MinWidth = Math.Min(120, Math.Max(60, ActualWidth)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
            box.SetResourceReference(StyleProperty, "Field");
            _editor = box;
            _label.Visibility = Visibility.Hidden;
            _labelHost.Children.Add(box);
            bool done = false;
            void Finish(bool save)
            {
                if (done) return;
                done = true;
                string text = box.Text.Trim();
                _labelHost.Children.Remove(box);
                _label.Visibility = Visibility.Visible;
                _editor = null;
                if (save && text.Length > 0 && text != Group.Name) _owner._actions.RenameGroup(Group, text);
            }
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { Finish(true); e.Handled = true; }
                else if (e.Key == Key.Escape) { Finish(false); e.Handled = true; }
            };
            box.LostKeyboardFocus += (_, _) => Finish(true);
            Dispatcher.BeginInvoke(new Action(() => { box.Focus(); box.SelectAll(); }), DispatcherPriority.Input);
        }
    }
}
