using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Threading.Tasks;
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
        public StackPanel Panel { get; } = new() { Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
        public TextBlock Value { get; } = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, Style = null };
        public Brush? Tint { get; set; }
        public bool TintSet { get; set; }
    }

    private readonly AppSettings _settings;
    private readonly List<Chip> _chips;
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private IntPtr _hwnd;
    private TaskbarLayout? _layout;
    private bool _readingLayout, _canPaint;
    private int _layoutGeneration;
    private readonly Border _pill;
    private double _fitWidth = -1, _fitHeight = -1;
    private int _contentRevision, _fitRevision = -1;
    private bool _fits;
    private readonly DispatcherTimer _layoutTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
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
        SizeToContent = SizeToContent.Manual;
        Width = 1;
        Height = 1;
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
            var label = new TextBlock { Text = c.Label, FontSize = 10, Style = null, HorizontalAlignment = HorizontalAlignment.Center };
            c.Value.HorizontalAlignment = HorizontalAlignment.Center;
            label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            c.Value.SetResourceReference(TextBlock.ForegroundProperty, c.Accent);
            c.Panel.Children.Add(label);
            c.Panel.Children.Add(c.Value);
            _row.Children.Add(c.Panel);
        }

        _pill = new Border { Padding = new Thickness(1, 0, 1, 0), VerticalAlignment = VerticalAlignment.Stretch, Child = _row };
        System.Windows.Documents.TextElement.SetFontFamily(_pill, (FontFamily)Application.Current.FindResource("AppFont"));
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Content = _pill;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.SetExStyle(_hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE, true);
            HideNative();
        };
        _taskbar.Changed += () => Reposition();
        _taskbar.LayoutChanged += () =>
        {
            ++_layoutGeneration;
            _layout = null;
            HideNative();
            RefreshLayout();
        };
        _layoutTimer.Tick += (_, _) => RefreshLayout();
        Closed += (_, _) =>
        {
            _taskbar.Dispose();
            _hover.Stop();
            _layoutTimer.Stop();
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
            _layoutTimer.Start();
            RefreshLayout();
            Reposition();
            _hover.Start();
        }
        else if (!show && IsVisible)
        {
            Hide();
            _taskbar.Stop();
            _layoutTimer.Stop();
            ++_layoutGeneration;
            _layout = null;
            _canPaint = false;
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

        Reposition();
        if (++_ticks % 6 == 0 && _canPaint) KeepOnTop();
    }

    private void PollHover()
    {
        bool over = false;
        if (_hwnd != IntPtr.Zero && IsVisible && _canPaint && Native.GetCursorPos(out var p) && Native.GetWindowRect(_hwnd, out var r))
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
        if (chip.Value.Text != text) { chip.Value.Text = text; ++_contentRevision; }
        if (chip.TintSet && ReferenceEquals(chip.Tint, tint)) return;
        chip.Tint = tint;
        chip.TintSet = true;
        if (tint is not null) chip.Value.Foreground = tint;
        else chip.Value.SetResourceReference(TextBlock.ForegroundProperty, chip.Accent);
    }

    private bool AnyChip() => _chips.Exists(c => c.Enabled(_settings));

    private void ApplyChipVisibility()
    {
        ++_contentRevision;
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
                Reposition();
                break;
            case nameof(AppSettings.BandOffset):
            case nameof(AppSettings.BandPosition):
                Reposition();
                break;
            case nameof(AppSettings.SpeedBits):
            case nameof(AppSettings.SpeedUnit):
                Update(_last);
                break;
        }
    }

    public void Revive()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible || !_canPaint || !Native.IsIconic(_hwnd)) return;
        Native.ShowWindow(_hwnd, Native.SW_SHOWNOACTIVATE);
        KeepOnTop();
    }

    private void KeepOnTop()
    {
        if (_hwnd != IntPtr.Zero)
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    private void SetFontSize(double value)
    {
        foreach (var c in _chips)
        {
            c.Value.FontSize = value;
            ((TextBlock)c.Panel.Children[0]).FontSize = value * 0.60;
        }
    }

    private bool FitContent(double widthDip, double heightDip)
    {
        if (Math.Abs(_fitWidth - widthDip) < 0.1 && Math.Abs(_fitHeight - heightDip) < 0.1 &&
            _fitRevision == _contentRevision) return _fits;
        _fitWidth = widthDip;
        _fitHeight = heightDip;
        _fitRevision = _contentRevision;
        foreach (var c in _chips) { c.Panel.Width = double.NaN; c.Panel.MinWidth = 0; }
        // Measure real glyphs, not estimated minimum widths. Pick the largest
        // font that fits BOTH dimensions, then distribute remaining width.
        bool Fits(double font)
        {
            SetFontSize(font);
            _pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return _pill.DesiredSize.Width <= widthDip && _pill.DesiredSize.Height <= heightDip - 2;
        }
        double low = 8, high = Math.Max(low, heightDip);
        if (!Fits(low)) return _fits = false;
        for (int i = 0; i < 10; ++i)
        {
            double mid = (low + high) / 2;
            if (Fits(mid)) low = mid;
            else high = mid;
        }
        Fits(low);
        int count = 0;
        foreach (var c in _chips) if (c.Enabled(_settings)) ++count;
        if (count == 0) return _fits = false;
        double extra = Math.Max(0, widthDip - _pill.DesiredSize.Width) / count;
        foreach (var c in _chips)
            if (c.Enabled(_settings))
                c.Panel.Width = Math.Max(0, c.Panel.DesiredSize.Width - c.Panel.Margin.Left - c.Panel.Margin.Right + extra);
        return _fits = true;
    }

    private void HideNative()
    {
        _canPaint = false;
        if (_hwnd != IntPtr.Zero) Native.ShowWindow(_hwnd, Native.SW_HIDE);
        SetHovering(false);
    }

    private async void RefreshLayout()
    {
        if (_readingLayout || !IsVisible) return;
        IntPtr tray = _taskbar.Handle;
        if (tray == IntPtr.Zero || !Native.GetWindowRect(tray, out var t)) return;
        var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(Native.MonitorFromWindow(tray, 2), ref info) ||
            Math.Min(t.Bottom, info.rcMonitor.Bottom) - Math.Max(t.Top, info.rcMonitor.Top) <= 2) return;
        _readingLayout = true;
        int generation = _layoutGeneration;
        try
        {
            var layout = await Task.Run(() => TaskbarLayout.Read(tray));
            if (!IsVisible || generation != _layoutGeneration || tray != _taskbar.Handle) return;
            _layout = layout;
            Reposition();
        }
        finally { _readingLayout = false; }
    }

    private void Reposition()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible || _repositioning) return;
        IntPtr tray = _taskbar.Handle;
        if (tray == IntPtr.Zero || _layout is null || !Native.GetWindowRect(tray, out var t))
        {
            HideNative();
            return;
        }
        _repositioning = true;
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;
            double trayW = t.Right - t.Left, trayH = t.Bottom - t.Top;
            // A resized or vertical taskbar needs a new safe layout.
            if (trayH > trayW || Math.Abs(_layout.Width - trayW) > 1 || Math.Abs(_layout.Height - trayH) > 1)
            {
                _layout = null;
                ++_layoutGeneration;
                HideNative();
                return;
            }
            Rect area = _layout.FreeArea(_settings.BandPosition, 6 * sx);
            if (area.IsEmpty) { HideNative(); return; }
            double widthPx = Math.Floor(area.Width);
            if (!FitContent(widthPx / sx, trayH / sy))
            {
                HideNative();
                return;
            }
            double preferred = _settings.BandPosition is "Right" or "Start" ? area.Right - widthPx : area.Left;
            double x = t.Left + Math.Clamp(preferred + _settings.BandOffset * sx, area.Left, area.Right - widthPx);
            Width = widthPx / sx;
            Height = trayH / sy;
            Left = x / sx;
            Top = t.Top / sy;
            var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
            if (!Native.GetMonitorInfo(Native.MonitorFromWindow(tray, 2), ref info)) { HideNative(); return; }
            int top = Math.Max(t.Top, info.rcMonitor.Top), bottom = Math.Min(t.Bottom, info.rcMonitor.Bottom);
            if (!Native.IsWindowVisible(tray) || bottom - top <= 2) { HideNative(); return; }
            int pixelX = (int)Math.Ceiling(x), pixelWidth = (int)Math.Floor(widthPx);
            IntPtr region = Native.CreateRectRgn(0, top - t.Top, pixelWidth, bottom - t.Top);
            if (region == IntPtr.Zero) { HideNative(); return; }
            if (Native.SetWindowRgn(_hwnd, region, true) == 0)
            {
                Native.DeleteObject(region);
                HideNative();
                return;
            }
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, pixelX, t.Top, pixelWidth,
                (int)trayH, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            _canPaint = true;
        }
        finally { _repositioning = false; }
    }

    private static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
