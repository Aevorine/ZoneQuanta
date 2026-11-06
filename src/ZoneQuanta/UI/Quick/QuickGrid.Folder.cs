using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using ZoneQuanta.Core.Quick;
using ZoneQuanta.UI.Controls;
using Win32 = Microsoft.Win32;

namespace ZoneQuanta.UI.Quick;

// Folder view: every member of a group in a panel over the board, grown out of the card. The board underneath
// never moves. Search inside it, step to the previous / next group, right-click for the group's menu, add files
// or folders to the group; open, rename, reorder by dragging inside, drag out over the dimmed board to drop
// anywhere on it (the view gets out of the way), drop files on it to add them to the group. Its size and spacing are
// its own (FolderMetrics), not the board's: drag an edge or corner to resize it (remembered), double-click one for
// the automatic size again, or pick a size / spacing from its menu.
// Its tiles are cached across openings (icons stay loaded), so every way a view closes ends in Release, which
// hands them back; a view still fading out finishes at once when another one needs them.
public sealed partial class QuickGrid
{
    private readonly Grid _boardRoot = new();
    private readonly Dictionary<QuickItem, Tile> _folderTiles = new();
    private Folder? _folder;
    private Folder? _fading;
    private static bool _adoptLogged;
    private bool _eatRightUp;

    // A cached tile is shown in one place at a time. One still held by a panel it no longer belongs to (a view
    // that did not hand it back, a card the last build dropped) leaves that panel first, instead of WPF refusing
    // it a second parent. Logged once: it means some path forgot to hand its tiles back.
    private static void Adopt(FrameworkElement element, string where)
    {
        if (element.Parent is not System.Windows.Controls.Panel stale) return;
        stale.Children.Remove(element);
        if (_adoptLogged) return;
        _adoptLogged = true;
        Core.Log.Write($"quick tile taken back from a stale parent ({where})");
    }

    // Esc: the folder's search first, then the folder (the window's own Esc comes after).
    public bool CloseFolderIfOpen()
    {
        if (_folder is not { } f) return false;
        if (!f.ClearSearch()) CloseFolder(animate: true, "esc");
        return true;
    }

    // Back (a three-finger swipe down): the folder view closes at once, its search with it.
    public bool CloseFolderNow()
    {
        if (_folder is null) return false;
        CloseFolder(animate: true, "swipe");
        return true;
    }

    // A press anywhere in the window outside the folder view closes it; what was pressed still gets the press
    // (the search field, a toolbar button, the window's move). Menus and other pop-ups are not the window.
    public void CloseFolderOnPressOutside(MouseButtonEventArgs e)
    {
        if (_folder is not { } f || e.OriginalSource is not DependencyObject src) return;
        var visual = src as Visual ?? WindowDrag.Ancestor<Visual>(src);
        if (visual is null || PresentationSource.FromVisual(visual) != PresentationSource.FromVisual(this)) return;
        if (f.IsAncestorOf(visual)) return;
        CloseFolder(animate: true, "outside press");
    }

    private void OpenFolder(GroupCard card)
    {
        CloseFolder(animate: false);
        FinishFading();
        Folder? f = null;
        try
        {
            f = new Folder(this, card);
            _boardRoot.Children.Add(f);
            _folder = f;
            Core.Log.Write($"quick folder \"{card.Group.Name}\" opened: {card.Items.Count} items");
        }
        catch (Exception ex)
        {
            // A view must never take the app down: drop what was built and unfold the card in place,
            // the other way to see every member.
            Core.Log.Error($"quick folder \"{card.Group.Name}\" failed, unfolding in place", ex);
            _folder = null;
            if (f is not null)
            {
                f.Release();
                _boardRoot.Children.Remove(f);
            }
            if (!card.Group.Expanded && card.Parent is not null) card.Toggle();
        }
    }

    // Also finishes a view still fading out, so any caller that closes (a layout switch too) frees every tile.
    // why: logged for closes the user asked for (the log then tells which way a view was left).
    private void CloseFolder(bool animate, string? why = null)
    {
        FinishFading();
        if (_folder is not { } f) return;
        _folder = null;
        if (why is not null) Core.Log.Write($"quick folder \"{f.Group.Name}\" closed: {why}");
        if (animate) _fading = f;
        f.Close(animate, () =>
        {
            if (_fading == f) _fading = null;
            f.Release();
            _boardRoot.Children.Remove(f);
        });
    }

    private void FinishFading() => _fading?.FinishClose();

    // After a rebuild: the open folder follows its group (renamed, reordered, members gone), or closes.
    private void RefreshFolder()
    {
        if (_folder is not { } f) return;
        var card = CardOf(f.Group);
        if (card is null || card.Items.Count == 0)
        {
            CloseFolder(animate: false);
            return;
        }
        try { f.Fill(card); }
        catch (Exception ex)
        {
            Core.Log.Error($"quick folder \"{card.Group.Name}\" refresh", ex);
            CloseFolder(animate: false);
        }
    }

    private GroupCard? CardOf(QuickGroup group) =>
        _cards.FirstOrDefault(c => c.Group == group)
        ?? _cards.FirstOrDefault(c => string.Equals(c.Group.Name, group.Name, StringComparison.OrdinalIgnoreCase));

    // The previous / next group in board order, round the ends.
    private GroupCard? Neighbour(QuickGroup group, int delta)
    {
        if (_cards.Count < 2) return null;
        int i = Math.Max(0, _cards.FindIndex(c => c.Group == group));
        return _cards[((i + delta) % _cards.Count + _cards.Count) % _cards.Count];
    }

    private Tile FolderTile(QuickItem item)
    {
        if (!_folderTiles.TryGetValue(item, out var tile))
        {
            tile = new Tile(item, _actions.Settings);
            tile.EnableHover();
            tile.ContextMenu = new ContextMenu();
            tile.ContextMenuOpening += OnMenu;
            _folderTiles[item] = tile;
        }
        Adopt(tile, "folder");
        tile.Margin = new Thickness(0);
        tile.VerticalAlignment = VerticalAlignment.Center;
        tile.Layout("Card", _m);
        return tile;
    }

    private sealed class Folder : Grid
    {
        private static readonly Duration OpenTime = TimeSpan.FromMilliseconds(200), CloseTime = TimeSpan.FromMilliseconds(150), SlideTime = TimeSpan.FromMilliseconds(190);
        private const double SearchMin = 110, SearchMax = 220, TitleMin = 96;
        private readonly QuickGrid _owner;
        private readonly Border _scrim = new() { Opacity = 0 };
        private readonly Border _panel = new() { CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        private readonly UniformWrapPanel _cells = new() { VerticalAlignment = VerticalAlignment.Top };
        private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, AllowDrop = true };
        private readonly TextBlock _title = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _count = new() { FontSize = 13, Margin = new Thickness(8, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        private readonly Grid _titleHost = new() { Background = Brushes.Transparent, Cursor = Cursors.IBeam, MinWidth = TitleMin };
        private readonly TextBox _search = new() { FontSize = 13, Width = SearchMax, Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        private readonly Grid _header = new() { Margin = new Thickness(18, 10, 8, 4) };
        private readonly Border _sizeBadge = new()
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 4, 10, 4), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 14),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed, IsHitTestVisible = false,
        };
        private readonly TextBlock _sizeText = new() { FontSize = 12 };
        private bool _resizing;
        private Point _resizeFrom;
        private Size _resizeStart;
        private static ControlTemplate? _bare;
        private readonly Button _prev, _next, _unfold;
        private readonly Border _slotBar = new() { Width = 3, CornerRadius = new CornerRadius(1.5), Visibility = Visibility.Collapsed, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        private readonly ScaleTransform _zoom = new(1, 1);
        private readonly TranslateTransform _shift = new(), _slide = new();
        private readonly DispatcherTimer _outside = new() { Interval = TimeSpan.FromMilliseconds(260) };
        private GroupCard _card;
        // Every member in the group's order, and the ones shown (the search's matches, best first).
        private List<QuickItem> _all = new(), _items = new();
        private Tile? _down;
        private Point _downAt;
        private Point? _blankAt;
        private int _slot = -1;
        private bool _closing, _quiet, _hadFocus;
        private Action? _done;
        private TextBox? _editor;
        private Action<bool>? _finishRename;
        private List<QuickItem>? _pop;

        public Folder(QuickGrid owner, GroupCard card)
        {
            _owner = owner;
            _card = card;
            Group = card.Group;
            Focusable = false;

            _scrim.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            _scrim.AllowDrop = true;
            _scrim.MouseDown += (_, e) =>
            {
                // A right press only dismisses: the card under it must not open its menu on the release.
                if (e.ChangedButton == MouseButton.Right) _owner._eatRightUp = true;
                _owner.CloseFolder(animate: true, "dimmed board");
                e.Handled = true;
            };
            _scrim.DragOver += OnScrimDrag;
            _scrim.DragLeave += (_, _) => _outside.Stop();
            _scrim.Drop += (_, e) => e.Handled = true;
            Children.Add(_scrim);

            _panel.SetResourceReference(Border.BackgroundProperty, owner.GroupBackground);
            _panel.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
            _panel.Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Opacity = 0.35, Direction = 270, Color = Colors.Black };
            _panel.RenderTransformOrigin = new Point(0.5, 0.5);
            _panel.RenderTransform = new TransformGroup { Children = { _zoom, _shift } };
            _panel.MouseLeftButtonDown += (_, e) => e.Handled = true;
            // A click (not a drag) on the view's own empty space closes it too.
            _panel.PreviewMouseLeftButtonDown += (_, e) => _blankAt = e.ClickCount == 1 && Blank(e) ? e.GetPosition(this) : null;
            _panel.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (_blankAt is not { } at) return;
                _blankAt = null;
                var d = e.GetPosition(this) - at;
                if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance && Blank(e))
                    _owner.CloseFolder(animate: true, "empty space");
            };
            // Right-click on the header or between the icons: the group's own menu (a tile keeps its item menu).
            _panel.ContextMenu = new ContextMenu();
            _panel.ContextMenuOpening += (_, e) =>
            {
                if (TileAt(e.OriginalSource) is not null || Within<TextBox>(e.OriginalSource)) return;
                _owner.FillGroupMenu(_panel.ContextMenu, _card, BeginRename, inFolder: true);
                AddViewMenu(_panel.ContextMenu);
            };

            _title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            _count.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleRow.Children.Add(_title);
            titleRow.Children.Add(_count);
            _titleHost.Children.Add(titleRow);
            _titleHost.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2) BeginRename();
                e.Handled = true;
            };
            _titleHost.ToolTip = "重命名";

            _search.SetResourceReference(StyleProperty, "IconField");
            _search.Tag = Application.Current.TryFindResource("Icon.Search");
            System.Windows.Automation.AutomationProperties.SetName(_search, "搜索本组");
            _search.TextChanged += (_, _) => { if (!_quiet) Show(); };
            _search.PreviewKeyDown += OnSearchKey;

            _prev = HeaderButton("Icon.Left", "上一个分组", () => Step(-1));
            _next = HeaderButton("Icon.Right", "下一个分组", () => Step(1));
            Button? add = null;
            add = HeaderButton("Icon.Plus", "添加到本组", () => OpenAddMenu(add!));
            _unfold = HeaderButton("Icon.Down", "原地展开", () =>
            {
                var c = _card;
                _owner.CloseFolder(animate: false);
                if (!c.Group.Expanded) c.Toggle();
            });
            foreach (var b in new[] { _prev, _next, add, _unfold, HeaderButton("Icon.Close", "关闭", () => _owner.CloseFolder(animate: true, "close button")) })
                _buttons.Children.Add(b);

            var header = _header;
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(_titleHost);
            Grid.SetColumn(_search, 1);
            header.Children.Add(_search);
            Grid.SetColumn(_buttons, 2);
            header.Children.Add(_buttons);
            DockPanel.SetDock(header, Dock.Top);
            _scroll.Margin = new Thickness(0, 0, FolderMetrics.ScrollRight, FolderMetrics.ScrollBottom);

            var body = new Grid();
            body.Children.Add(_cells);
            body.Children.Add(_slotBar);
            _slotBar.SetResourceReference(Border.BackgroundProperty, "Accent1Brush");
            _scroll.Content = body;
            _scroll.RenderTransform = _slide;
            _scroll.PreviewMouseLeftButtonDown += OnDown;
            _scroll.PreviewMouseMove += OnMove;
            _scroll.PreviewMouseLeftButtonUp += OnUp;
            _scroll.DragOver += OnPanelDrag;
            _scroll.DragLeave += (_, _) => ShowSlot(-1);
            _scroll.Drop += OnPanelDrop;

            var dock = new DockPanel { ClipToBounds = true };
            dock.Children.Add(header);
            dock.Children.Add(_scroll);
            var frame = new Grid();
            frame.Children.Add(dock);
            _sizeBadge.SetResourceReference(Border.BackgroundProperty, "RaisedBrush");
            _sizeBadge.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
            _sizeText.SetResourceReference(TextBlock.ForegroundProperty, "Accent1Brush");
            System.Windows.Documents.Typography.SetNumeralAlignment(_sizeText, FontNumeralAlignment.Tabular);
            _sizeBadge.Child = _sizeText;
            frame.Children.Add(_sizeBadge);
            AddResizeHandles(frame);
            _panel.Child = frame;
            Children.Add(_panel);

            _outside.Tick += (_, _) =>
            {
                // The pointer left the view while dragging: get out of the way so the board takes the drop.
                _outside.Stop();
                _owner.CloseFolder(animate: false);
            };
            PreviewKeyDown += OnKey;
            PreviewTextInput += OnType;
            // The mouse's back button closes, as it leaves a page.
            PreviewMouseDown += (_, e) =>
            {
                if (e.ChangedButton != MouseButton.XButton1) return;
                _owner.CloseFolder(animate: true, "back button");
                e.Handled = true;
            };
            SizeChanged += (_, _) => Relayout();
            Fill(card);
            Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(Grow), DispatcherPriority.Loaded);
        }

        public QuickGroup Group { get; private set; }
        private bool Searching => _search.Text.Trim().Length > 0;

        private static Button HeaderButton(string icon, string tip, Action act)
        {
            var b = new Button { Tag = Application.Current.TryFindResource(icon), ToolTip = tip };
            b.SetResourceReference(StyleProperty, "IconButton");
            System.Windows.Automation.AutomationProperties.SetName(b, tip);
            b.Click += (_, e) => { act(); e.Handled = true; };
            return b;
        }

        private static bool Within<T>(object source) where T : DependencyObject
        {
            for (var d = source as DependencyObject; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d is T) return true;
            return false;
        }

        // Empty space of the view: not an icon, field, button, scroll bar, edge handle or the title, and not the
        // icons' block either (the gaps between icons belong to them, so a near miss never closes the view).
        private bool Blank(MouseEventArgs e)
        {
            for (var d = e.OriginalSource as DependencyObject; d is not null && d != _panel; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d is Tile or System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.Primitives.ButtonBase or Thumb or System.Windows.Controls.Primitives.ScrollBar || d == _titleHost)
                    return false;
            var tiles = _cells.Children.OfType<Tile>().Where(t => t.IsVisible).ToList();
            if (tiles.Count == 0) return true;
            var p = e.GetPosition(_cells);
            double slot = _cells.ActualWidth / Math.Max(1, _cells.Columns), row = _cells.ActualHeight / Math.Max(1, _cells.Rows);
            foreach (var t in tiles)
            {
                var r = new Rect(t.TranslatePoint(new Point(0, 0), _cells), new Size(t.ActualWidth, t.ActualHeight));
                r.Inflate(Math.Max(2, (slot - r.Width) / 2 + 2), Math.Max(2, (row - r.Height) / 2 + 2));
                if (r.Contains(p)) return false;
            }
            return true;
        }

        public void Fill(GroupCard card)
        {
            _card = card;
            Group = card.Group;
            _all = card.Items.ToList();
            if (_editor is null) _title.Text = Group.Name;
            bool many = _owner._cards.Count > 1;
            _prev.Visibility = _next.Visibility = many ? Visibility.Visible : Visibility.Collapsed;
            _unfold.Visibility = card.CanExpand && !Group.Expanded ? Visibility.Visible : Visibility.Collapsed;
            Show();
            if (_pop is { } pop)
            {
                // Items just added from this view light up here, not on the card underneath.
                _pop = null;
                var first = true;
                foreach (var t in _cells.Children.OfType<Tile>().Where(t => pop.Contains(t.Item)))
                {
                    if (first) t.BringIntoView();
                    first = false;
                    t.Pop();
                }
            }
        }

        // The members the search matches (all of them without one), best match first like the launcher's search.
        private void Show()
        {
            string q = _search.Text.Trim();
            _items = q.Length == 0 ? _all
                : _all.Select(i => (Item: i, Score: QuickActions.Match(q, i))).Where(x => x.Score >= 0)
                    .OrderByDescending(x => x.Score).Select(x => x.Item).ToList();
            _count.Text = q.Length == 0 ? N(_all.Count) : $"{N(_items.Count)} / {N(_all.Count)}";
            var keep = Keyboard.FocusedElement as Tile;
            _cells.Children.Clear();
            foreach (var item in _items) _cells.Children.Add(_owner.FolderTile(item));
            Relayout();
            if (keep is not null && _cells.Children.Contains(keep)) keep.Focus();
        }

        private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);

        // Hands every tile back so the next view (or a rebuild) can show them.
        public void Release()
        {
            _outside.Stop();
            _cells.Children.Clear();
        }

        public bool ClearSearch()
        {
            if (_search.Text.Length == 0) return false;
            bool inField = _search.IsKeyboardFocusWithin;
            _search.Clear();
            if (inField) FocusFirst();
            return true;
        }

        private void FocusFirst()
        {
            if (_cells.Children.OfType<Tile>().FirstOrDefault() is { } t)
            {
                t.Focus();
                t.BringIntoView();
            }
        }

        private FolderMetrics Metrics => FolderMetrics.From(_owner._actions.Settings, _owner._m.Icon);

        // The size mode's size (Auto: by every member, not the search's matches, so the panel holds still while
        // typing) inside the board area; rows beyond the height scroll. Not while an edge is being dragged.
        public void Relayout()
        {
            if (_resizing) return;
            var room = Room();
            if (room.Width <= 0 || room.Height <= 0) return;
            var size = Metrics.PanelSize(_owner._actions.Settings, _all.Count, _card.Columns, room, HeaderHeight(), Chrome() + TitleMin + SearchMin);
            ApplySize(size.Width, size.Height);
        }

        private Size Room()
        {
            double w = ActualWidth > 0 ? ActualWidth : _owner._boardRoot.ActualWidth;
            double h = ActualHeight > 0 ? ActualHeight : _owner._boardRoot.ActualHeight;
            return new Size(Math.Max(0, w - 2 * FolderMetrics.Margin), Math.Max(0, h - 2 * FolderMetrics.Margin));
        }

        private double HeaderHeight()
        {
            _header.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return _header.DesiredSize.Height;
        }

        // Header room besides the title and search: margins and the buttons.
        private double Chrome()
        {
            _buttons.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return 18 + 8 + 16 + _buttons.DesiredSize.Width;
        }

        // Columns fill the width (slots share it evenly), rows keep the density's pitch.
        private void ApplySize(double width, double height)
        {
            var fm = Metrics;
            var (cols, rows, scroll) = fm.Grid(_all.Count, width, FolderMetrics.ViewHeight(height, HeaderHeight()));
            _cells.Columns = cols;
            _cells.Rows = rows;
            _cells.ClearValue(WidthProperty);
            _cells.Height = rows * fm.Pitch;
            _cells.Margin = new Thickness(fm.PadX, fm.BlockPad, fm.PadX, 0);
            double tileW = fm.TileWidth(fm.Inner(width, scroll) / cols);
            foreach (var t in _cells.Children.OfType<Tile>()) t.FitCard(tileW, fm.Pitch);
            _panel.Width = width;
            _panel.Height = height;
            _search.Width = Math.Clamp(width - Chrome() - TitleMin - 40, SearchMin, SearchMax);
        }

        // A thumb that draws nothing: the panel's edges and corners.
        private static ControlTemplate Bare()
        {
            if (_bare is not null) return _bare;
            var fill = new FrameworkElementFactory(typeof(Border));
            fill.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            return _bare = new ControlTemplate(typeof(Thumb)) { VisualTree = fill };
        }

        // Edges and corners resize the view about its centre, so the dragged edge follows the pointer; the
        // bottom-right corner shows a grip. Double-click any of them for the automatic size.
        private void AddResizeHandles(Grid frame)
        {
            var handles = new (HorizontalAlignment H, VerticalAlignment V, int X, int Y)[]
            {
                (HorizontalAlignment.Left, VerticalAlignment.Stretch, -1, 0), (HorizontalAlignment.Right, VerticalAlignment.Stretch, 1, 0),
                (HorizontalAlignment.Stretch, VerticalAlignment.Top, 0, -1), (HorizontalAlignment.Stretch, VerticalAlignment.Bottom, 0, 1),
                (HorizontalAlignment.Left, VerticalAlignment.Top, -1, -1), (HorizontalAlignment.Right, VerticalAlignment.Top, 1, -1),
                (HorizontalAlignment.Left, VerticalAlignment.Bottom, -1, 1), (HorizontalAlignment.Right, VerticalAlignment.Bottom, 1, 1),
            };
            foreach (var (h, v, x, y) in handles)
            {
                var t = new Thumb { HorizontalAlignment = h, VerticalAlignment = v, Focusable = false };
                if (x == 1 && y == 1)
                {
                    t.SetResourceReference(StyleProperty, "CornerGrip");
                    t.Margin = new Thickness(0, 0, 4, 4);
                    t.Opacity = 0.6;
                    t.ToolTip = "调整大小";
                    t.MouseEnter += (_, _) => t.Opacity = 1;
                    t.MouseLeave += (_, _) => { if (!_resizing) t.Opacity = 0.6; };
                }
                else
                {
                    bool corner = x != 0 && y != 0;
                    t.Template = Bare();
                    t.Cursor = corner ? (x == y ? Cursors.SizeNWSE : Cursors.SizeNESW) : x != 0 ? Cursors.SizeWE : Cursors.SizeNS;
                    if (x != 0) t.Width = corner ? 18 : 8;
                    if (y != 0) t.Height = corner ? 18 : 8;
                    // Straddles the border, mostly outside the panel.
                    t.Margin = new Thickness(x < 0 ? -5 : 0, y < 0 ? -5 : 0, x > 0 ? -5 : 0, y > 0 ? -5 : 0);
                }
                System.Windows.Automation.AutomationProperties.SetName(t, "调整视图大小");
                t.PreviewMouseLeftButtonDown += (_, e) =>
                {
                    if (e.ClickCount != 2) return;
                    SetSize("Auto", 0, 0);
                    e.Handled = true;
                };
                t.DragStarted += (_, _) => BeginResize();
                t.DragDelta += (_, _) => TrackResize(x, y);
                t.DragCompleted += (_, e) => EndResize(!e.Canceled);
                frame.Children.Add(t);
            }
        }

        private void BeginResize()
        {
            if (_closing) return;
            _resizing = true;
            _resizeFrom = Mouse.GetPosition(this);
            _resizeStart = new Size(_panel.ActualWidth, _panel.ActualHeight);
            _panel.BeginAnimation(WidthProperty, null);
            _panel.BeginAnimation(HeightProperty, null);
            ShowSize(_resizeStart);
            _sizeBadge.Visibility = Visibility.Visible;
        }

        // Pointer position against the fixed view (the panel moves as it grows about its centre).
        private void TrackResize(int x, int y)
        {
            if (!_resizing) return;
            var d = Mouse.GetPosition(this) - _resizeFrom;
            var room = Room();
            double minW = Math.Min(Math.Max(FolderMetrics.MinWidth, Chrome() + TitleMin + SearchMin), room.Width);
            double minH = Math.Min(FolderMetrics.MinHeight, room.Height);
            double w = x == 0 ? _resizeStart.Width : Math.Clamp(_resizeStart.Width + 2 * x * d.X, minW, Math.Max(minW, room.Width));
            double h = y == 0 ? _resizeStart.Height : Math.Clamp(_resizeStart.Height + 2 * y * d.Y, minH, Math.Max(minH, room.Height));
            ApplySize(Math.Round(w), Math.Round(h));
            ShowSize(new Size(w, h));
        }

        private void ShowSize(Size s) =>
            _sizeText.Text = string.Create(CultureInfo.InvariantCulture, $"{Math.Round(s.Width)} × {Math.Round(s.Height)}");

        private void EndResize(bool commit)
        {
            if (!_resizing) return;
            _resizing = false;
            _sizeBadge.Visibility = Visibility.Collapsed;
            double w = Math.Round(_panel.Width), h = Math.Round(_panel.Height);
            bool changed = Math.Abs(w - _resizeStart.Width) >= 1 || Math.Abs(h - _resizeStart.Height) >= 1;
            if (commit && changed) SetSize("Custom", w, h);
            else Relayout();
        }

        private void SetSize(string mode, double w, double h)
        {
            var s = _owner._actions.Settings;
            if (s.QuickFolderSize == mode && Math.Abs(s.QuickFolderWidth - w) < 1 && Math.Abs(s.QuickFolderHeight - h) < 1)
            {
                Relayout();
                return;
            }
            s.SetQuickFolderSize(mode, w, h);
            Core.Log.Write(string.Create(CultureInfo.InvariantCulture, $"quick folder size: {mode}{(mode == "Custom" ? $" {w:0} x {h:0}" : "")}"));
        }

        // The view's own entries under the group's: its size and its spacing.
        private void AddViewMenu(ContextMenu menu)
        {
            var s = _owner._actions.Settings;
            menu.Items.Add(new Separator());
            var size = new MenuItem { Header = "视图大小", Tag = Application.Current.TryFindResource("Icon.Fullscreen") };
            bool custom = s.QuickFolderSize == "Custom";
            Add(size.Items, "自动", "", "", () => SetSize("Auto", 0, 0), true).IsChecked = s.QuickFolderSize == "Auto";
            bool preset = false;
            foreach (var (name, w, h) in FolderMetrics.Presets)
            {
                bool on = custom && Math.Abs(s.QuickFolderWidth - w) < 1 && Math.Abs(s.QuickFolderHeight - h) < 1;
                preset |= on;
                Add(size.Items, name, "", string.Create(CultureInfo.InvariantCulture, $"{w} × {h}"), () => SetSize("Custom", w, h), true).IsChecked = on;
            }
            Add(size.Items, "铺满", "", "", () => SetSize("Fill", 0, 0), true).IsChecked = s.QuickFolderSize == "Fill";
            if (custom && !preset)
                Add(size.Items, "自定义", "", string.Create(CultureInfo.InvariantCulture, $"{s.QuickFolderWidth:0} × {s.QuickFolderHeight:0}"), () => { }, true).IsChecked = true;
            menu.Items.Add(size);
            var spacing = new MenuItem { Header = "图标间距", Tag = Application.Current.TryFindResource("Icon.Spacing") };
            foreach (var (key, name) in FolderMetrics.Densities)
                Add(spacing.Items, name, "", "", () => s.QuickFolderDensity = key, true).IsChecked = s.QuickFolderDensity == key;
            menu.Items.Add(spacing);
        }

        // Grows out of the card face: scaled and moved onto it, then eased to the middle.
        private void Grow()
        {
            if (_closing) return;
            _scrim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.62, OpenTime));
            if (!FromCard(out double sx, out double sy, out double dx, out double dy))
            {
                _panel.Opacity = 1;
            }
            else
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                _zoom.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(sx, 1, OpenTime) { EasingFunction = ease });
                _zoom.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(sy, 1, OpenTime) { EasingFunction = ease });
                _shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dx, 0, OpenTime) { EasingFunction = ease });
                _shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dy, 0, OpenTime) { EasingFunction = ease });
                _panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, OpenTime));
            }
            FocusFirst();
        }

        private bool FromCard(out double sx, out double sy, out double dx, out double dy)
        {
            sx = sy = 1;
            dx = dy = 0;
            if (_card.Parent is null || !_card.IsVisible || _panel.ActualWidth <= 0 || _panel.ActualHeight <= 0) return false;
            Rect face, panel;
            try
            {
                face = _card.FaceIn(this);
                panel = _panel.TransformToVisual(this).TransformBounds(new Rect(0, 0, _panel.ActualWidth, _panel.ActualHeight));
            }
            catch (InvalidOperationException) { return false; }
            // The transform group already holds the current animation values; undo them to get the resting rect.
            panel = new Rect(panel.X - _shift.X, panel.Y - _shift.Y, panel.Width / Math.Max(0.01, _zoom.ScaleX), panel.Height / Math.Max(0.01, _zoom.ScaleY));
            if (face.Width <= 0 || face.Height <= 0) return false;
            sx = Math.Clamp(face.Width / panel.Width, 0.05, 1.5);
            sy = Math.Clamp(face.Height / panel.Height, 0.05, 1.5);
            dx = face.X + face.Width / 2 - (panel.X + panel.Width / 2);
            dy = face.Y + face.Height / 2 - (panel.Y + panel.Height / 2);
            return true;
        }

        // Shrinks back into the card and fades; done runs exactly once, at the end or when FinishClose cuts it short.
        public void Close(bool animate, Action done)
        {
            if (_closing) return;
            _closing = true;
            _done = done;
            _outside.Stop();
            IsHitTestVisible = false;
            _finishRename?.Invoke(true);
            _hadFocus = IsKeyboardFocusWithin;
            if (!animate || !IsLoaded)
            {
                FinishClose();
                return;
            }
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var fade = new DoubleAnimation(0, CloseTime);
            fade.Completed += (_, _) => FinishClose();
            _scrim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, CloseTime));
            if (FromCard(out double sx, out double sy, out double dx, out double dy))
            {
                _zoom.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(sx, CloseTime) { EasingFunction = ease });
                _zoom.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(sy, CloseTime) { EasingFunction = ease });
                _shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dx, CloseTime) { EasingFunction = ease });
                _shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dy, CloseTime) { EasingFunction = ease });
            }
            _panel.BeginAnimation(OpacityProperty, fade);
        }

        public void FinishClose()
        {
            if (_done is not { } done) return;
            _done = null;
            done();
            if (_hadFocus) _owner.FocusBoard();
        }

        // Ctrl+← / → (or Ctrl+PageUp / PageDown): the neighbouring group, sliding in from that side.
        private void Step(int delta)
        {
            if (_closing || _owner.Neighbour(Group, delta) is not { } next || next.Group == Group) return;
            _finishRename?.Invoke(true);
            double was = _panel.ActualWidth, wasH = _panel.ActualHeight;
            try
            {
                _quiet = true;
                _search.Clear();
                _quiet = false;
                Fill(next);
            }
            catch (Exception ex)
            {
                _quiet = false;
                Core.Log.Error($"quick folder step to \"{next.Group.Name}\"", ex);
                _owner.CloseFolder(animate: false);
                return;
            }
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(34 * Math.Sign(delta), 0, SlideTime) { EasingFunction = ease });
            _scroll.BeginAnimation(OpacityProperty, new DoubleAnimation(0.15, 1, SlideTime));
            _title.BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, SlideTime));
            if (was > 0 && Math.Abs(was - _panel.Width) > 1)
                _panel.BeginAnimation(WidthProperty, new DoubleAnimation(was, _panel.Width, SlideTime) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            if (wasH > 0 && Math.Abs(wasH - _panel.Height) > 1)
                _panel.BeginAnimation(HeightProperty, new DoubleAnimation(wasH, _panel.Height, SlideTime) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            _scroll.ScrollToHome();
            Dispatcher.BeginInvoke(new Action(FocusFirst), DispatcherPriority.Loaded);
            Core.Log.Write($"quick folder \"{Group.Name}\" opened (step {(delta > 0 ? "next" : "previous")}): {_all.Count} items");
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox) return;
            var mods = Keyboard.Modifiers;
            if (e.Key == Key.Escape)
            {
                if (!ClearSearch()) _owner.CloseFolder(animate: true, "esc");
                e.Handled = true;
                return;
            }
            if (mods == ModifierKeys.Control && e.Key == Key.F)
            {
                _search.Focus();
                _search.SelectAll();
                e.Handled = true;
                return;
            }
            if (mods == ModifierKeys.Control && e.Key is Key.Left or Key.Right or Key.PageUp or Key.PageDown)
            {
                Step(e.Key is Key.Left or Key.PageUp ? -1 : 1);
                e.Handled = true;
                return;
            }
            var tiles = _cells.Children.OfType<Tile>().ToList();
            if (tiles.Count == 0) return;
            int i = Keyboard.FocusedElement is Tile t ? tiles.IndexOf(t) : -1;
            if (mods == ModifierKeys.None && e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End)
            {
                int cols = Math.Max(1, _cells.Columns);
                int next = i < 0 ? 0 : e.Key switch
                {
                    Key.Left => i - 1, Key.Right => i + 1, Key.Up => i - cols, Key.Down => i + cols,
                    Key.Home => 0, _ => tiles.Count - 1,
                };
                if (next >= 0 && next < tiles.Count)
                {
                    tiles[next].Focus();
                    tiles[next].BringIntoView();
                }
                else if (e.Key == Key.Up && next < 0)
                {
                    // Up from the first row: into the search field.
                    _search.Focus();
                    _search.CaretIndex = _search.Text.Length;
                }
                e.Handled = true;
                return;
            }
            if (i >= 0 && _owner.ItemKey(tiles[i], e)) e.Handled = true;
        }

        // Typing anywhere in the view searches it.
        private void OnType(object sender, TextCompositionEventArgs e)
        {
            if (e.OriginalSource is TextBox || _editor is not null || string.IsNullOrWhiteSpace(e.Text) || e.Text.Any(char.IsControl)) return;
            _search.Focus();
            _search.Text += e.Text;
            _search.CaretIndex = _search.Text.Length;
            e.Handled = true;
        }

        private void OnSearchKey(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter when Keyboard.Modifiers == ModifierKeys.None:
                    if (_items.FirstOrDefault() is { } first) _owner._actions.Open(first);
                    break;
                case Key.Down:
                    FocusFirst();
                    break;
                case Key.Escape:
                    if (!ClearSearch()) _owner.CloseFolder(animate: true, "esc");
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            var tile = TileAt(e.OriginalSource);
            if (e.ClickCount == 2)
            {
                if (tile is not null && !_owner._singleClick && !tile.Renaming) _owner._actions.Open(tile.Item);
                _down = null;
                return;
            }
            _down = tile;
            _downAt = e.GetPosition(this);
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _down is not { } tile || tile.Renaming) return;
            var d = e.GetPosition(this) - _downAt;
            if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _down = null;
            var data = new DataObject();
            data.SetData(DragFormat, tile.Item.Path);
            if (QuickActions.Exists(tile.Item.Path)) data.SetData(DataFormats.FileDrop, new[] { tile.Item.Path });
            tile.Opacity = 0.35;
            _owner._board.Frozen = true;
            try { DragDrop.DoDragDrop(tile, data, DragDropEffects.Copy | DragDropEffects.Link); }
            finally
            {
                tile.ClearValue(OpacityProperty);
                _owner._board.Frozen = false;
                _owner.EndArrange();
                ShowSlot(-1);
                _outside.Stop();
            }
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            var tile = TileAt(e.OriginalSource);
            bool click = tile is not null && tile == _down && !tile.Renaming;
            _down = null;
            if (click && _owner._singleClick) _owner._actions.Open(tile!.Item);
        }

        private void OnScrimDrag(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            ShowSlot(-1);
            if (!_outside.IsEnabled) _outside.Start();
        }

        // While searching the shown order is the match order, not the group's: a member dropped back stays where it
        // is, anything else joins the group at the end.
        private void OnPanelDrag(object sender, DragEventArgs e)
        {
            _outside.Stop();
            bool ours = e.Data.GetDataPresent(DragFormat), files = e.Data.GetDataPresent(DataFormats.FileDrop);
            var dragged = ours && e.Data.GetData(DragFormat) is string p ? _owner._actions.Store.Find(p) : null;
            bool still = Searching && dragged is not null && _all.Contains(dragged);
            e.Effects = (ours || files) && !still ? DragDropEffects.Link : DragDropEffects.None;
            ShowSlot((ours || files) && !Searching ? SlotAt(e.GetPosition(_cells)) : -1);
            e.Handled = true;
        }

        private void OnPanelDrop(object sender, DragEventArgs e)
        {
            int slot = _slot >= 0 ? _slot : SlotAt(e.GetPosition(_cells));
            ShowSlot(-1);
            e.Handled = true;
            var actions = _owner._actions;
            if (e.Data.GetData(DragFormat) is string path)
            {
                if (actions.Store.Find(path) is not { } item) return;
                if (!Searching) actions.PlaceInGroup(item, Group.Name, Before(slot, item));
                else if (!_all.Contains(item)) actions.PlaceInGroup(item, Group.Name, null);
                return;
            }
            if (e.Data.GetData(DataFormats.FileDrop) is string[] drop) AddHere(drop, Searching ? -1 : slot);
        }

        // New members from a drop or the + menu: in the group (at the slot in the custom order), lit up in this view.
        private void AddHere(IEnumerable<string> paths, int slot)
        {
            var actions = _owner._actions;
            var added = actions.Add(paths, -1, Group.Name);
            if (added.Count == 0) return;
            if (slot >= 0 && actions.Settings.QuickSort == "Custom")
                foreach (var item in added) actions.PlaceInGroup(item, Group.Name, Before(slot, item));
            actions.ClearHighlight();
            _pop = added;
        }

        private void OpenAddMenu(FrameworkElement anchor)
        {
            var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
            Add(menu.Items, "添加文件", "Icon.File", "", () => AddFromDialog(folders: false), true);
            Add(menu.Items, "添加文件夹", "Icon.Folder", "", () => AddFromDialog(folders: true), true);
            menu.IsOpen = true;
        }

        private void AddFromDialog(bool folders)
        {
            var window = Window.GetWindow(this);
            var launcher = window as LauncherWindow;
            if (launcher is not null) launcher.KeepOpen = true;
            try
            {
                string[]? picked = null;
                if (folders)
                {
                    var dlg = new Win32.OpenFolderDialog { Multiselect = true };
                    if (dlg.ShowDialog(window) == true) picked = dlg.FolderNames;
                }
                else
                {
                    var dlg = new Win32.OpenFileDialog { Multiselect = true, DereferenceLinks = false };
                    if (dlg.ShowDialog(window) == true) picked = dlg.FileNames;
                }
                if (picked is { Length: > 0 } && !_closing) AddHere(picked, -1);
            }
            finally
            {
                if (launcher is not null)
                {
                    launcher.KeepOpen = false;
                    launcher.Activate();
                }
            }
        }

        private QuickItem? Before(int slot, QuickItem dragged)
        {
            for (int i = Math.Max(0, slot); i < _items.Count; i++)
                if (_items[i] != dragged) return _items[i];
            return null;
        }

        // Slot nearest a point: before the first icon of the nearest row whose centre lies right of it.
        private int SlotAt(Point p)
        {
            var tiles = _cells.Children.OfType<Tile>().ToList();
            if (tiles.Count == 0) return 0;
            var centres = tiles.Select(t => t.TranslatePoint(new Point(t.ActualWidth / 2, t.ActualHeight / 2), _cells)).ToList();
            double rowY = centres.Select(c => c.Y).OrderBy(y => Math.Abs(y - p.Y)).First();
            int last = -1;
            for (int i = 0; i < tiles.Count; i++)
            {
                if (Math.Abs(centres[i].Y - rowY) > 1) continue;
                if (centres[i].X > p.X) return i;
                last = i;
            }
            return last + 1;
        }

        private void ShowSlot(int slot)
        {
            _slot = slot;
            var tiles = _cells.Children.OfType<Tile>().ToList();
            if (slot < 0 || tiles.Count == 0)
            {
                _slotBar.Visibility = Visibility.Collapsed;
                return;
            }
            bool after = slot >= tiles.Count;
            var t = tiles[Math.Clamp(slot, 0, tiles.Count - 1)];
            var at = t.TranslatePoint(new Point(after ? t.ActualWidth : 0, 6), (UIElement)_slotBar.Parent);
            _slotBar.Margin = new Thickness(at.X - 1.5, at.Y, 0, 0);
            _slotBar.Height = Math.Max(12, t.ActualHeight - 12);
            _slotBar.Visibility = Visibility.Visible;
        }

        private void BeginRename()
        {
            if (_editor is not null || _closing) return;
            var box = new TextBox { Text = Group.Name, MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            box.SetResourceReference(StyleProperty, "Field");
            _editor = box;
            _title.Visibility = Visibility.Hidden;
            _titleHost.Children.Add(box);
            string was = Group.Name;
            void Finish(bool save)
            {
                if (_editor != box) return;
                _finishRename = null;
                string text = box.Text.Trim();
                _titleHost.Children.Remove(box);
                _title.Visibility = Visibility.Visible;
                _editor = null;
                // Looked up by the name it had: an undo while typing replaces every group object in the store.
                var group = _owner._actions.Store.Groups.FirstOrDefault(g => string.Equals(g.Name, was, StringComparison.OrdinalIgnoreCase));
                if (!save || group is null || text.Length == 0 || text == group.Name) return;
                if (string.Equals(Group.Name, was, StringComparison.OrdinalIgnoreCase)) _title.Text = text;
                _owner._actions.RenameGroup(group, text);
            }
            _finishRename = Finish;
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
