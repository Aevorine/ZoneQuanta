using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using ZoneQuanta.Core.Monitor;
using ZoneQuanta.Core.Quick;
using ZoneQuanta.Core.Settings;
using ZoneQuanta.Platform;
using Shapes = System.Windows.Shapes;

namespace ZoneQuanta.UI.Quick;

// Icon grid / detail list of quick items: open, context actions, drag to reorder, drop files to add.
public sealed class QuickGrid : UserControl
{
    private const string DragFormat = "ZoneQuanta.QuickItem";
    private readonly QuickActions _actions;
    private readonly bool _singleClick;
    private readonly ListBox _list = new();
    private readonly Dictionary<QuickItem, Tile> _tiles = new();
    private string _filter = "";
    private bool _dirty = true;
    private Point _downAt;
    private Tile? _downTile, _dropTile;
    private bool _dragging;

    public QuickGrid(QuickActions actions, bool singleClickOpen)
    {
        _actions = actions;
        _singleClick = singleClickOpen;
        _list.Background = Brushes.Transparent;
        _list.BorderThickness = new Thickness(0);
        _list.Padding = new Thickness(0);
        _list.SelectionMode = SelectionMode.Single;
        _list.AllowDrop = true;
        _list.FocusVisualStyle = null;
        _list.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "TileItem");
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        ScrollViewer.SetCanContentScroll(_list, false);
        Content = _list;

        _list.PreviewMouseLeftButtonDown += OnDown;
        _list.PreviewMouseMove += OnMove;
        _list.PreviewMouseLeftButtonUp += OnUp;
        _list.MouseDoubleClick += (_, e) => { if (!_singleClick && TileAt(e.OriginalSource) is { } t) _actions.Open(t.Item); };
        _list.PreviewKeyDown += OnKey;
        _list.DragOver += OnDragOver;
        _list.DragLeave += (_, _) => MarkDrop(null);
        _list.Drop += OnDrop;

        actions.Store.Changed += _ => Invalidate();
        actions.Settings.PropertyChanged += OnSetting;
        IsVisibleChanged += (_, _) => { if (IsVisible) Sync(); };
        ApplyView();
    }

    public QuickItem? Selected => (_list.SelectedItem as Tile)?.Item;
    public int Count => _list.Items.Count;

    public string Filter
    {
        get => _filter;
        set
        {
            if (_filter == value) return;
            _filter = value;
            Rebuild();
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        }
    }

    public void FocusList()
    {
        if (_list.Items.Count == 0) return;
        if (_list.SelectedIndex < 0) _list.SelectedIndex = 0;
        (_list.ItemContainerGenerator.ContainerFromIndex(_list.SelectedIndex) as ListBoxItem)?.Focus();
    }

    public void OpenSelected()
    {
        if (Selected is { } item) _actions.Open(item);
    }

    public void MoveSelection(int delta)
    {
        if (_list.Items.Count == 0) return;
        _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + delta, 0, _list.Items.Count - 1);
        _list.ScrollIntoView(_list.SelectedItem);
    }

    private void Invalidate()
    {
        _dirty = true;
        if (IsVisible) Dispatcher.BeginInvoke(new Action(Sync));
    }

    private void Sync()
    {
        if (_dirty) Rebuild();
        foreach (var t in _tiles.Values) t.RefreshMeta(false);
    }

    private void OnSetting(object? sender, PropertyChangedEventArgs e)
    {
        string n = e.PropertyName ?? "";
        if (n == nameof(AppSettings.QuickView)) ApplyView();
        else if (n == nameof(AppSettings.QuickSort)) Invalidate();
        else if (n.StartsWith("QuickShow", StringComparison.Ordinal))
            foreach (var t in _tiles.Values) t.Layout(View);
    }

    private string View => _actions.Settings.QuickView == "List" ? "List" : "Grid";

    private void ApplyView()
    {
        var panel = View == "List" ? new FrameworkElementFactory(typeof(StackPanel)) : new FrameworkElementFactory(typeof(WrapPanel));
        _list.ItemsPanel = new ItemsPanelTemplate(panel);
        foreach (var t in _tiles.Values) t.Layout(View);
    }

    private void Rebuild()
    {
        _dirty = false;
        var keep = _list.SelectedItem as Tile;
        var items = _actions.View(_filter).ToList();
        var alive = new HashSet<QuickItem>(_actions.Store.Items);
        foreach (var gone in _tiles.Keys.Where(k => !alive.Contains(k)).ToList()) _tiles.Remove(gone);

        _list.Items.Clear();
        foreach (var item in items)
        {
            if (!_tiles.TryGetValue(item, out var tile))
            {
                tile = new Tile(item, _actions.Settings);
                tile.ContextMenuOpening += OnMenu;
                tile.ContextMenu = new ContextMenu();
                _tiles[item] = tile;
            }
            tile.Layout(View);
            tile.Refresh();
            _list.Items.Add(tile);
        }
        if (keep is not null && _list.Items.Contains(keep)) _list.SelectedItem = keep;

        if (_actions.Highlight is { } h && _tiles.TryGetValue(h, out var fresh))
        {
            _actions.ClearHighlight();
            _list.SelectedItem = fresh;
            _list.ScrollIntoView(fresh);
            fresh.Pop();
        }
    }

    private static Tile? TileAt(object source)
    {
        for (var d = source as DependencyObject; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is Tile t) return t;
        return null;
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _downTile = TileAt(e.OriginalSource);
        _downAt = e.GetPosition(_list);
        _dragging = false;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _downTile is null || _dragging || _downTile.Renaming) return;
        var d = e.GetPosition(_list) - _downAt;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragging = true;
        var data = new DataObject();
        data.SetData(DragFormat, _downTile.Item.Path);
        if (QuickActions.Exists(_downTile.Item.Path)) data.SetData(DataFormats.FileDrop, new[] { _downTile.Item.Path });
        try { DragDrop.DoDragDrop(_downTile, data, DragDropEffects.Copy | DragDropEffects.Link); }
        finally
        {
            MarkDrop(null);
            _downTile = null;
        }
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        var tile = TileAt(e.OriginalSource);
        bool click = !_dragging && tile is not null && tile == _downTile && !tile.Renaming;
        _downTile = null;
        if (click && _singleClick) _actions.Open(tile!.Item);
    }

    private bool CanReorder => _filter.Length == 0 && _actions.Settings.QuickSort == "Custom";

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var target = TileAt(e.OriginalSource);
        if (e.Data.GetDataPresent(DragFormat))
            e.Effects = CanReorder && target is not null ? DragDropEffects.Link : DragDropEffects.None;
        else
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
        MarkDrop(e.Effects == DragDropEffects.None ? null : target);
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        var target = TileAt(e.OriginalSource);
        MarkDrop(null);
        int index = target is null ? -1 : IndexOf(target.Item);
        if (e.Data.GetData(DragFormat) is string path)
        {
            if (!CanReorder || target is null) return;
            if (_actions.Store.Find(path) is { } item && item != target.Item) _actions.Move(item, index);
        }
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            _actions.Add(files, CanReorder ? index : -1);
        }
        e.Handled = true;
    }

    private int IndexOf(QuickItem item)
    {
        var all = _actions.Store.Items;
        for (int i = 0; i < all.Count; i++) if (all[i] == item) return i;
        return -1;
    }

    private void MarkDrop(Tile? tile)
    {
        if (_dropTile == tile) return;
        _dropTile?.SetDropTarget(false);
        _dropTile = tile;
        _dropTile?.SetDropTarget(true);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (Selected is not { } item || (_list.SelectedItem as Tile)?.Renaming == true) return;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.Enter when ctrl:
                _actions.Reveal(item);
                break;
            case Key.Enter:
                _actions.Open(item);
                break;
            case Key.Delete:
                _actions.Remove(item);
                break;
            case Key.F2:
                ((Tile)_list.SelectedItem).BeginRename(name => _actions.Rename(item, name));
                break;
            case Key.C when ctrl:
                _actions.CopyPath(item);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void OnMenu(object sender, ContextMenuEventArgs e)
    {
        var tile = (Tile)sender;
        var item = tile.Item;
        _list.SelectedItem = tile;
        var menu = tile.ContextMenu!;
        menu.Items.Clear();
        bool exists = QuickActions.Exists(item.Path) || PathEngine.IsShell(item.Path);
        Add(menu, "打开", "Icon.Open", "Enter", () => _actions.Open(item), exists);
        if (ShellLauncher.IsExecutable(item.Path)) Add(menu, "以管理员身份运行", "Icon.Shield", "", () => _actions.RunAsAdmin(item), exists);
        if (File.Exists(item.Path)) Add(menu, "打开方式", "Icon.Window", "", () => _actions.OpenWith(item), true);
        Add(menu, "打开所在位置", "Icon.Folder", "Ctrl+Enter", () => _actions.Reveal(item), exists && !PathEngine.IsShell(item.Path));
        menu.Items.Add(new Separator());
        Add(menu, "复制路径", "Icon.Copy", "Ctrl+C", () => _actions.CopyPath(item), true);
        Add(menu, "重命名", "Icon.Edit", "F2", () => tile.BeginRename(name => _actions.Rename(item, name)), true);
        Add(menu, "属性", "Icon.Info", "", () => _actions.Properties(item), exists && !PathEngine.IsShell(item.Path));
        menu.Items.Add(new Separator());
        Add(menu, "移除", "Icon.Trash", "Del", () => _actions.Remove(item), true);
    }

    private static void Add(ContextMenu menu, string text, string icon, string gesture, Action act, bool enabled)
    {
        var mi = new MenuItem { Header = text, InputGestureText = gesture, IsEnabled = enabled };
        mi.Tag = Application.Current.TryFindResource(icon);
        mi.Click += (_, _) => act();
        menu.Items.Add(mi);
    }

    // One quick item. Icons and file details load off the UI thread and are cached.
    private sealed class Tile : Border
    {
        private readonly AppSettings _s;
        private readonly Grid _icon = new();
        private readonly TextBlock _name = new() { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly StackPanel _text = new();
        private readonly StackPanel _details = new();
        private readonly ScaleTransform _scale = new(1, 1);
        private string _view = "";
        private int _iconPx;
        private (DateTime At, long Size, DateTime Modified, bool Exists) _meta;
        private bool _metaLoading;
        private TextBox? _editor;

        public Tile(QuickItem item, AppSettings s)
        {
            Item = item;
            _s = s;
            Background = Brushes.Transparent;
            CornerRadius = new CornerRadius(10);
            BorderThickness = new Thickness(1);
            BorderBrush = Brushes.Transparent;
            RenderTransformOrigin = new Point(0.5, 0.5);
            RenderTransform = _scale;
            _text.Children.Add(_name);
            _text.Children.Add(_details);
        }

        public QuickItem Item { get; }
        public bool Renaming => _editor is not null;

        public void Layout(string view)
        {
            bool showIcon = _s.QuickShowIcon || !_s.QuickShowName;
            _view = view;
            Child = null;
            if (_icon.Parent is System.Windows.Controls.Panel p1) p1.Children.Remove(_icon);
            if (_text.Parent is System.Windows.Controls.Panel p2) p2.Children.Remove(_text);

            if (view == "List")
            {
                Width = double.NaN;
                Padding = new Thickness(10, 7, 10, 7);
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                _icon.Width = _icon.Height = 24;
                _icon.Margin = new Thickness(0, 0, 12, 0);
                _icon.VerticalAlignment = VerticalAlignment.Center;
                _name.TextAlignment = TextAlignment.Left;
                _name.TextWrapping = TextWrapping.NoWrap;
                _name.MaxHeight = double.PositiveInfinity;
                _details.Orientation = Orientation.Horizontal;
                _details.HorizontalAlignment = HorizontalAlignment.Left;
                _text.VerticalAlignment = VerticalAlignment.Center;
                if (showIcon) g.Children.Add(_icon);
                Grid.SetColumn(_text, 1);
                g.Children.Add(_text);
                Child = g;
                SetIconSize(24);
            }
            else
            {
                Width = 112;
                Padding = new Thickness(6, 12, 6, 10);
                var st = new StackPanel();
                _icon.Width = _icon.Height = 40;
                _icon.Margin = new Thickness(0, 0, 0, 8);
                _icon.HorizontalAlignment = HorizontalAlignment.Center;
                _name.TextAlignment = TextAlignment.Center;
                _name.TextWrapping = TextWrapping.Wrap;
                _name.MaxHeight = 36;
                _details.Orientation = Orientation.Vertical;
                _details.HorizontalAlignment = HorizontalAlignment.Center;
                if (showIcon) st.Children.Add(_icon);
                st.Children.Add(_text);
                Child = st;
                SetIconSize(40);
            }
            _name.Visibility = _s.QuickShowName || !showIcon ? Visibility.Visible : Visibility.Collapsed;
            Refresh();
        }

        public void Refresh()
        {
            if (_editor is null) _name.Text = Item.DisplayName;
            ToolTip = Item.Path;
            BuildDetails();
            RefreshMeta(false);
        }

        private void SetIconSize(double dip)
        {
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            int px = (int)Math.Round(dip * Math.Max(1, scale));
            if (px == _iconPx && _icon.Children.Count > 0) return;
            _iconPx = px;
            _icon.Children.Clear();
            if (ShellIcons.Cached(Item.Path, px) is { } cached)
            {
                _icon.Children.Add(MakeImage(cached));
                return;
            }
            var ph = new Shapes.Path
            {
                Data = (Geometry)Application.Current.FindResource(Directory.Exists(Item.Path) ? "Icon.Folder" : "Icon.File"),
                Width = dip * 0.7, Height = dip * 0.7,
            };
            ph.SetResourceReference(StyleProperty, "IconPath");
            ph.SetResourceReference(Shapes.Shape.StrokeProperty, "MutedBrush");
            _icon.Children.Add(ph);
            ShellIcons.Request(Item.Path, px, bmp =>
            {
                if (bmp is null) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_iconPx != px) return;
                    _icon.Children.Clear();
                    var img = MakeImage(bmp);
                    img.Opacity = 0;
                    _icon.Children.Add(img);
                    img.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
                }));
            });
        }

        private static Image MakeImage(BitmapSource src)
        {
            var img = new Image { Source = src, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            return img;
        }

        public void RefreshMeta(bool force)
        {
            if (_metaLoading || (!force && (DateTime.UtcNow - _meta.At).TotalSeconds < 20)) return;
            _metaLoading = true;
            string path = Item.Path;
            Task.Run(() =>
            {
                long size = -1;
                DateTime mod = default;
                bool exists = PathEngine.IsShell(path);
                try
                {
                    var fi = new FileInfo(path);
                    if (fi.Exists) { size = fi.Length; mod = fi.LastWriteTime; exists = true; }
                    else if (Directory.Exists(path)) { mod = Directory.GetLastWriteTime(path); exists = true; }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
                return (DateTime.UtcNow, size, mod, exists);
            }).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
            {
                _metaLoading = false;
                _meta = t.IsCompletedSuccessfully ? t.Result : (DateTime.UtcNow, -1L, default, false);
                Opacity = _meta.Exists ? 1 : 0.45;
                BuildDetails();
            })), TaskScheduler.Default);
        }

        private void BuildDetails()
        {
            _details.Children.Clear();
            var parts = new List<string>();
            if (_s.QuickShowType) parts.Add(QuickActions.TypeName(Item.Path));
            if (_s.QuickShowSize && _meta.Size >= 0 && _meta.At != default) parts.Add(UnitFormat.Size(_meta.Size, false, "Auto"));
            if (_s.QuickShowModified && _meta.Modified != default) parts.Add(_meta.Modified.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            if (_s.QuickShowCount) parts.Add($"{Item.OpenCount} 次");
            if (_s.QuickShowPath) parts.Insert(0, PathEngine.Parent(Item.Path) is { Length: > 0 } parent ? parent : Item.Path);
            foreach (string p in parts)
            {
                var t = new TextBlock
                {
                    Text = p,
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = _view == "List" ? new Thickness(0, 2, 14, 0) : new Thickness(0, 2, 0, 0),
                    TextAlignment = _view == "List" ? TextAlignment.Left : TextAlignment.Center,
                    MaxWidth = _view == "List" ? 420 : 100,
                };
                t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                _details.Children.Add(t);
            }
            _details.Visibility = parts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public void SetDropTarget(bool on)
        {
            if (on) SetResourceReference(BorderBrushProperty, "Accent1Brush");
            else BorderBrush = Brushes.Transparent;
        }

        // New items pop in so the user sees where they landed.
        public void Pop()
        {
            var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
            var a = new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease };
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            SetResourceReference(BorderBrushProperty, "Accent1Brush");
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
            t.Tick += (_, _) => { t.Stop(); BorderBrush = Brushes.Transparent; };
            t.Start();
        }

        public void BeginRename(Action<string> commit)
        {
            if (_editor is not null) return;
            var box = new TextBox { Text = Item.DisplayName, MinWidth = 80 };
            box.SetResourceReference(StyleProperty, "Field");
            _editor = box;
            int index = _text.Children.IndexOf(_name);
            _text.Children.Remove(_name);
            _text.Children.Insert(Math.Max(0, index), box);
            bool done = false;
            void Finish(bool save)
            {
                if (done) return;
                done = true;
                string text = box.Text;
                _text.Children.Remove(box);
                _text.Children.Insert(0, _name);
                _editor = null;
                if (save && text.Trim().Length > 0) commit(text);
                else Refresh();
            }
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { Finish(true); e.Handled = true; }
                else if (e.Key == Key.Escape) { Finish(false); e.Handled = true; }
            };
            box.LostKeyboardFocus += (_, _) => Finish(true);
            Dispatcher.BeginInvoke(new Action(() => { box.Focus(); box.SelectAll(); }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }
}
