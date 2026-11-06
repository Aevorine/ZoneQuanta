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
// so loose icons and small cards fill gaps that larger cards leave.
// Drag an item onto a card to move it there, onto an ungrouped item to make a new group of the
// two, or onto empty space to take it out of its group. Drag a card anywhere: it follows the pointer
// cell by cell while the others make room, and stays pinned where it is dropped.
public sealed partial class QuickGrid
{
    // Card chrome around the icon grid: padding 8 + border 1 on each side.
    private const double CardChrome = 18;
    // Room under each card for its name (and the space before the next row of cards).
    private const double LabelStrip = 34;
    // A one-column card shows a single column of smaller icons in cells of this size.
    private const double MiniCell = 40;
    private const double ColumnGap = 14;
    private readonly ScrollViewer _boardScroll = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Focusable = false,
        Background = Brushes.Transparent,
        AllowDrop = true,
    };
    private readonly CellGridPanel _board = new() { Background = Brushes.Transparent, Margin = new Thickness(2, 2, 2, 4), Gap = ColumnGap };
    private readonly Dictionary<QuickItem, Tile> _boardTiles = new();
    private readonly List<GroupCard> _cards = new();
    private GroupCard? _downCard, _dropCard, _lifted;
    private Point _grab;
    private (int Col, int Row)? _liftTarget;
    private int _liftRows;
    private Tile? _focused;

    // Card fill: the launcher window sits on SurfaceBrush, the panel page on CardBrush.
    public string GroupBackground { get; set; } = "CardBrush";

    // One icon in a card (the "Card" tile): its width, and its height with or without the name line.
    private double CellWidth => _actions.Settings.QuickShowName ? 80 : 64;
    private double TileHeight => _actions.Settings.QuickShowName ? 80 : 58;
    // A grid row is one row of icons. A card with n icon rows covers n + 1 grid rows: the extra one holds
    // its chrome and name (CardChrome + LabelStrip < TileHeight), so icon rows stay tight at any height
    // and cards of different heights still line up on whole rows.
    private double RowHeight => TileHeight;

    // Columns × rows of a group card: its own size, else the default, else by item count.
    private (int Cols, int Rows) CardSize(QuickGroup group, int count)
    {
        if (group.Cols > 0 && group.Rows > 0) return (group.Cols, group.Rows);
        var s = _actions.Settings;
        if (s.QuickCardCols > 0 && s.QuickCardRows > 0) return (s.QuickCardCols, s.QuickCardRows);
        return count <= 2 ? (2, 1) : count <= 4 ? (count, 1) : (4, 2);
    }

    private void InitBoard()
    {
        _boardScroll.Content = _board;
        _boardScroll.PreviewMouseLeftButtonDown += OnBoardDown;
        _boardScroll.PreviewMouseMove += OnBoardMove;
        _boardScroll.PreviewMouseLeftButtonUp += OnBoardUp;
        _boardScroll.PreviewKeyDown += OnBoardKey;
        _boardScroll.DragOver += OnBoardDragOver;
        _boardScroll.DragLeave += (_, _) => MarkBoardDrop(null, null);
        _boardScroll.Drop += OnBoardDrop;
    }

    private void BuildBoard()
    {
        ClearBoard();
        _board.CellWidth = CellWidth + 4;
        _board.CellHeight = RowHeight;
        var h = _actions.Highlight;
        foreach (var (group, items) in _actions.Sections())
        {
            if (group is null)
            {
                // A loose icon takes one cell, so it can fill a gap beside or under a card.
                foreach (var item in items) _board.Children.Add(BoardTile(item, "Card"));
                continue;
            }
            var card = new GroupCard(this, group, items, peek: h is not null && items.Contains(h));
            if (group.Col is int col && group.Row is int row) CellGridPanel.SetPin(card, col, row, group.PinnedAt);
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
        foreach (var c in _cards) c.Detach();
        _cards.Clear();
        _board.Children.Clear();
        _dropCard = null;
        _dropTile = null;
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
        tile.Margin = view == "Grid" ? new Thickness(4, 6, 4, 6) : new Thickness(0);
        tile.VerticalAlignment = VerticalAlignment.Top;
        tile.Layout(view);
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
        _downCard = tile is null && card is not null && !card.Renaming && !card.IsGrip(e.OriginalSource) ? card : null;
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
        _downTile = null;
        _downCard = null;
        if (click && _singleClick) _actions.Open(tile!.Item);
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

    private bool CanPlace => _actions.Settings.QuickSort == "Custom";

    private void OnBoardDragOver(object sender, DragEventArgs e)
    {
        EdgeScroll(e);
        var tile = TileAt(e.OriginalSource);
        var card = CardAt(e.OriginalSource);
        if (e.Data.GetDataPresent(GroupDragFormat))
        {
            e.Effects = _lifted is not null ? DragDropEffects.Move : DragDropEffects.None;
            if (_lifted is not null) Follow(e);
            MarkBoardDrop(null, null);
        }
        else if (e.Data.GetDataPresent(DragFormat) || e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Link;
            MarkBoardDrop(card, card is null ? tile : null);
        }
        else
        {
            e.Effects = DragDropEffects.None;
            MarkBoardDrop(null, null);
        }
        e.Handled = true;
    }

    private void OnBoardDrop(object sender, DragEventArgs e)
    {
        var tile = TileAt(e.OriginalSource);
        var card = CardAt(e.OriginalSource);
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
        if (e.Data.GetData(DragFormat) is string path)
        {
            if (_actions.Store.Find(path) is not { } item) return;
            if (card is not null)
            {
                if (!string.Equals(item.Group, card.Group.Name, StringComparison.OrdinalIgnoreCase)) _actions.MoveToGroup(item, card.Group.Name);
                if (tile is not null && tile.Item != item && CanPlace) _actions.Move(item, IndexOf(tile.Item));
            }
            else if (tile is not null && tile.Item != item)
            {
                _actions.NewGroup(new[] { tile.Item, item });
            }
            else if (tile is null && item.Group.Length > 0)
            {
                _actions.Ungroup(item);
            }
            return;
        }
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            _actions.Add(files, -1, card?.Group.Name);
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
        var menu = card.ContextMenu!;
        menu.Items.Clear();
        Add(menu, "撤销", "Icon.Reset", "Ctrl+Z", () => _actions.Undo(), _actions.Store.CanUndo);
        Add(menu, "重做", "Icon.Right", "Ctrl+Y", () => _actions.Redo(), _actions.Store.CanRedo);
        menu.Items.Add(new Separator());
        Add(menu, "重命名分组", "Icon.Edit", "", card.BeginRename, true);
        var group = card.Group;
        menu.Items.Add(CardSizeMenu(menu, group.Cols, group.Rows, "跟随默认", (c, r) => _actions.SetSize(group, c, r)));
        if (card.CanExpand)
            Add(menu, group.Expanded ? "收起" : "展开全部", group.Expanded ? "Icon.Up" : "Icon.Down", "", card.Toggle, true);
        Add(menu, "按内容命名", "Icon.Bolt", "", () => _actions.NameByContent(group), true);
        if (group.Col is not null) Add(menu, "取消固定位置", "Icon.Pin", "", () => _actions.Unpin(group), true);
        if (_actions.Store.Groups.Any(g => g.Col is not null)) Add(menu, "全部自动排列", "Icon.Groups", "", () => _actions.Unpin(null), true);
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

    // One group: a rounded card covering its columns × rows of grid cells, with the name underneath.
    // It shows columns × rows icons (small icons in a one-column card, as many as fit); the last cell
    // expands the rest in place, which is remembered per group.
    private sealed class GroupCard : DockPanel
    {
        private readonly QuickGrid _owner;
        private readonly UniformWrapPanel _cells = new();
        private readonly Border _card;
        private readonly Grid _labelHost = new() { Height = LabelStrip };
        private readonly TextBlock _label;
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
        // while folded, icon rows drawn now, and the grid rows it covers.
        private readonly record struct Shape(bool Mini, int Cols, int Rows, int DrawnRows, int Span);

        public GroupCard(QuickGrid owner, QuickGroup group, List<QuickItem> items, bool peek)
        {
            _owner = owner;
            _items = items;
            _peek = peek;
            Group = group;
            Background = Brushes.Transparent;
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
                Margin = new Thickness(4, 7, 4, 0),
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                ToolTip = $"{group.Name} · {items.Count}",
            };
            _label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            _labelHost.Children.Add(_label);
            SetDock(_labelHost, Dock.Bottom);
            Children.Add(_labelHost);

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
            var face = new Grid();
            face.Children.Add(_card);
            face.Children.Add(_badge);
            face.Children.Add(_grip);
            Children.Add(face);
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
        public bool Renaming => _editor is not null;
        public bool CanExpand => _items.Count > _shape.Cols * _shape.Rows;

        private double NarrowWidth => _wantCols * (_owner.CellWidth + 4) + (_wantCols - 1) * ColumnGap;

        protected override Size MeasureOverride(Size constraint)
        {
            if (!double.IsInfinity(constraint.Width))
            {
                var shape = ShapeFor(constraint.Width);
                if (shape != _shape) Apply(shape);
            }
            return base.MeasureOverride(constraint);
        }

        // A card being resized previews its folded size.
        private bool Unfolded(int capacity) => !_resizing && (Group.Expanded || _peek) && _items.Count > capacity;

        public bool IsGrip(object source)
        {
            for (var d = source as DependencyObject; d is not null && d != this; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d == _grip) return true;
            return false;
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
            double colStep = Math.Max(1, _owner._board.Track + ColumnGap);
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

        private void ShowSize() => _badgeText.Text = $"{_wantCols} 列 × {_wantRows} 行";

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
            Math.Max(1, (int)Math.Floor((span * _owner.RowHeight - LabelStrip - CardChrome + 0.5) / MiniCell));

        private Shape ShapeFor(double width)
        {
            double inner = Math.Max(0, width - CardChrome);
            int cols = Math.Min(_wantCols, (int)Math.Floor((inner + 0.5) / _owner.CellWidth));
            if (_wantCols >= 2 && cols >= 2)
            {
                // Full tiles in the chosen rows; unfolded, the card grows by whole rows.
                int rows = _wantRows;
                if (Unfolded(cols * _wantRows)) rows = Math.Max(rows, (int)Math.Ceiling((_items.Count + 1) / (double)cols));
                return new Shape(false, cols, _wantRows, rows, rows + 1);
            }
            int miniCols = Math.Max(1, (int)Math.Floor((inner + 0.5) / MiniCell));
            int span = _wantRows + 1;
            if (Unfolded(miniCols * MiniRows(span)))
                while (miniCols * MiniRows(span) < _items.Count + 1) span++;
            return new Shape(true, miniCols, MiniRows(_wantRows + 1), MiniRows(span), span);
        }

        private void Apply(Shape shape)
        {
            _shape = shape;
            if (CellGridPanel.GetRowSpan(this) != shape.Span) CellGridPanel.SetRowSpan(this, shape.Span);
            _cells.Columns = shape.Cols;
            _cells.Rows = shape.DrawnRows;
            Fill();
        }

        public bool IsLabel(object source)
        {
            for (var d = source as DependencyObject; d is not null && d != this; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d == _labelHost) return true;
            return false;
        }

        public void Toggle()
        {
            _owner._actions.SetExpanded(Group, !Group.Expanded);
            _peek = false;
            Apply(ShapeFor(ActualWidth > 0 ? ActualWidth : NarrowWidth));
        }

        // Tiles are reused by the next build, so they must leave this card's panel first.
        public void Detach() => _cells.Children.Clear();

        public void SetDropTarget(bool on)
        {
            if (on) _card.SetResourceReference(Border.BorderBrushProperty, "Accent1Brush");
            else _card.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        }

        private void Fill()
        {
            _cells.Children.Clear();
            string view = _shape.Mini ? "Mini" : "Card";
            int capacity = _shape.Cols * _shape.Rows;
            bool overflow = _items.Count > capacity;
            bool open = Group.Expanded || _peek;
            int shown = !overflow || open ? _items.Count : capacity - 1;
            for (int i = 0; i < shown; i++) _cells.Children.Add(_owner.BoardTile(_items[i], view));
            if (overflow) _cells.Children.Add(MoreCell(open ? 0 : _items.Count - shown));
        }

        // "+N" while folded, an up arrow once unfolded.
        private FrameworkElement MoreCell(int more)
        {
            bool mini = _shape.Mini;
            double faceSize = mini ? MiniCell - 8 : 42;
            var face = new Border { Width = faceSize, Height = faceSize, CornerRadius = new CornerRadius(mini ? 8 : 12), HorizontalAlignment = HorizontalAlignment.Center };
            face.SetResourceReference(Border.BackgroundProperty, "RaisedBrush");
            if (more > 0)
            {
                var t = new TextBlock { Text = $"+{more}", FontSize = mini ? 11 : 14, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                t.SetResourceReference(TextBlock.ForegroundProperty, "Accent1Brush");
                face.Child = t;
            }
            else
            {
                var p = new Shapes.Path { Data = (Geometry)Application.Current.FindResource("Icon.Up"), Width = mini ? 12 : 16, Height = mini ? 12 : 16 };
                p.SetResourceReference(StyleProperty, "IconPath");
                p.SetResourceReference(Shapes.Shape.StrokeProperty, "Accent1Brush");
                face.Child = p;
            }
            var cell = new Border
            {
                Width = mini ? MiniCell : _owner.CellWidth,
                Padding = mini ? new Thickness(4) : new Thickness(4, 8, 4, 6),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top,
                ToolTip = more > 0 ? "展开全部" : "收起",
                Child = face,
            };
            cell.MouseLeftButtonUp += (_, e) =>
            {
                Toggle();
                e.Handled = true;
            };
            return cell;
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
