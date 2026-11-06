using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using ZoneQuanta.Core.Quick;
using ZoneQuanta.UI.Controls;

namespace ZoneQuanta.UI.Quick;

// Folder view: every member of a folded group in a panel over the board, grown out of the card. The board
// underneath never moves. Open, right-click, rename, reorder by dragging inside, drag out over the dimmed
// board to drop anywhere on it (the view gets out of the way), drop files on it to add them to the group.
public sealed partial class QuickGrid
{
    private readonly Grid _boardRoot = new();
    private readonly Dictionary<QuickItem, Tile> _folderTiles = new();
    private Folder? _folder;

    // Esc closes the folder view first (the window's own Esc comes after).
    public bool CloseFolderIfOpen()
    {
        if (_folder is null) return false;
        CloseFolder(animate: true);
        return true;
    }

    private void OpenFolder(GroupCard card)
    {
        CloseFolder(animate: false);
        var f = new Folder(this, card);
        _folder = f;
        _boardRoot.Children.Add(f);
        Core.Log.Write($"quick folder \"{card.Group.Name}\" opened: {card.Items.Count} items");
    }

    private void CloseFolder(bool animate)
    {
        if (_folder is not { } f) return;
        _folder = null;
        f.Close(animate, () => _boardRoot.Children.Remove(f));
    }

    // After a rebuild: the open folder follows its group (renamed, reordered, members gone), or closes.
    private void RefreshFolder()
    {
        if (_folder is not { } f) return;
        var card = _cards.FirstOrDefault(c => c.Group == f.Group)
            ?? _cards.FirstOrDefault(c => string.Equals(c.Group.Name, f.Group.Name, StringComparison.OrdinalIgnoreCase));
        if (card is null || card.Items.Count == 0) CloseFolder(animate: false);
        else f.Fill(card);
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
        tile.Margin = new Thickness(0);
        tile.VerticalAlignment = VerticalAlignment.Center;
        tile.Layout("Card", _m);
        return tile;
    }

    private sealed class Folder : Grid
    {
        private static readonly Duration OpenTime = TimeSpan.FromMilliseconds(200), CloseTime = TimeSpan.FromMilliseconds(150);
        private readonly QuickGrid _owner;
        private readonly Border _scrim = new() { Opacity = 0 };
        private readonly Border _panel = new() { CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        private readonly UniformWrapPanel _cells = new();
        private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, AllowDrop = true };
        private readonly TextBlock _title = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _count = new() { FontSize = 13, Margin = new Thickness(8, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        private readonly Grid _titleHost = new() { Background = Brushes.Transparent, Cursor = Cursors.IBeam };
        private readonly Border _slotBar = new() { Width = 3, CornerRadius = new CornerRadius(1.5), Visibility = Visibility.Collapsed, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        private readonly ScaleTransform _zoom = new(1, 1);
        private readonly TranslateTransform _shift = new();
        private readonly DispatcherTimer _outside = new() { Interval = TimeSpan.FromMilliseconds(260) };
        private GroupCard _card;
        private List<QuickItem> _items = new();
        private Tile? _down;
        private Point _downAt;
        private int _slot = -1;
        private bool _closing;
        private TextBox? _editor;

        public Folder(QuickGrid owner, GroupCard card)
        {
            _owner = owner;
            _card = card;
            Group = card.Group;
            Focusable = false;

            _scrim.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            _scrim.AllowDrop = true;
            _scrim.MouseLeftButtonDown += (_, e) => { _owner.CloseFolder(animate: true); e.Handled = true; };
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

            var header = new Grid { Margin = new Thickness(18, 10, 8, 4) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(_titleHost);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(HeaderButton("Icon.Down", "原地展开", () =>
            {
                var c = _card;
                _owner.CloseFolder(animate: false);
                if (!c.Group.Expanded) c.Toggle();
            }));
            buttons.Children.Add(HeaderButton("Icon.Close", "关闭", () => _owner.CloseFolder(animate: true)));
            Grid.SetColumn(buttons, 1);
            header.Children.Add(buttons);
            DockPanel.SetDock(header, Dock.Top);

            var body = new Grid();
            body.Children.Add(_cells);
            body.Children.Add(_slotBar);
            _slotBar.SetResourceReference(Border.BackgroundProperty, "Accent1Brush");
            _scroll.Content = body;
            _scroll.PreviewMouseLeftButtonDown += OnDown;
            _scroll.PreviewMouseMove += OnMove;
            _scroll.PreviewMouseLeftButtonUp += OnUp;
            _scroll.DragOver += OnPanelDrag;
            _scroll.DragLeave += (_, _) => ShowSlot(-1);
            _scroll.Drop += OnPanelDrop;

            var dock = new DockPanel();
            dock.Children.Add(header);
            dock.Children.Add(_scroll);
            _panel.Child = dock;
            Children.Add(_panel);

            _outside.Tick += (_, _) =>
            {
                // The pointer left the view while dragging: get out of the way so the board takes the drop.
                _outside.Stop();
                _owner.CloseFolder(animate: false);
            };
            PreviewKeyDown += OnKey;
            SizeChanged += (_, _) => Reflow();
            Fill(card);
            Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(Grow), DispatcherPriority.Loaded);
        }

        public QuickGroup Group { get; private set; }

        private static Button HeaderButton(string icon, string tip, Action act)
        {
            var b = new Button { Tag = Application.Current.TryFindResource(icon), ToolTip = tip };
            b.SetResourceReference(StyleProperty, "IconButton");
            System.Windows.Automation.AutomationProperties.SetName(b, tip);
            b.Click += (_, e) => { act(); e.Handled = true; };
            return b;
        }

        public void Fill(GroupCard card)
        {
            _card = card;
            Group = card.Group;
            _items = card.Items.ToList();
            if (_editor is null) _title.Text = Group.Name;
            _count.Text = _items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var keep = Keyboard.FocusedElement as Tile;
            _cells.Children.Clear();
            foreach (var item in _items) _cells.Children.Add(_owner.FolderTile(item));
            Reflow();
            if (keep is not null && _cells.Children.Contains(keep)) keep.Focus();
        }

        // Columns close to a 3:2 block, at least the card's own, at most what the board area holds;
        // rows beyond the height scroll.
        private void Reflow()
        {
            var m = _owner._m;
            double w = ActualWidth > 0 ? ActualWidth : _owner._boardRoot.ActualWidth;
            double h = ActualHeight > 0 ? ActualHeight : _owner._boardRoot.ActualHeight;
            if (w <= 0 || h <= 0) return;
            double padX = Math.Max(10, m.InsetX), padY = Math.Max(6, m.InsetY);
            int fit = Math.Max(1, (int)Math.Floor((w - 40 - 2 * padX) / m.PitchX));
            int n = Math.Max(1, _items.Count);
            int want = Math.Max(Math.Min(n, Math.Max(4, _card.Columns)), (int)Math.Ceiling(Math.Sqrt(n * 1.5)));
            int cols = Math.Clamp(want, 1, fit);
            int rows = (int)Math.Ceiling(n / (double)cols);
            _cells.Columns = cols;
            _cells.Rows = rows;
            _cells.Width = cols * m.PitchX;
            _cells.Height = rows * m.PitchY;
            _cells.Margin = new Thickness(padX, padY, padX, padY);
            double tileW = Math.Min(m.TileWidth, m.PitchX);
            foreach (var t in _cells.Children.OfType<Tile>()) t.FitCard(tileW, m.PitchY);
            _scroll.MaxHeight = Math.Max(m.PitchY + 2 * padY, h - 40 - 58);
            _panel.Width = cols * m.PitchX + 2 * padX + 2 + (rows * m.PitchY + 2 * padY > _scroll.MaxHeight ? 12 : 0);
        }

        // Grows out of the card face: scaled and moved onto it, then eased to the middle.
        private void Grow()
        {
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
            (_cells.Children.OfType<Tile>().FirstOrDefault())?.Focus();
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

        public void Close(bool animate, Action done)
        {
            if (_closing) return;
            _closing = true;
            _outside.Stop();
            IsHitTestVisible = false;
            if (_editor is not null) Keyboard.ClearFocus();
            bool hadFocus = IsKeyboardFocusWithin;
            if (!animate || !IsLoaded)
            {
                done();
                if (hadFocus) _owner.FocusBoard();
                return;
            }
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var fade = new DoubleAnimation(0, CloseTime);
            fade.Completed += (_, _) =>
            {
                done();
                if (hadFocus) _owner.FocusBoard();
            };
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

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox) return;
            if (e.Key == Key.Escape)
            {
                _owner.CloseFolder(animate: true);
                e.Handled = true;
                return;
            }
            var tiles = _cells.Children.OfType<Tile>().ToList();
            if (tiles.Count == 0) return;
            int i = Keyboard.FocusedElement is Tile t ? tiles.IndexOf(t) : -1;
            if (Keyboard.Modifiers == ModifierKeys.None && e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End)
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
                e.Handled = true;
                return;
            }
            if (i >= 0 && _owner.ItemKey(tiles[i], e)) e.Handled = true;
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

        private void OnPanelDrag(object sender, DragEventArgs e)
        {
            _outside.Stop();
            bool ours = e.Data.GetDataPresent(DragFormat), files = e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effects = ours || files ? DragDropEffects.Link : DragDropEffects.None;
            ShowSlot(ours || files ? SlotAt(e.GetPosition(_cells)) : -1);
            e.Handled = true;
        }

        private void OnPanelDrop(object sender, DragEventArgs e)
        {
            int slot = _slot >= 0 ? _slot : SlotAt(e.GetPosition(_cells));
            ShowSlot(-1);
            e.Handled = true;
            if (e.Data.GetData(DragFormat) is string path)
            {
                if (_owner._actions.Store.Find(path) is not { } item) return;
                _owner._actions.PlaceInGroup(item, Group.Name, Before(slot, item));
                return;
            }
            if (e.Data.GetData(DataFormats.FileDrop) is string[] drop)
            {
                var added = _owner._actions.Add(drop, -1, Group.Name);
                if (_owner._actions.Settings.QuickSort == "Custom")
                    foreach (var item in added) _owner._actions.PlaceInGroup(item, Group.Name, Before(slot, item));
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
            if (_editor is not null) return;
            var box = new TextBox { Text = Group.Name, MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            box.SetResourceReference(StyleProperty, "Field");
            _editor = box;
            _title.Visibility = Visibility.Hidden;
            _titleHost.Children.Add(box);
            bool done = false;
            void Finish(bool save)
            {
                if (done) return;
                done = true;
                string text = box.Text.Trim();
                _titleHost.Children.Remove(box);
                _title.Visibility = Visibility.Visible;
                _editor = null;
                if (save && text.Length > 0 && text != Group.Name)
                {
                    _title.Text = text;
                    _owner._actions.RenameGroup(Group, text);
                }
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
