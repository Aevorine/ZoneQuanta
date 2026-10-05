using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ZoneQuanta.Core.Monitor;
using ZoneQuanta.Core.Settings;
using ZoneQuanta.Platform;

namespace ZoneQuanta.UI.Band;

public sealed class BandWindow : Window
{
    private static readonly Brush Warn = Frozen("#E2AE74"), Danger = Frozen("#E27D7D");

    private sealed record Chip(string Key, string Label, string Accent, Func<AppSettings, bool> Enabled)
    {
        public StackPanel Panel { get; } = new() { Margin = new Thickness(7, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        public TextBlock Value { get; } = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, Style = null };
        public Brush? Tint { get; set; }
        public bool TintSet { get; set; }
    }

    private readonly AppSettings _settings;
    private readonly List<Chip> _chips;
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private IntPtr _hwnd;
    private Rect _start = Rect.Empty;
    private long _startTriedAt = -60000;
    private int _ticks;
    private bool _suppressed;
    private Metrics _last;
    private long _todayBytes;
    private readonly TaskbarTracker _taskbar = new();
    private bool _repositioning;
    private readonly DispatcherTimer _hover = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private int _inside, _outside;
    private bool _hovering;

    public event Action<bool>? HoverChanged;
    public event Action? SettingsRequested;

    public Rect BoundsPx => _hwnd != IntPtr.Zero && Native.GetWindowRect(_hwnd, out var r)
        ? new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top)
        : Rect.Empty;

    public BandWindow(AppSettings settings)
    {
        _settings = settings;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.Width;
        Focusable = false;
        Title = "ZoneQuanta Band";

        _chips = new List<Chip>
        {
            new("up", "上传", "Accent2Brush", s => s.BandUp),
            new("down", "下载", "Accent1Brush", s => s.BandDown),
            new("today", "今日流量", "TextBrush", s => s.BandToday),
            new("total", "总速", "TextBrush", s => s.BandTotal),
            new("mem", "内存", "TextBrush", s => s.BandMem),
            new("cpu", "CPU", "TextBrush", s => s.BandCpu),
        };
        foreach (var c in _chips)
        {
            var label = new TextBlock { Text = c.Label, FontSize = 10, Style = null };
            label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            c.Value.SetResourceReference(TextBlock.ForegroundProperty, c.Accent);
            c.Panel.Children.Add(label);
            c.Panel.Children.Add(c.Value);
            _row.Children.Add(c.Panel);
        }

        var pill = new Border { Padding = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Stretch, Child = _row };
        System.Windows.Documents.TextElement.SetFontFamily(pill, (FontFamily)Application.Current.FindResource("AppFont"));
        Content = new Grid { Children = { pill } };

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.SetExStyle(_hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE, true);
        };
        _taskbar.Changed += () => Reposition();
        Closed += (_, _) =>
        {
            _taskbar.Dispose();
            _hover.Stop();
            _settings.PropertyChanged -= OnSettingChanged;
        };
        SizeChanged += (_, _) => Reposition();
        MouseRightButtonUp += (_, e) => { e.Handled = true; SettingsRequested?.Invoke(); };
        StateChanged += (_, _) => Revive();
        _settings.PropertyChanged += OnSettingChanged;
        _hover.Tick += (_, _) => PollHover();
        ApplyChipVisibility();
    }

    public void SetSuppressed(bool suppressed)
    {
        _suppressed = suppressed;
        Sync();
    }

    public void Sync()
    {
        bool show = _settings.BandVisible && !_suppressed && AnyChip();
        if (show && !IsVisible)
        {
            Show();
            _taskbar.Start();
            Reposition();
            _hover.Start();
        }
        else if (!show && IsVisible)
        {
            Hide();
            _taskbar.Stop();
            _hover.Stop();
            SetHovering(false);
        }
    }

    public void Update(Metrics m, long? todayBytes = null)
    {
        _last = m;
        if (todayBytes.HasValue) _todayBytes = todayBytes.Value;
        if (!IsVisible) return;
        bool bits = _settings.SpeedBits;
        string unit = _settings.SpeedUnit;
        Set("up", UnitFormat.Speed(m.UpBps, bits, unit), null);
        Set("down", UnitFormat.Speed(m.DownBps, bits, unit), null);
        Set("today", UnitFormat.Size(_todayBytes, false, "Auto"), null);
        Set("total", UnitFormat.Speed(m.UpBps + m.DownBps, bits, unit), null);
        Set("mem", $"{m.Mem:0}%", Load(m.Mem));
        Set("cpu", $"{m.Cpu:0}%", Load(m.Cpu));

        if (++_ticks % 30 == 0) Reposition(refreshStart: true);
        else if (_ticks % 6 == 0) KeepOnTop();
    }

    private void PollHover()
    {
        bool over = false;
        if (_hwnd != IntPtr.Zero && IsVisible && Native.GetCursorPos(out var p) && Native.GetWindowRect(_hwnd, out var r))
            over = p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;

        if (over) { _outside = 0; if (++_inside >= 1) SetHovering(true); }
        else { _inside = 0; if (++_outside >= 2) SetHovering(false); }
    }

    private void SetHovering(bool on)
    {
        if (_hovering == on) return;
        _hovering = on;
        HoverChanged?.Invoke(on);
    }

    private static Brush? Load(double percent) => percent >= 90 ? Danger : percent >= 70 ? Warn : null;

    private void Set(string key, string text, Brush? tint)
    {
        var chip = _chips.Find(c => c.Key == key)!;
        if (chip.Value.Text != text) chip.Value.Text = text;
        if (chip.TintSet && ReferenceEquals(chip.Tint, tint)) return;
        chip.Tint = tint;
        chip.TintSet = true;
        if (tint is not null) chip.Value.Foreground = tint;
        else chip.Value.SetResourceReference(TextBlock.ForegroundProperty, chip.Accent);
    }

    private bool AnyChip() => _chips.Exists(c => c.Enabled(_settings));

    private void ApplyChipVisibility()
    {
        foreach (var c in _chips) c.Panel.Visibility = c.Enabled(_settings) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.BandVisible):
            case nameof(AppSettings.BandUp):
            case nameof(AppSettings.BandDown):
            case nameof(AppSettings.BandToday):
            case nameof(AppSettings.BandTotal):
            case nameof(AppSettings.BandMem):
            case nameof(AppSettings.BandCpu):
                ApplyChipVisibility();
                Sync();
                break;
            case nameof(AppSettings.BandOffset):
            case nameof(AppSettings.BandPosition):
                Reposition(refreshStart: true);
                break;
            case nameof(AppSettings.SpeedBits):
            case nameof(AppSettings.SpeedUnit):
                Update(_last);
                break;
        }
    }

    public void Revive()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible || !Native.IsIconic(_hwnd)) return;
        Native.ShowWindow(_hwnd, Native.SW_SHOWNOACTIVATE);
        KeepOnTop();
    }

    private void KeepOnTop()
    {
        if (_hwnd != IntPtr.Zero)
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    private double _fontFor;

    private void ApplyFonts(double heightDip)
    {
        if (Math.Abs(_fontFor - heightDip) < 0.5) return;
        _fontFor = heightDip;
        double value = Math.Clamp(heightDip * 0.40, 12, 24);
        double label = Math.Clamp(heightDip * 0.24, 9, 14);
        foreach (var c in _chips)
        {
            c.Value.FontSize = value;
            ((TextBlock)c.Panel.Children[0]).FontSize = label;
            c.Panel.MinWidth = c.Key is "mem" or "cpu" ? value * 2.9 : value * 5.4;
        }
    }

    private void Reposition(bool refreshStart = false)
    {
        if (_hwnd == IntPtr.Zero || !IsVisible || _repositioning) return;
        IntPtr tray = _taskbar.Handle;
        if (tray == IntPtr.Zero || !Native.GetWindowRect(tray, out var t))
        {
            Native.ShowWindow(_hwnd, Native.SW_HIDE);
            SetHovering(false);
            return;
        }
        _repositioning = true;
        try
        {

        var dpi = VisualTreeHelper.GetDpi(this);
        double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;
        double trayW = t.Right - t.Left, trayH = t.Bottom - t.Top;
        if (trayH > trayW) trayH = Math.Min(trayH, 48 * sy);

        ApplyFonts(trayH / sy);
        if (_settings.BandPosition == "Start" && (refreshStart || (_start.IsEmpty && Environment.TickCount64 - _startTriedAt > 30000)))
        {
            _start = FindStartButton(tray);
            _startTriedAt = Environment.TickCount64;
        }

        double widthPx = ActualWidth * sx;
        double gap = 6 * sx;
        double x = t.Left;
        switch (_settings.BandPosition)
        {
            case "Start":
                if (!_start.IsEmpty && _start.Left - widthPx - gap > t.Left) x = _start.Left - widthPx - gap;
                break;
            case "Right":
                IntPtr notify = Native.FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
                x = notify != IntPtr.Zero && Native.GetWindowRect(notify, out var n) ? n.Left - widthPx - gap : t.Right - widthPx - 220 * sx;
                break;
        }
        x += _settings.BandOffset * sx;

        Height = trayH / sy;
        Left = x / sx;
        Top = t.Top / sy;
        // Clip to the taskbar monitor so the sliding band cannot spill onto
        // another display or remain visible over the auto-hide reveal strip.
        var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        if (Native.GetMonitorInfo(Native.MonitorFromWindow(tray, 2), ref info))
        {
            int left = Math.Max(t.Left, info.rcMonitor.Left);
            int top = Math.Max(t.Top, info.rcMonitor.Top);
            int right = Math.Min(t.Right, info.rcMonitor.Right);
            int bottom = Math.Min(t.Bottom, info.rcMonitor.Bottom);
            bool visible = Native.IsWindowVisible(tray) && right - left > 2 && bottom - top > 2;
            if (visible)
            {
                IntPtr region = Native.CreateRectRgn((int)Math.Max(0, left - x), top - t.Top,
                    (int)Math.Min(widthPx, right - x), bottom - t.Top);
                if (Native.SetWindowRgn(_hwnd, region, true) == 0) Native.DeleteObject(region);
                Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, (int)Math.Round(x), t.Top,
                    (int)Math.Ceiling(widthPx), (int)trayH, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            }
            else
            {
                Native.ShowWindow(_hwnd, Native.SW_HIDE);
                SetHovering(false);
            }
        }
        }
        finally { _repositioning = false; }
    }

    private static Rect FindStartButton(IntPtr tray)
    {
        try
        {
            var root = AutomationElement.FromHandle(tray);
            var el = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "StartButton"))
                     ?? root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "开始"))
                     ?? root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Start"));
            if (el is not null)
            {
                var r = el.Current.BoundingRectangle;
                if (!r.IsEmpty && r.Width > 0) return r;
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        return Rect.Empty;
    }

    private static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
