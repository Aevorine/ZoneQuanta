using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using ZoneQuanta.Core.Update;
using ZoneQuanta.Platform;
using ZoneQuanta.UI.Panel.Pages;

namespace ZoneQuanta.UI.Panel;

public partial class PanelWindow : Window
{
    private sealed record PageDef(string Title, string Icon, Func<IPanelHost, UserControl> Create);

    private static readonly PageDef[] Defs =
    {
        new("控制台", "Icon.Grid", h => new ConsolePage(h)),
        new("显示", "Icon.Monitor", h => new DisplayPage(h)),
        new("窗口", "Icon.Move", h => new WindowPage(h)),
        new("系统", "Icon.Sliders", h => new SystemPage(h)),
        new("更新", "Icon.Download", h => new UpdatePage(h)),
    };

    private readonly IPanelHost _host;
    private readonly Dictionary<string, UserControl> _pages = new();

    public PanelWindow(IPanelHost host)
    {
        _host = host;
        InitializeComponent();

        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width - 24);
        Height = Math.Min(Height, area.Height - 24);
        MinWidth = Math.Min(MinWidth, Width);
        MinHeight = Math.Min(MinHeight, Height);
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
        VersionText.Text = $"v{UpdateService.CurrentVersion}";

        foreach (var def in Defs)
        {
            var item = new RadioButton
            {
                Content = def.Title,
                Style = (Style)FindResource("NavItem"),
                Tag = FindResource(def.Icon),
                GroupName = "nav",
            };
            item.Checked += (_, _) => Navigate(def);
            Nav.Children.Add(item);
        }
        ((RadioButton)Nav.Children[0]).IsChecked = true;

        MinButton.Click += (_, _) => HidePanel();
        CloseButton.Click += (_, _) => HidePanel();
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) HidePanel(); };
        SourceInitialized += (_, _) => Native.RoundCorners(new WindowInteropHelper(this).Handle);
        Closing += OnClosing;
    }

    public void ShowPanel()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        Show();
        Activate();
    }

    public void HidePanel()
    {
        ShowInTaskbar = false;
        WindowState = WindowState.Normal;
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
        if (!_pages.TryGetValue(def.Title, out var page))
            _pages[def.Title] = page = def.Create(_host);
        PageTitle.Text = def.Title;
        PageHost.Content = page;
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
