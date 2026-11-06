using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using ZoneQuanta.Core.Update;
using ZoneQuanta.Platform;
using ZoneQuanta.UI.Panel.Pages;

namespace ZoneQuanta.UI.Panel;

public partial class PanelWindow : Window
{
    private sealed record PageDef(string Title, string Icon, string About, Func<IPanelHost, UserControl> Create);

    // About: shown when the pointer rests on the nav item (the only place a page explains itself).
    private static readonly PageDef[] Defs =
    {
        new("控制台", "Icon.Grid", "常用开关、缩放与不透明度", h => new ConsolePage(h)),
        new("显示", "Icon.Monitor", "时间格式、主题与横竖排列", h => new DisplayPage(h)),
        new("时区", "Icon.Globe", "添加、改名、排序时区", h => new ZonesPage(h)),
        new("监控", "Icon.Pulse", "任务栏监控条、网速单位与刷新频率", h => new MonitorPage(h)),
        new("流量", "Icon.Chart", "总流量、额度预测与各应用流量", h => new TrafficPage(h)),
        new("快捷", "Icon.Bolt", "快捷面板的内容、显示与打开方式", h => new QuickPage(h)),
        new("快捷键", "Icon.Keyboard", "全局快捷键与面板内快捷键", h => new KeysPage(h)),
        new("窗口", "Icon.Move", "时钟坐标、微调与快速定位", h => new WindowPage(h)),
        new("系统", "Icon.Sliders", "开机自启、置顶、穿透、历史版本与退出", h => new SystemPage(h)),
        new("更新", "Icon.Download", "检查、下载并安装新版本", h => new UpdatePage(h)),
    };
    private const double NavWide = 212, NavNarrow = 64;

    private readonly IPanelHost _host;
    private readonly Dictionary<string, UserControl> _pages = new();
    private readonly Thickness _resizeBorder;
    private readonly Controls.WindowMemory _memory;
    private MaximizeFrame? _maximizeFrame;
    private bool _maximized; // survives a trip through Minimized, where WindowState no longer says

    public PanelWindow(IPanelHost host)
    {
        _host = host;
        InitializeComponent();
        _resizeBorder = WindowChrome.GetWindowChrome(this).ResizeBorderThickness;

        // Opens where it was left: size, spot and maximized state survive restarts and updates.
        _memory = new Controls.WindowMemory(this, () => _host.Settings.PanelBounds, b => _host.Settings.PanelBounds = b);
        _memory.Place(Controls.WindowMemory.Spot.Last, new Size(Width, Height), onCursorScreen: false);
        if (Content is Grid root)
        {
            var badge = new Controls.SizeBadge();
            Grid.SetColumnSpan(badge, 2);
            root.Children.Add(badge);
            badge.Attach(_memory);
        }
        VersionText.Text = $"v{UpdateService.CurrentVersion}";
        SetCompact(_host.Settings.NavCollapsed, animate: false);
        CollapseButton.Click += (_, _) => ToggleNav();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.B && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                ToggleNav();
                e.Handled = true;
            }
        };

        BuildNav();
        // Reopens on the page it was left on (settings.json PanelPage), else the first.
        (Nav.Children.OfType<RadioButton>().FirstOrDefault(r => (string)r.Content == _host.Settings.PanelPage)
            ?? (RadioButton)Nav.Children[0]).IsChecked = true;
        var reorder = new NavReorder(Nav, order => _host.Settings.NavOrder = order.ToList());
        PreviewKeyDown += reorder.OnWindowKey;
        // Nav items move themselves; everything else on the sidebar (logo, name, gaps, version) is title bar:
        // drag to move the window, double-click to maximize.
        Controls.WindowDrag.Attach(Sidebar, this, e => IsSidebarBlank(e.OriginalSource as DependencyObject)
            ? Controls.WindowDrag.Grip.Blank : Controls.WindowDrag.Grip.None);
        var reset = new MenuItem { Header = "恢复默认顺序", Tag = TryFindResource("Icon.Reset") };
        reset.Click += (_, _) => { _host.Settings.NavOrder = new(); BuildNav(); };
        Nav.ContextMenu = new ContextMenu();
        Nav.ContextMenu.Items.Add(reset);
        Nav.ContextMenuOpening += (_, _) => reset.IsEnabled = _host.Settings.NavOrder.Count > 0;

        // Page content: drag to move the window, hold to pick a card up and put it elsewhere.
        Controls.Arrange.Settings = _host.Settings;
        _ = new Controls.ContentDrag(PageArea, this);
        var resetLayout = new MenuItem { Header = "恢复默认布局", Tag = TryFindResource("Icon.Reset") };
        resetLayout.Click += (_, _) =>
        {
            foreach (var p in Controls.Arrange.PanelsIn(PageHost)) Controls.Arrange.Reset(p);
        };
        PageArea.ContextMenu = new ContextMenu();
        PageArea.ContextMenu.Items.Add(resetLayout);
        PageArea.ContextMenuOpening += (_, e) =>
        {
            var panels = Controls.Arrange.PanelsIn(PageHost);
            if (panels.Count == 0) e.Handled = true;
            else resetLayout.IsEnabled = panels.Exists(Controls.Arrange.HasSaved);
        };

        MinButton.Click += (_, _) => HidePanel();
        MaxButton.Click += (_, _) => ToggleMaximize();
        CloseButton.Click += (_, _) => HidePanel();
        StateChanged += (_, _) => OnWindowStateChanged();
        SourceInitialized += (_, _) => OnSourceInitialized();
        Closing += OnClosing;
        OnWindowStateChanged();
    }

    private WindowState RestoredState => _maximized ? WindowState.Maximized : WindowState.Normal;

    private bool IsSidebarBlank(DependencyObject? d)
    {
        if (d == Sidebar || d == Nav || d is TextBlock { Name: "VersionText" }) return true;
        for (; d is not null && d != Sidebar; d = VisualTreeHelper.GetParent(d))
            if (d == Brand) return true;
        return false;
    }

    // Saved order first (unknown titles skipped), then any page it does not mention, in built-in order.
    private void BuildNav()
    {
        string? current = (Nav.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true))?.Content as string;
        var order = _host.Settings.NavOrder;
        var defs = order.Select(t => Array.Find(Defs, d => d.Title == t)).OfType<PageDef>().Distinct()
            .Concat(Defs.Where(d => !order.Contains(d.Title))).ToList();
        Nav.Children.Clear();
        foreach (var def in defs)
        {
            var item = new RadioButton
            {
                Content = def.Title,
                Style = (Style)FindResource("NavItem"),
                Tag = FindResource(def.Icon),
                GroupName = "nav",
                ToolTip = NavTip(def),
            };
            ToolTipService.SetPlacement(item, System.Windows.Controls.Primitives.PlacementMode.Right);
            ToolTipService.SetInitialShowDelay(item, 250);
            item.Checked += (_, _) => Navigate(def);
            Nav.Children.Add(item);
            if (def.Title == current) item.IsChecked = true;
        }
    }

    private static UIElement NavTip(PageDef def)
    {
        var panel = new StackPanel { Margin = new Thickness(2, 1, 2, 1) };
        panel.Children.Add(new TextBlock { Text = def.Title, FontSize = 13, FontWeight = FontWeights.SemiBold });
        var about = new TextBlock { Text = def.About, FontSize = 12, Margin = new Thickness(0, 2, 0, 0) };
        about.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        panel.Children.Add(about);
        return panel;
    }

    private void ToggleNav()
    {
        _host.Settings.NavCollapsed = !_host.Settings.NavCollapsed;
        SetCompact(_host.Settings.NavCollapsed, animate: true);
    }

    // Icons only (64 px) or icons with names (212 px); the width slides, names fade with it.
    private void SetCompact(bool compact, bool animate)
    {
        Controls.SideNav.SetCompact(Sidebar, compact);
        BrandText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        VersionText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Brand.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        Brand.Margin = compact ? new Thickness(0, 0, 0, 6) : new Thickness(6, 0, 0, 6);
        CollapseButton.Tag = FindResource(compact ? "Icon.Right" : "Icon.Left");
        CollapseButton.ToolTip = compact ? "展开导航（Ctrl+B）" : "收起导航（Ctrl+B）";
        double to = compact ? NavNarrow : NavWide;
        if (!animate)
        {
            Sidebar.BeginAnimation(WidthProperty, null);
            Sidebar.Width = to;
            return;
        }
        Sidebar.BeginAnimation(WidthProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        });
        Core.Log.Write($"nav {(compact ? "collapsed" : "expanded")}");
    }

    private void OnSourceInitialized()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.RoundCorners(hwnd);
        _maximizeFrame = new MaximizeFrame(HwndSource.FromHwnd(hwnd), MaxButton);
        _maximizeFrame.HoverChanged += HighlightMaxButton;
        _maximizeFrame.Clicked += ToggleMaximize;
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnWindowStateChanged()
    {
        if (WindowState != WindowState.Minimized)
        {
            _maximized = WindowState == WindowState.Maximized;
            MaxButton.Tag = FindResource(_maximized ? "Icon.Restore" : "Icon.Max");
            MaxButton.ToolTip = _maximized ? "还原" : "最大化";
            // A maximized window has no edge to drag: its top pixels must remain caption.
            WindowChrome.GetWindowChrome(this).ResizeBorderThickness = _maximized ? new Thickness(0) : _resizeBorder;
        }
        Revive();
    }

    // Over this button the mouse is in the non-client area, so WPF never reports IsMouseOver.
    private void HighlightMaxButton(bool on)
    {
        if (on)
        {
            MaxButton.SetResourceReference(BackgroundProperty, "RaisedBrush");
            MaxButton.SetResourceReference(ForegroundProperty, "TextBrush");
        }
        else
        {
            MaxButton.ClearValue(BackgroundProperty);
            MaxButton.ClearValue(ForegroundProperty);
        }
    }

    public void ShowPanel()
    {
        if (WindowState == WindowState.Minimized) WindowState = RestoredState;
        ShowInTaskbar = true;
        Show();
        Activate();
    }

    public void Revive()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !IsVisible || !Native.IsIconic(hwnd)) return;
        // SW_SHOWNOACTIVATE always lands on the normal size, so it cannot bring a maximized panel back.
        Native.ShowWindow(hwnd, _maximized ? Native.SW_SHOWMAXIMIZED : Native.SW_SHOWNOACTIVATE);
    }

    public void HidePanel()
    {
        ShowInTaskbar = false;
        if (WindowState == WindowState.Minimized) WindowState = RestoredState;
        Hide();
    }

    public void TogglePanel()
    {
        if (IsVisible) HidePanel();
        else ShowPanel();
    }

    public void SelectPage(string title)
    {
        foreach (RadioButton r in Nav.Children)
            if ((string)r.Content == title) r.IsChecked = true;
    }

    private void Navigate(PageDef def)
    {
        _host.Settings.PanelPage = def.Title;
        if (!_pages.TryGetValue(def.Title, out var page))
            _pages[def.Title] = page = def.Create(_host);
        PageHost.Content = page;

        var slide = new TranslateTransform(0, 14);
        PageHost.RenderTransform = slide;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (Application.Current.ShutdownMode == ShutdownMode.OnExplicitShutdown && !App.Exiting)
        {
            e.Cancel = true;
            HidePanel();
        }
    }
}
