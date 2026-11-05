using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
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

    public QuickWindow(QuickActions actions, Action openSettings) : base("ZoneQuanta Quick")
    {
        _actions = actions;
        _openSettings = openSettings;
        _grid = new QuickGrid(actions, singleClickOpen: true) { Margin = new Thickness(-2, 12, -2, 0) };
        _search.SetResourceReference(StyleProperty, "IconField");
        _search.Tag = FindResource("Icon.Search");
        MainInput = _search;

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

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (var b in new UIElement[] { _groups, _tiles, _list, sort, add, settings }) tools.Children.Add(b);

        var bar = new Grid { Background = Brushes.Transparent };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.Children.Add(_search);
        Grid.SetColumn(tools, 1);
        bar.Children.Add(tools);
        // Gaps move the window at once; the search field and buttons after a short hold.
        Controls.WindowDrag.Attach(bar, this, source => source == bar || source == tools);

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
        SetRoot(root);
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
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(SizeMoveHook);
    }

    public Action? ImportStart { get; set; }

    // Large by default; a size the user drags to is remembered across restarts.
    protected override Rect Place(Rect work, bool firstTime)
    {
        var s = _actions.Settings;
        double w = s.QuickWidth > 0 ? s.QuickWidth : Math.Clamp(work.Width * 0.64, 640, 1320);
        double h = s.QuickHeight > 0 ? s.QuickHeight : Math.Clamp(work.Height * 0.78, 460, 980);
        w = Math.Min(w, work.Width - 16);
        h = Math.Min(h, work.Height - 16);
        return new Rect(work.Left + (work.Width - w) / 2, work.Top + (work.Height - h) * 0.4, w, h);
    }

    // Only a size the user drags to is remembered; the computed default keeps following the monitor.
    private Size _sizeAtGrab;

    private IntPtr SizeMoveHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int EnterSizeMove = 0x0231, ExitSizeMove = 0x0232;
        if (msg == EnterSizeMove) _sizeAtGrab = new Size(ActualWidth, ActualHeight);
        else if (msg == ExitSizeMove && (Math.Abs(ActualWidth - _sizeAtGrab.Width) > 1 || Math.Abs(ActualHeight - _sizeAtGrab.Height) > 1))
        {
            _actions.Settings.QuickWidth = Math.Round(ActualWidth);
            _actions.Settings.QuickHeight = Math.Round(ActualHeight);
        }
        return IntPtr.Zero;
    }

    protected override void OnPresented()
    {
        _search.Focus();
        _search.SelectAll();
        SyncEmpty();
        _grid.ScrollHome();
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
        menu.IsOpen = true;
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
        menu.IsOpen = true;
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
