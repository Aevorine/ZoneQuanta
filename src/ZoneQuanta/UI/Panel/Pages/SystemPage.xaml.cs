using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ZoneQuanta.Core;
using ZoneQuanta.Platform;

namespace ZoneQuanta.UI.Panel.Pages;

public partial class SystemPage : UserControl
{
    private readonly IPanelHost _host;

    public SystemPage(IPanelHost host)
    {
        _host = host;
        InitializeComponent();
        DataContext = host.Settings;

        ShowHotkey();
        HotkeyBox.PreviewKeyDown += OnKey;
        Controls.HotkeyBox.Guard(HotkeyBox, () => HotkeyBox.Text = "按下新的快捷键…", ShowHotkey);
        FolderButton.Click += (_, _) => host.OpenSettingsFolder();
        ExitButton.Click += (_, _) => host.ExitApp();

        SnapshotButton.Click += async (_, _) =>
        {
            await Task.Run(() => DataHistory.Take("手动"));
            LoadHistory();
        };
        HistoryFolder.Click += (_, _) =>
        {
            string dir = DataPaths.File("history");
            System.IO.Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        };
        IsVisibleChanged += (_, _) => { if (IsVisible) LoadHistory(); };
        // A version taken while the page is open shows up at once.
        void OnTaken() => Dispatcher.BeginInvoke(() => { if (IsVisible) LoadHistory(); });
        Loaded += (_, _) =>
        {
            DataHistory.Taken -= OnTaken; // Loaded can repeat without Unloaded
            DataHistory.Taken += OnTaken;
        };
        Unloaded += (_, _) => DataHistory.Taken -= OnTaken;
    }

    private async void LoadHistory()
    {
        var (list, current) = await Task.Run(DataHistory.List);
        History.Children.Clear();
        // "Current" is the version equal to the files on disk; any other, newest included, can be restored.
        foreach (var s in list) History.Children.Add(HistoryRow(s, current: s.Hash.Length > 0 && s.Hash == current));
    }

    // One version: when, why it was kept, what changed since the one before, and going back to it.
    private UIElement HistoryRow(Snapshot s, bool current)
    {
        var g = new Grid { MinHeight = 38 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var when = new TextBlock { Text = When(s.Time), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        when.SetResourceReference(TextBlock.ForegroundProperty, current ? "Accent1Brush" : "TextBrush");
        System.Windows.Documents.Typography.SetNumeralAlignment(when, FontNumeralAlignment.Tabular);
        var why = new TextBlock { Text = s.Reason, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        why.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var what = new TextBlock { Text = s.Summary, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = s.Summary, Margin = new Thickness(0, 0, 8, 0) };
        what.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Grid.SetColumn(why, 1);
        Grid.SetColumn(what, 2);
        g.Children.Add(when);
        g.Children.Add(why);
        g.Children.Add(what);
        if (current) return g;

        var restore = new Button { ToolTip = "恢复到这个版本" };
        restore.SetResourceReference(StyleProperty, "IconButton");
        restore.SetResourceReference(TagProperty, "Icon.Reset");
        var confirm = new Button { Content = "确认恢复", Visibility = Visibility.Collapsed, MinWidth = 96 };
        confirm.SetResourceReference(StyleProperty, "Primary");
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            confirm.Visibility = Visibility.Collapsed;
            restore.Visibility = Visibility.Visible;
        };
        restore.Click += (_, _) =>
        {
            restore.Visibility = Visibility.Collapsed;
            confirm.Visibility = Visibility.Visible;
            timer.Start();
        };
        confirm.Click += (_, _) =>
        {
            timer.Stop();
            confirm.IsEnabled = false;
            _host.RestoreSnapshot(s);
        };
        var buttons = new Grid();
        buttons.Children.Add(restore);
        buttons.Children.Add(confirm);
        Grid.SetColumn(buttons, 3);
        g.Children.Add(buttons);
        return g;
    }

    private static string When(DateTime t)
    {
        var today = DateTime.Today;
        string day = t.Date == today ? "今天" : t.Date == today.AddDays(-1) ? "昨天" : t.ToString("M-d", CultureInfo.InvariantCulture);
        return $"{day} {t:HH:mm:ss}";
    }

    public static string Format(uint modifiers, uint vk)
    {
        var parts = new List<string>();
        if ((modifiers & Native.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & Native.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & Native.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & Native.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(KeyInterop.KeyFromVirtualKey((int)vk).ToString());
        return string.Join(" + ", parts);
    }

    private void ShowHotkey() => HotkeyBox.Text = Format(_host.Settings.HotkeyModifiers, _host.Settings.HotkeyKey);

    private void OnKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
            return;
        if (key == Key.Escape)
        {
            Controls.HotkeyBox.Release(HotkeyBox);
            return;
        }

        uint mods = 0;
        var m = Keyboard.Modifiers;
        if (m.HasFlag(ModifierKeys.Control)) mods |= Native.MOD_CONTROL;
        if (m.HasFlag(ModifierKeys.Alt)) mods |= Native.MOD_ALT;
        if (m.HasFlag(ModifierKeys.Shift)) mods |= Native.MOD_SHIFT;
        if (m.HasFlag(ModifierKeys.Windows)) mods |= Native.MOD_WIN;
        if (mods == 0) return;

        _host.Settings.HotkeyModifiers = mods;
        _host.Settings.HotkeyKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        Controls.HotkeyBox.Release(HotkeyBox);
        ShowHotkey();
    }
}
