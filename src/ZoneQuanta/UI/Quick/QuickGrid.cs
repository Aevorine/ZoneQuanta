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
using ZoneQuanta.UI.Controls;
using Shapes = System.Windows.Shapes;

namespace ZoneQuanta.UI.Quick;

// Quick items as group cards (QuickGrid.Board.cs), an icon grid or a detail list: open, context actions,
// drag to reorder or regroup, drop files to add.
public sealed partial class QuickGrid : UserControl
{
    private const string DragFormat = "ZoneQuanta.QuickItem";
    private const string GroupDragFormat = "ZoneQuanta.QuickGroup";
    private readonly QuickActions _actions;
    private readonly bool _singleClick;
    private readonly ListBox _list = new();
    private readonly Dictionary<QuickItem, Tile> _tiles = new();
    private string _filter = "";
    private bool _dirty = true;
    private Point _downAt;
    private Tile? _downTile, _dropTile;
    private bool _dragging;
    // Spacing changes arrive per slider step; the board rebuilds once they settle.
    private readonly System.Windows.Threading.DispatcherTimer _spacingTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };

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

        InitBoard();
        actions.AttachUndoKeys(this);
        actions.Store.Changed += _ => Invalidate();
        actions.IconsChanged += files =>
        {
            var set = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
            foreach (var t in _boardTiles.Values.Concat(_tiles.Values).Concat(_folderTiles.Values))
                if (t.Item.Icon is { } f && set.Contains(f)) t.ReloadIcon();
        };
        actions.GoneChanged += () => { foreach (var t in OnBoard ? _boardTiles.Values.Concat(_folderTiles.Values) : _tiles.Values) t.RefreshMeta(true); };
        actions.Settings.PropertyChanged += OnSetting;
        _spacingTimer.Tick += (_, _) =>
        {
            _spacingTimer.Stop();
            if (OnBoard) Invalidate();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Sync();
            else
            {
                CloseFolder(animate: false);
                FinishFading();
            }
        };
        ApplyView();
    }

    // A focused tile whose item was removed no longer counts.
    public QuickItem? Selected => OnBoard
        ? (_focused is { Parent: not null } f && _actions.Store.Items.Contains(f.Item) ? f.Item : null)
        : (_list.SelectedItem as Tile)?.Item;

    // Raised when the arrow keys leave the top of the board, so the window can return to its search field.
    public event Action? LeftTop;

    private bool OnBoard => _actions.Settings.QuickLayout == "Groups" && _filter.Trim().Length == 0;
    private string FlatView => _actions.Settings.QuickLayout == "List" ? "List" : "Grid";

    public string Filter
    {
        get => _filter;
        set
        {
            if (_filter == value) return;
            _filter = value;
            Rebuild();
            if (!OnBoard && _list.Items.Count > 0) _list.SelectedIndex = 0;
        }
    }

    public void FocusList()
    {
        if (OnBoard)
        {
            FocusBoard();
            return;
        }
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
        foreach (var t in OnBoard ? _boardTiles.Values : _tiles.Values) t.RefreshMeta(false);
    }

    private void OnSetting(object? sender, PropertyChangedEventArgs e)
    {
        string n = e.PropertyName ?? "";
        if (n == nameof(AppSettings.QuickLayout))
        {
            ApplyView();
            Invalidate();
        }
        else if (n == nameof(AppSettings.QuickSort)) Invalidate();
        else if (n is nameof(AppSettings.QuickCardCols) or nameof(AppSettings.QuickCardRows))
        {
            if (OnBoard) Invalidate();
        }
        else if (n is nameof(AppSettings.QuickGapX) or nameof(AppSettings.QuickGapY)
            or nameof(AppSettings.QuickPadX) or nameof(AppSettings.QuickPadY) or nameof(AppSettings.QuickGroupGap) or nameof(AppSettings.QuickSpacing) or nameof(AppSettings.QuickFill))
        {
            _spacingTimer.Stop();
            _spacingTimer.Start();
        }
        else if (n.StartsWith("QuickShow", StringComparison.Ordinal))
        {
            ApplyView();
            if (OnBoard) Invalidate();
        }
    }

    // Grid tiles spread evenly over the width; list rows fill columns, several side by side when the
    // window is wide enough for the name plus every detail shown.
    private void ApplyView()
    {
        var panel = new FrameworkElementFactory(typeof(UniformWrapPanel));
        if (FlatView == "List")
        {
            panel.SetValue(UniformWrapPanel.MinItemWidthProperty, ListColumnWidth());
            panel.SetValue(UniformWrapPanel.StretchItemsProperty, true);
        }
        else panel.SetValue(UniformWrapPanel.MinItemWidthProperty, 132.0);
        _list.ItemsPanel = new ItemsPanelTemplate(panel);
        foreach (var t in _tiles.Values) t.Layout(FlatView);
    }

    private double ListColumnWidth()
    {
        var s = _actions.Settings;
        double w = 300;
        if (s.QuickShowPath) w += 260;
        if (s.QuickShowType) w += 90;
        if (s.QuickShowSize) w += 80;
        if (s.QuickShowModified) w += 130;
        if (s.QuickShowCount) w += 60;
        return w;
    }

    // A launcher opens at the top, like the Start menu; a just-added item still scrolls itself into
    // view afterwards because that request is queued behind this one.
    public void ScrollHome()
    {
        _boardScroll.ScrollToHome();
        FindScroll(_list)?.ScrollToHome();
    }

    private static ScrollViewer? FindScroll(DependencyObject d)
    {
        if (d is ScrollViewer sv) return sv;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            if (FindScroll(VisualTreeHelper.GetChild(d, i)) is { } found) return found;
        return null;
    }

    private void Rebuild()
    {
        _dirty = false;
        var alive = new HashSet<QuickItem>(_actions.Store.Items);
        foreach (var gone in _tiles.Keys.Where(k => !alive.Contains(k)).ToList()) _tiles.Remove(gone);
        foreach (var gone in _boardTiles.Keys.Where(k => !alive.Contains(k)).ToList()) _boardTiles.Remove(gone);
        foreach (var gone in _folderTiles.Keys.Where(k => !alive.Contains(k)).ToList()) _folderTiles.Remove(gone);
        if (OnBoard)
        {
            _list.Items.Clear();
            BuildBoard();
            Content = _boardRoot;
            RefreshFolder();
            return;
        }
        CloseFolder(animate: false);
        ClearBoard();
        Content = _list;

        var keep = _list.SelectedItem as Tile;
        var items = _actions.View(_filter).ToList();
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
            tile.Layout(FlatView);
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

    // Reordering needs the whole list in view (no search filter); under another sort the shown order is kept.
    private bool CanReorder => _filter.Length == 0;

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var target = TileAt(e.OriginalSource);
        if (e.Data.GetDataPresent(DragFormat))
            e.Effects = CanReorder ? DragDropEffects.Link : DragDropEffects.None;
        else
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
        MarkDrop(e.Effects == DragDropEffects.None ? null : target);
        e.Handled = true;
    }

    // Before the tile under the pointer, or after it past its middle (right half in the grid, lower half in a list);
    // empty space below the last tile is the end.
    private QuickItem? InsertBefore(DragEventArgs e, Tile? target, QuickItem dragged)
    {
        var view = _list.Items.OfType<Tile>().Select(t => t.Item).ToList();
        if (target is null) return null;
        var p = e.GetPosition(target);
        bool after = FlatView == "List" ? p.Y > target.ActualHeight / 2 : p.X > target.ActualWidth / 2;
        int i = view.IndexOf(target.Item) + (after ? 1 : 0);
        while (i < view.Count && view[i] == dragged) i++;
        return i < view.Count ? view[i] : null;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        var target = TileAt(e.OriginalSource);
        MarkDrop(null);
        if (e.Data.GetData(DragFormat) is string path)
        {
            if (!CanReorder || _actions.Store.Find(path) is not { } item || item == target?.Item) return;
            _actions.MoveBefore(item, InsertBefore(e, target, item));
        }
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            // Files from outside take the spot only in the custom order; under another sort they are simply added.
            foreach (var added in _actions.Add(files))
                if (CanReorder && _actions.Settings.QuickSort == "Custom") _actions.MoveBefore(added, InsertBefore(e, target, added));
        }
        e.Handled = true;
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
        if (_list.SelectedItem is Tile tile && ItemKey(tile, e)) e.Handled = true;
    }

    // The configurable in-launcher shortcuts, shared by the list and the board.
    private bool ItemKey(Tile tile, KeyEventArgs e)
    {
        var item = tile.Item;
        if (tile.Renaming) return false;
        if (Chord("Reveal").Matches(e)) _actions.Reveal(item);
        else if (Chord("Rename").Matches(e)) tile.BeginRename(name => _actions.Rename(item, name));
        else if (Chord("Remove").Matches(e)) _actions.Remove(item);
        else if (Chord("CopyPath").Matches(e)) _actions.CopyPath(item);
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) _actions.Open(item);
        else return false;
        return true;
    }

    private KeyChord Chord(string name) => KeyChord.Parse(_actions.Settings.LocalKey(name));

    private void OnMenu(object sender, ContextMenuEventArgs e)
    {
        var tile = (Tile)sender;
        var item = tile.Item;
        if (_list.Items.Contains(tile)) _list.SelectedItem = tile;
        var menu = tile.ContextMenu!;
        menu.Items.Clear();
        bool exists = QuickActions.Exists(item.Path) || PathEngine.IsShell(item.Path);
        Add(menu, "打开", "Icon.Open", "Enter", () => _actions.Open(item), exists);
        if (ShellLauncher.IsExecutable(item.Path)) Add(menu, "以管理员身份运行", "Icon.Shield", "", () => _actions.RunAsAdmin(item), exists);
        if (File.Exists(item.Path)) Add(menu, "打开方式", "Icon.Window", "", () => _actions.OpenWith(item), true);
        Add(menu, "打开所在位置", "Icon.Folder", Chord("Reveal").Display, () => _actions.Reveal(item), exists && !PathEngine.IsShell(item.Path));
        menu.Items.Add(new Separator());
        Add(menu, "复制路径", "Icon.Copy", Chord("CopyPath").Display, () => _actions.CopyPath(item), true);
        Add(menu, "重命名", "Icon.Edit", Chord("Rename").Display, () => tile.BeginRename(name => _actions.Rename(item, name)), true);
        Add(menu, "属性", "Icon.Info", "", () => _actions.Properties(item), exists && !PathEngine.IsShell(item.Path));
        menu.Items.Add(GroupSubmenu(item));
        if (item.Col is not null && item.Group.Length == 0) Add(menu, "取消固定位置", "Icon.Pin", "", () => _actions.UnpinItem(item), true);
        menu.Items.Add(new Separator());
        Add(menu, "移除", "Icon.Trash", Chord("Remove").Display, () => _actions.Remove(item), true);
        if (_actions.Gone.Count > 0) Add(menu, $"清理失效项（{_actions.Gone.Count}）", "Icon.Trash", "", () => _actions.CleanGone(), true);
    }

    private MenuItem GroupSubmenu(QuickItem item)
    {
        var sub = new MenuItem { Header = "移到分组", Tag = Application.Current.TryFindResource("Icon.Groups") };
        foreach (var g in _actions.Store.Groups)
        {
            var mi = new MenuItem { Header = g.Name, IsChecked = string.Equals(item.Group, g.Name, StringComparison.OrdinalIgnoreCase) };
            string name = g.Name;
            mi.Click += (_, _) => { if (!mi.IsChecked) _actions.MoveToGroup(item, name); };
            sub.Items.Add(mi);
        }
        if (sub.Items.Count > 0) sub.Items.Add(new Separator());
        Add(sub.Items, "新建分组", "Icon.Plus", "", () => _actions.NewGroup(new[] { item }), true);
        Add(sub.Items, "移出分组", "Icon.Close", "", () => _actions.Ungroup(item), item.Group.Length > 0);
        return sub;
    }

    private static void Add(ContextMenu menu, string text, string icon, string gesture, Action act, bool enabled) =>
        Add(menu.Items, text, icon, gesture, act, enabled);

    private static MenuItem Add(ItemCollection items, string text, string icon, string gesture, Action act, bool enabled)
    {
        var mi = new MenuItem { Header = text, InputGestureText = gesture, IsEnabled = enabled };
        mi.Tag = Application.Current.TryFindResource(icon);
        mi.Click += (_, _) => act();
        items.Add(mi);
        return mi;
    }
}
