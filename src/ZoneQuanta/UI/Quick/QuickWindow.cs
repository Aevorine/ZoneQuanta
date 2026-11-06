using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ZoneQuanta.Core.Quick;
using Win32 = Microsoft.Win32;

namespace ZoneQuanta.UI.Quick;

// The quick-access launcher: search / path field on top, pinned items below.
public sealed class QuickWindow : LauncherWindow
{
    private static readonly (string Key, string Text)[] Sorts =
    {
        ("Custom", "自定义"), ("Name", "名称"), ("Recent", "最近使用"), ("Frequent", "最常用"), ("Type", "类型"),
    };

    private readonly QuickActions _actions;
    private readonly Action _openSettings;
    private readonly TextBox _search = new() { FontSize = 15 };
    private readonly QuickGrid _grid;
    private readonly ToggleButton _groups, _tiles, _list;
    private readonly FrameworkElement _empty;
    private readonly Controls.WindowMemory _memory;
    private readonly Controls.SizeBadge _sizeBadge = new();
    private readonly Button _clean = new() { VerticalAlignment = VerticalAlignment.Center };

    public QuickWindow(QuickActions actions, Action openSettings) : base("ZoneQuanta Quick")
    {
        _actions = actions;
        _openSettings = openSettings;
        _grid = new QuickGrid(actions, singleClickOpen: true) { Margin = new Thickness(-2, 12, -2, 0) };
        _search.SetResourceReference(StyleProperty, "IconField");
        _search.Tag = FindResource("Icon.Search");
        MainInput = _search;
        actions.AttachUndoKeys(this);

        _groups = Toggle("Icon.Groups", "分组");
        _tiles = Toggle("Icon.Grid", "网格");
        _list = Toggle("Icon.List", "列表");
        _groups.Click += (_, _) => actions.Settings.QuickLayout = "Groups";
        _tiles.Click += (_, _) => actions.Settings.QuickLayout = "Grid";
        _list.Click += (_, _) => actions.Settings.QuickLayout = "List";
        var sort = IconButton("Icon.Sort", "排序");
        sort.Click += (_, _) => OpenSortMenu(sort);
        var add = IconButton("Icon.Plus", "添加");
        add.Click += (_, _) => OpenAddMenu(add);
        var settings = IconButton("Icon.Sliders", "设置");
        settings.Click += (_, _) => { Hide(); _openSettings(); };
        // Shown only while some items point at something that is gone; one click clears them all (Ctrl+Z brings them back).
        _clean.SetResourceReference(StyleProperty, "Pill");
        _clean.Tag = FindResource("Icon.Trash");
        _clean.Margin = new Thickness(4, 0, 6, 0);
        _clean.Click += (_, _) =>
        {
            int n = actions.CleanGone();
            if (n > 0) _sizeBadge.Flash($"已清理 {n}");
        };
        actions.GoneChanged += SyncClean;
        SyncClean();

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (var b in new UIElement[] { _clean, _groups, _tiles, _list, sort, add, settings }) tools.Children.Add(b);

        var bar = new Grid { Background = Brushes.Transparent };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.Children.Add(_search);
        Grid.SetColumn(tools, 1);
        bar.Children.Add(tools);

        _empty = BuildEmpty();
        var body = new Grid();
        body.Children.Add(_grid);
        body.Children.Add(_empty);

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(bar);
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        Grid.SetRowSpan(_sizeBadge, 2);
        root.Children.Add(_sizeBadge);
        var grip = ResizeGrip();
        Grid.SetRowSpan(grip, 2);
        root.Children.Add(grip);
        StateChanged += (_, _) => grip.Visibility = WindowState == WindowState.Maximized ? Visibility.Collapsed : Visibility.Visible;
        SetRoot(root);
        bar.MouseRightButtonUp += (_, e) =>
        {
            if (e.OriginalSource != bar && e.OriginalSource != tools) return;
            OpenWindowMenu();
            e.Handled = true;
        };
        // Margins and bar gaps move at once; buttons and the empty part of the field once the pointer travels.
        Controls.WindowDrag.Attach(this, this, e =>
        {
            // Popups have their own presentation source: never a window move.
            if (e.OriginalSource is not Visual src || PresentationSource.FromVisual(src) != PresentationSource.FromVisual(this)) return Controls.WindowDrag.Grip.None;
            if (src == bar || src == tools || !root.IsAncestorOf(src)) return Controls.WindowDrag.Grip.Blank;
            if (!bar.IsAncestorOf(src)) return Controls.WindowDrag.Grip.None;
            return Controls.WindowDrag.Ancestor<TextBox>(src) == _search && Controls.WindowDrag.OnCharacters(_search, e)
                ? Controls.WindowDrag.Grip.Text : Controls.WindowDrag.Grip.Content;
        });
        MinWidth = 420;
        MinHeight = 300;

        _search.TextChanged += (_, _) => { if (!PathEngine.LooksLikePath(_search.Text)) _grid.Filter = _search.Text; };
        _search.PreviewKeyDown += OnSearchKey;
        actions.Opened += () => { if (IsVisible && actions.Settings.QuickCloseAfterOpen) Hide(); };
        actions.Failed += _ => { if (IsActive) Shake(); };
        actions.Store.Changed += _ => SyncEmpty();
        actions.Settings.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Core.Settings.AppSettings.QuickLayout)) SyncView(); };
        _grid.LeftTop += () => _search.Focus();
        SyncView();
        SyncEmpty();
        _memory = new Controls.WindowMemory(this, () => actions.Settings.QuickBounds, b => actions.Settings.QuickBounds = b, "quick");
        _sizeBadge.Attach(_memory);
        actions.Importer.Finished += text => Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) _sizeBadge.Flash(text); }));
    }

    private void SyncClean()
    {
        int n = _actions.Gone.Count;
        _clean.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        _clean.Content = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _clean.ToolTip = $"清理失效项（{n}）";
        System.Windows.Automation.AutomationProperties.SetName(_clean, "清理失效项");
    }

    public Action? ImportStart { get; set; }

    // Default before the user sizes it: large, centred a little above the middle.
    protected override Rect Place(Rect work, bool firstTime)
    {
        double w = Math.Clamp(work.Width * 0.64, 640, 1320), h = Math.Clamp(work.Height * 0.78, 460, 980);
        return new Rect(work.Left + (work.Width - w) / 2, work.Top + (work.Height - h) * 0.4, w, h);
    }

    // Reopens at the size, spot and maximized state it was left in (see WindowMemory).
    protected override void PlaceOn(Rect work, bool firstTime)
    {
        var spot = _actions.Settings.QuickPlacement switch
        {
            "Center" => Controls.WindowMemory.Spot.Center,
            "Cursor" => Controls.WindowMemory.Spot.Cursor,
            _ => Controls.WindowMemory.Spot.Last,
        };
        _memory.Place(spot, Place(work, firstTime).Size, rise: 0.4);
    }

    protected override void OnPresented()
    {
        _search.Focus();
        _search.SelectAll();
        SyncEmpty();
        _grid.ScrollHome();
        _actions.ScheduleGoneScan();
    }

    protected override bool OnEscape()
    {
        if (_search.Text.Length == 0) return false;
        _search.Clear();
        return true;
    }

    private void OnSearchKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (PathEngine.LooksLikePath(_search.Text)) _actions.OpenText(_search.Text);
                else _grid.OpenSelected();
                break;
            case Key.Down:
                _grid.FocusList();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void SyncView()
    {
        string layout = _actions.Settings.QuickLayout;
        _groups.IsChecked = layout == "Groups";
        _tiles.IsChecked = layout == "Grid";
        _list.IsChecked = layout == "List";
    }

    private void SyncEmpty()
    {
        bool empty = _actions.Store.Items.Count == 0;
        _empty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        _grid.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private FrameworkElement BuildEmpty()
    {
        var icon = new System.Windows.Shapes.Path { Data = (Geometry)FindResource("Icon.Bolt"), Width = 56, Height = 56, Margin = new Thickness(0, 0, 0, 22) };
        icon.SetResourceReference(StyleProperty, "IconPath");
        icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "LineBrush");
        icon.StrokeThickness = 1.2;
        var files = new Button { Content = "添加文件", Margin = new Thickness(0, 0, 6, 0) };
        files.SetResourceReference(StyleProperty, "Primary");
        files.Click += (_, _) => AddFiles();
        var folder = new Button { Content = "添加文件夹", Margin = new Thickness(6, 0, 0, 0) };
        folder.SetResourceReference(StyleProperty, "Action");
        folder.Click += (_, _) => AddFolder();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        buttons.Children.Add(files);
        buttons.Children.Add(folder);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(icon);
        stack.Children.Add(buttons);
        var zone = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Margin = new Thickness(0, 12, 0, 0), AllowDrop = true, Child = stack, Background = Brushes.Transparent };
        zone.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        zone.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true; };
        zone.Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] f) _actions.Add(f); };
        return zone;
    }

    private void OpenSortMenu(FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        foreach (var (key, text) in Sorts)
        {
            var mi = new MenuItem { Header = text, IsCheckable = true, IsChecked = _actions.Settings.QuickSort == key };
            mi.Click += (_, _) => _actions.Settings.QuickSort = key;
            menu.Items.Add(mi);
        }
        menu.Items.Add(new Separator());
        var s = _actions.Settings;
        menu.Items.Add(QuickGrid.CardSizeMenu(menu, s.QuickCardCols, s.QuickCardRows, "自动", _actions.SetDefaultSize));
        var auto = new MenuItem { Header = "自动分类", IsCheckable = true, IsChecked = _actions.Settings.QuickAutoGroup };
        auto.Click += (_, _) => _actions.Settings.QuickAutoGroup = auto.IsChecked;
        menu.Items.Add(auto);
        if (_actions.Store.Groups.Any(g => g.Col is not null) || _actions.Store.Items.Any(i => i.Col is not null))
        {
            var flow = new MenuItem { Header = "全部自动排列", Tag = FindResource("Icon.Groups") };
            flow.Click += (_, _) => _actions.Unpin(null);
            menu.Items.Add(flow);
        }
        menu.IsOpen = true;
    }

    // Right-click on the bar's empty part: window size and where the panel opens.
    private void OpenWindowMenu()
    {
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        bool max = WindowState == WindowState.Maximized;
        var size = new MenuItem { Header = max ? "还原" : "最大化", Tag = FindResource(max ? "Icon.Restore" : "Icon.Max") };
        size.Click += (_, _) => Controls.WindowDrag.ToggleMaximize(this);
        var reset = new MenuItem { Header = "恢复默认大小", Tag = FindResource("Icon.Reset") };
        reset.Click += (_, _) =>
        {
            var work = SystemParameters.WorkArea;
            _memory.Reset(Place(work, firstTime: true).Size, rise: 0.4);
        };
        menu.Items.Add(size);
        menu.Items.Add(reset);
        menu.Items.Add(new Separator());
        var where = new MenuItem { Header = "打开位置", Tag = FindResource("Icon.Window") };
        foreach (var (key, text) in new[] { ("Last", "上次位置"), ("Center", "屏幕中央"), ("Cursor", "鼠标处") })
        {
            var mi = new MenuItem { Header = text, IsCheckable = true, IsChecked = _actions.Settings.QuickPlacement == key };
            mi.Click += (_, _) => _actions.Settings.QuickPlacement = key;
            where.Items.Add(mi);
        }
        menu.Items.Add(where);
        menu.IsOpen = true;
    }

    // The theme's corner grip (as on group cards) in the bottom-right corner: shows that the panel can be
    // resized and starts the system resize from there.
    private FrameworkElement ResizeGrip()
    {
        var grip = new Thumb
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -14, -14),
            Opacity = 0.55,
        };
        grip.SetResourceReference(StyleProperty, "CornerGrip");
        grip.MouseEnter += (_, _) => grip.Opacity = 1;
        grip.MouseLeave += (_, _) => grip.Opacity = 0.55;
        grip.PreviewMouseLeftButtonDown += (_, e) =>
        {
            Platform.Native.BeginCornerResize(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            e.Handled = true;
        };
        return grip;
    }

    private void OpenAddMenu(FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        var f = new MenuItem { Header = "添加文件", Tag = FindResource("Icon.File") };
        f.Click += (_, _) => AddFiles();
        var d = new MenuItem { Header = "添加文件夹", Tag = FindResource("Icon.Folder") };
        d.Click += (_, _) => AddFolder();
        var st = new MenuItem { Header = "从「开始」导入", Tag = FindResource("Icon.Start") };
        st.Click += (_, _) => ImportStart?.Invoke();
        menu.Items.Add(f);
        menu.Items.Add(d);
        menu.Items.Add(st);
        menu.Items.Add(ImportMenu());
        menu.IsOpen = true;
    }

    // Bookmark files (iTab backups, exported bookmarks.html, JSON backups) and the browsers' own bookmarks.
    private MenuItem ImportMenu()
    {
        var sub = new MenuItem { Header = "导入书签", Tag = FindResource("Icon.Globe") };
        var file = new MenuItem { Header = "从文件…", Tag = FindResource("Icon.File") };
        file.Click += (_, _) => ImportFile();
        sub.Items.Add(file);
        foreach (var (name, path) in BrowserBookmarks())
        {
            var mi = new MenuItem { Header = name };
            mi.Click += (_, _) => _ = _actions.Importer.RunAsync(path);
            sub.Items.Add(mi);
        }
        return sub;
    }

    private static System.Collections.Generic.IEnumerable<(string Name, string Path)> BrowserBookmarks()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var (name, dir) in new[] { ("Edge", @"Microsoft\Edge"), ("Chrome", @"Google\Chrome"), ("Brave", @"BraveSoftware\Brave-Browser"), ("Vivaldi", "Vivaldi") })
        {
            string f = System.IO.Path.Combine(local, dir, "User Data", "Default", "Bookmarks");
            if (System.IO.File.Exists(f)) yield return (name, f);
        }
    }

    private void ImportFile()
    {
        var dlg = new Win32.OpenFileDialog
        {
            Filter = "书签文件|*.itabdata;*.html;*.htm;*.json;Bookmarks|所有文件|*.*",
            DereferenceLinks = true,
        };
        KeepOpen = true;
        try { if (dlg.ShowDialog(this) == true) _ = _actions.Importer.RunAsync(dlg.FileName); }
        finally { KeepOpen = false; Activate(); }
    }

    private void AddFiles()
    {
        var dlg = new Win32.OpenFileDialog { Multiselect = true, DereferenceLinks = false };
        KeepOpen = true;
        try { if (dlg.ShowDialog(this) == true) _actions.Add(dlg.FileNames); }
        finally { KeepOpen = false; Activate(); }
    }

    private void AddFolder()
    {
        var dlg = new Win32.OpenFolderDialog { Multiselect = true };
        KeepOpen = true;
        try { if (dlg.ShowDialog(this) == true) _actions.Add(dlg.FolderNames); }
        finally { KeepOpen = false; Activate(); }
    }

    private ToggleButton Toggle(string icon, string tip)
    {
        var t = new ToggleButton { Tag = FindResource(icon), ToolTip = tip };
        t.SetResourceReference(StyleProperty, "IconToggle");
        return t;
    }

    private Button IconButton(string icon, string tip)
    {
        var b = new Button { Tag = FindResource(icon), ToolTip = tip };
        b.SetResourceReference(StyleProperty, "IconButton");
        return b;
    }
}
