using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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

public sealed class BandWindow : IDisposable
{
    private static readonly Brush Warn = Frozen("#E2AE74"), Danger = Frozen("#E27D7D");

    private const int Up = 0, Down = 1, Today = 2, Total = 3, Mem = 4, Cpu = 5;

    private sealed class Chip
    {
        public Chip(string key, string label, string accent, string template, Func<AppSettings, bool> enabled)
        {
            Key = key; Label = label; Accent = accent; Template = template; Enabled = enabled;
        }

        public string Key { get; }
        public string Label { get; }
        public string Accent { get; }
        public string Template { get; }
        public Func<AppSettings, bool> Enabled { get; }
        public StackPanel Panel { get; } = new() { Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
        public TextBlock Caption { get; } = new() { FontSize = 10, Style = null, HorizontalAlignment = HorizontalAlignment.Center };
        public TextBlock Value { get; } = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, Style = null };
        public Brush? Tint { get; set; }
        public bool TintSet { get; set; }
        // Widest the chip has had to be at the current font. It only grows, so numbers that change
        // every sample never make the row breathe; it is rebuilt when the layout is searched again.
        public double Reserved { get; set; }
        public bool Dirty { get; set; } = true;
    }

    private static readonly TimeSpan ReserveLife = TimeSpan.FromSeconds(60);

    private readonly AppSettings _settings;
    private readonly Chip[] _chips;
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _probe = new() { FontWeight = FontWeights.SemiBold, Style = null };
    private IntPtr _hwnd;
    private HwndSource? _source;
    private IntPtr _parent;
    private bool _visible, _disposed;
    private bool IsVisible => _visible;
    private TaskbarLayout? _layout;
    private bool _readingLayout, _layoutDirty, _canPaint;
    private int _layoutGeneration;
    private readonly Border _pill;
    private double _fitWidth = -1, _fitHeight = -1, _fontSize = 13;
    private int _contentRevision, _fitRevision = -1;
    private bool _fits;
    private long _reservedSince;
    private const long MinScanGapMs = 300;
    private readonly DispatcherTimer _layoutTimer = new() { Interval = TimeSpan.FromMilliseconds(1000) };
    private long _lastScanAt;
    // One-shot: re-read the layout as soon as a taskbar move has settled instead of waiting for the poll.
    private readonly DispatcherTimer _retry = new();
    private bool _wasShown;
    private bool _suppressed;
    private Metrics _last;
    private long _todayBytes;
    private readonly TaskbarTracker _taskbar = new();
    private bool _repositioning;
    private int _shownX, _shownY, _shownW, _shownH;
    private bool _shownValid;
    // Hover follows the pointer events; the timer only runs while hovering to catch a missed leave.
    private readonly DispatcherTimer _hover = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private int _outside;
    private bool _hovering;

    public event Action<bool>? HoverChanged;
    // The band is actually on screen (as opposed to enabled but tucked away with an auto-hidden taskbar).
    public event Action<bool>? PaintingChanged;
    public bool IsPainting => _canPaint;
    public event Action<string>? SettingsRequested;

    public Rect BoundsPx => _hwnd != IntPtr.Zero && Native.GetWindowRect(_hwnd, out var r)
        ? new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top)
        : Rect.Empty;

    public BandWindow(AppSettings settings)
    {
        _settings = settings;

        _chips = new[]
        {
            new Chip("up", "上传", "Accent2Brush", "000 KB/s", s => s.BandUp),
            new Chip("down", "下载", "Accent1Brush", "000 KB/s", s => s.BandDown),
            new Chip("today", "今日流量", "TextBrush", "00.0 GB", s => s.BandToday),
            new Chip("total", "总速", "TextBrush", "000 KB/s", s => s.BandTotal),
            new Chip("mem", "内存", "TextBrush", "100%", s => s.BandMem),
            new Chip("cpu", "CPU", "TextBrush", "100%", s => s.BandCpu),
        };
        foreach (var c in _chips)
        {
            c.Caption.Text = c.Label;
            c.Value.HorizontalAlignment = HorizontalAlignment.Center;
            c.Caption.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            c.Value.SetResourceReference(TextBlock.ForegroundProperty, c.Accent);
            c.Panel.Children.Add(c.Caption);
            c.Panel.Children.Add(c.Value);
            c.Panel.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                SettingsRequested?.Invoke(c.Key == "today" ? "流量" : "监控");
            };
            _row.Children.Add(c.Panel);
        }

        _pill = new Border { Padding = new Thickness(1, 0, 1, 0), VerticalAlignment = VerticalAlignment.Stretch, Child = _row };
        _pill.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        var font = (FontFamily)Application.Current.FindResource("AppFont");
        System.Windows.Documents.TextElement.SetFontFamily(_pill, font);
        _probe.FontFamily = font;
        _pill.UseLayoutRounding = true;
        _pill.SnapsToDevicePixels = true;

        _taskbar.Changed += () =>
        {
            if (_taskbar.IsMoving) SetHovering(false);
            Reposition();
            // Coming back from auto-hide: what sits on the taskbar may have changed while it was away.
            bool shown = TrayShown(out _);
            if (shown && !_wasShown) RefreshLayout();
            _wasShown = shown;
        };
        _taskbar.LayoutChanged += () =>
        {
            // Keep painting where we are while the free space is re-read (a few milliseconds);
            // Reposition hides the band itself if the new layout no longer leaves room.
            ++_layoutGeneration;
            // An auto-hidden taskbar is re-read when it comes back (see Changed), not on every event
            // its tray and buttons raise while nobody can see it.
            if (_layout is null || TrayShown(out _)) RefreshLayout();
        };
        _taskbar.Rebuilt += () =>
        {
            ++_layoutGeneration;
            _layout = null;
            HideNative();
            RefreshLayout();
        };
        _layoutTimer.Tick += (_, _) =>
        {
            // The taskbar's own geometry is the only thing that can change while it is tucked away, and the
            // tracker reports that; skip the cross-process scan until it is back (unless we have no layout).
            if (_layout is null || TrayShown(out _)) RefreshLayout();
        };
        _retry.Tick += (_, _) => { _retry.Stop(); RefreshLayout(); };
        _pill.MouseRightButtonUp += (_, e) => { e.Handled = true; SettingsRequested?.Invoke("监控"); };
        _pill.MouseEnter += (_, _) => { if (_canPaint && !_taskbar.IsMoving) { _outside = 0; SetHovering(true); } };
        _pill.MouseLeave += (_, _) => { if (_hovering) _hover.Start(); };
        _settings.PropertyChanged += OnSettingChanged;
        _hover.Tick += (_, _) => PollHover();
        ApplyChipVisibility();
    }

    public void SetSuppressed(bool suppressed)
    {
        if (_suppressed == suppressed) return;
        _suppressed = suppressed;
        Sync();
    }

    public void Sync()
    {
        bool show = _settings.BandVisible && !_suppressed && AnyChip();
        if (show && !IsVisible)
        {
            _visible = true;
            _taskbar.Start();
            _layoutTimer.Start();
            RefreshLayout();
            Reposition();
            _wasShown = TrayShown(out _);
        }
        else if (!show && IsVisible)
        {
            HideNative();
            _visible = false;
            _taskbar.Stop();
            _layoutTimer.Stop();
            _retry.Stop();
            ++_layoutGeneration;
            _layout = null;
            SetPainting(false);
            _hover.Stop();
            SetHovering(false);
        }
    }

    public void Update(Metrics m, long? todayBytes = null)
    {
        _last = m;
        if (todayBytes.HasValue) _todayBytes = todayBytes.Value;
        bool bits = _settings.SpeedBits;
        string unit = _settings.SpeedUnit;
        bool changed = false;
        changed |= Set(Up, UnitFormat.Speed(m.UpBps, bits, unit), null);
        changed |= Set(Down, UnitFormat.Speed(m.DownBps, bits, unit), null);
        changed |= Set(Today, UnitFormat.Size(_todayBytes, false, "Auto"), null);
        changed |= Set(Total, UnitFormat.Speed(m.UpBps + m.DownBps, bits, unit), null);
        changed |= Set(Mem, $"{m.Mem:0}%", Load(m.Mem));
        changed |= Set(Cpu, $"{m.Cpu:0}%", Load(m.Cpu));

        if (!changed || !IsVisible || !_canPaint) return;
        // Text swaps are cheap. Only a value wider than anything seen so far (at this font) needs
        // the row re-fitted; everything else just re-renders in place.
        if (ReserveGrew()) Reposition();
        else _pill.UpdateLayout();
    }

    private void PollHover()
    {
        bool over = _hwnd != IntPtr.Zero && IsVisible && _canPaint && Native.GetCursorPos(out var p) &&
                    Native.GetWindowRect(_hwnd, out var r) && p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
        if (over) { _outside = 0; return; }
        if (++_outside >= 1)
        {
            _hover.Stop();
            SetHovering(false);
        }
    }

    private void SetPainting(bool on)
    {
        if (_canPaint == on) return;
        _canPaint = on;
        PaintingChanged?.Invoke(on);
    }

    private void SetHovering(bool on)
    {
        if (_hovering == on) return;
        _hovering = on;
        if (on) _hover.Start(); else _hover.Stop();
        HoverChanged?.Invoke(on);
    }

    private static Brush? Load(double percent) => percent >= 90 ? Danger : percent >= 70 ? Warn : null;

    private bool Set(int index, string text, Brush? tint)
    {
        var chip = _chips[index];
        bool changed = false;
        if (chip.Value.Text != text)
        {
            chip.Value.Text = text;
            chip.Dirty = true;
            changed = true;
        }
        if (chip.TintSet && ReferenceEquals(chip.Tint, tint)) return changed;
        chip.Tint = tint;
        chip.TintSet = true;
        if (tint is not null) chip.Value.Foreground = tint;
        else chip.Value.SetResourceReference(TextBlock.ForegroundProperty, chip.Accent);
        return true;
    }

    private bool AnyChip()
    {
        foreach (var c in _chips) if (c.Enabled(_settings)) return true;
        return false;
    }

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
                ++_contentRevision; // new unit strings change the widest text
                Update(_last);
                Reposition();
                break;
        }
    }

    public void Revive()
    {
        if (!_visible || !_canPaint || _hwnd == IntPtr.Zero) return;
        // The shell may iconify or hide top-level windows when the desktop is shown; we are still wanted.
        if (!Native.IsWindowVisible(_hwnd) || Native.IsIconic(_hwnd)) Native.ShowWindow(_hwnd, Native.SW_SHOWNOACTIVATE);
        KeepOnTop();
    }

    private void KeepOnTop()
    {
        if (_hwnd != IntPtr.Zero && Native.IsWindow(_hwnd))
            Native.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    private bool EnsureHost(IntPtr tray)
    {
        if (_disposed || tray == IntPtr.Zero || !Native.IsWindow(tray)) return false;
        if (_parent == tray && _hwnd != IntPtr.Zero && Native.IsWindow(_hwnd)) return true;
        _source?.Dispose();
        _hwnd = IntPtr.Zero;
        _shownValid = false;
        _parent = tray;
        var parameters = new HwndSourceParameters("ZoneQuanta Band")
        {
            ParentWindow = IntPtr.Zero,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP
            ExtendedWindowStyle = (int)(Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | 0x8),
            PositionX = 0, PositionY = 2, Width = 1, Height = 1,
        };
        _source = new HwndSource(parameters) { RootVisual = _pill, SizeToContent = SizeToContent.Manual };
        // Explorer's composition bridge can cover foreign child surfaces even
        // when UI Automation reports their text. Use an independent target.
        _source.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
        _source.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (message == 0x21) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
            return IntPtr.Zero;
        });
        _hwnd = _source.Handle;
        _fitRevision = -1;
        return _hwnd != IntPtr.Zero;
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _visible = false;
        _taskbar.Dispose();
        _hover.Stop();
        _layoutTimer.Stop();
        _retry.Stop();
        _settings.PropertyChanged -= OnSettingChanged;
        _source?.Dispose();
        _source = null;
        _hwnd = IntPtr.Zero;
    }

    private void SetFontSize(double value)
    {
        _fontSize = value;
        foreach (var c in _chips)
        {
            c.Value.FontSize = value;
            c.Caption.FontSize = Math.Max(8, value * 0.60);
        }
    }

    private double Needed(Chip c)
    {
        var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);
        c.Caption.Measure(infinite);
        c.Value.Measure(infinite);
        return Math.Max(c.Caption.DesiredSize.Width, c.Value.DesiredSize.Width);
    }

    private double TemplateWidth(Chip c)
    {
        _probe.FontSize = _fontSize;
        _probe.Text = c.Template;
        _probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return _probe.DesiredSize.Width;
    }

    // True when a freshly changed value is wider than the room reserved for its chip.
    private bool ReserveGrew()
    {
        bool grew = false;
        foreach (var c in _chips)
        {
            if (!c.Dirty || !c.Enabled(_settings)) continue;
            c.Dirty = false;
            double need = Needed(c);
            if (need > c.Reserved + 0.01)
            {
                c.Reserved = need;
                grew = true;
            }
        }
        return grew;
    }

    private bool FitContent(double widthDip, double heightDip)
    {
        long now = Stopwatch.GetTimestamp();
        bool same = Math.Abs(_fitWidth - widthDip) < 0.1 && Math.Abs(_fitHeight - heightDip) < 0.1 &&
                    _fitRevision == _contentRevision && Stopwatch.GetElapsedTime(_reservedSince, now) < ReserveLife;
        if (same && !_fits) return false;
        if (same)
        {
            ReserveGrew();
            if (Distribute(widthDip)) return true;
        }
        return SearchFont(widthDip, heightDip, now);
    }

    // Shares spare width evenly between the visible chips; false when the reserved widths no longer fit.
    private bool Distribute(double widthDip)
    {
        double used = _pill.Padding.Left + _pill.Padding.Right;
        int count = 0;
        foreach (var c in _chips)
        {
            if (!c.Enabled(_settings)) continue;
            used += c.Reserved + c.Panel.Margin.Left + c.Panel.Margin.Right;
            ++count;
        }
        if (count == 0 || used > widthDip - 2) return false;
        double extra = (widthDip - used) / count;
        foreach (var c in _chips)
            if (c.Enabled(_settings))
                c.Panel.Width = c.Reserved + extra;
        return true;
    }

    private bool SearchFont(double widthDip, double heightDip, long now)
    {
        _fitWidth = widthDip;
        _fitHeight = heightDip;
        _fitRevision = _contentRevision;
        _reservedSince = now;
        foreach (var c in _chips) { c.Panel.Width = double.NaN; c.Panel.MinWidth = 0; c.Dirty = false; }

        // A HwndSource root can report a stale/zero DesiredSize while its
        // layout is pending. Measure the actual leaf text controls directly.
        bool Fits(double font)
        {
            SetFontSize(font);
            double width = _pill.Padding.Left + _pill.Padding.Right;
            double height = 0;
            foreach (var c in _chips)
            {
                if (!c.Enabled(_settings)) continue;
                double need = Needed(c);
                c.Reserved = Math.Max(need, TemplateWidth(c));
                width += c.Reserved + c.Panel.Margin.Left + c.Panel.Margin.Right;
                height = Math.Max(height, c.Caption.DesiredSize.Height + c.Value.DesiredSize.Height);
            }
            return width <= widthDip - 2 && height <= heightDip - 2;
        }

        double low = 9;
        double high = Math.Max(low, heightDip);
        if (!Fits(low)) return _fits = false;
        for (int i = 0; i < 10; ++i)
        {
            double mid = (low + high) / 2;
            if (Fits(mid)) low = mid;
            else high = mid;
        }
        // Half-DIP steps keep small changes in sampled numbers from making
        // the font visibly pulse. Never round upwards past the measured fit.
        low = Math.Max(9, Math.Floor(low * 2) / 2);
        if (!Fits(low)) return _fits = false;
        return _fits = Distribute(widthDip);
    }

    private void HideNative()
    {
        SetPainting(false);
        _shownValid = false;
        if (_hwnd != IntPtr.Zero) Native.ShowWindow(_hwnd, Native.SW_HIDE);
        SetHovering(false);
    }

    private async void RefreshLayout()
    {
        if (!IsVisible || _disposed) return;
        if (_taskbar.IsMoving)
        {
            RetryAfterMove();
            return;
        }
        long sinceScan = Environment.TickCount64 - _lastScanAt;
        if (sinceScan < MinScanGapMs)
        {
            // Bursts of shell events collapse into one scan shortly after the burst.
            if (!_retry.IsEnabled)
            {
                _retry.Interval = TimeSpan.FromMilliseconds(MinScanGapMs - sinceScan + 5);
                _retry.Start();
            }
            return;
        }
        if (_readingLayout)
        {
            _layoutDirty = true; // a change arrived mid-read: read again as soon as this one lands
            return;
        }
        _readingLayout = true;
        try
        {
            do
            {
                _layoutDirty = false;
                IntPtr tray = _taskbar.Handle;
                if (tray == IntPtr.Zero || !Native.IsWindow(tray)) return;

                // Control rectangles are relative to the taskbar window, so the scan works while an
                // auto-hidden taskbar is tucked away too; the band is simply ready when it comes back.
                int generation = _layoutGeneration;
                _lastScanAt = Environment.TickCount64;
                var layout = await Task.Run(() => TaskbarLayout.Read(tray));
                if (_disposed || !IsVisible || tray != _taskbar.Handle) return;
                if (_taskbar.IsMoving)
                {
                    RetryAfterMove(); // rectangles were read against a window that moved meanwhile
                    return;
                }
                if (generation != _layoutGeneration)
                {
                    _layoutDirty = true;
                    continue;
                }
                bool unchanged = layout is not null && layout.SameAs(_layout);
                _layout = layout;
                if (!unchanged || !_canPaint) Reposition();
            } while (_layoutDirty);
        }
        finally { _readingLayout = false; }
    }

    private void RetryAfterMove()
    {
        _retry.Interval = TimeSpan.FromMilliseconds(Math.Max(20, _taskbar.MovingRemainingMs + 15));
        _retry.Start();
    }

    // The taskbar is on screen in full (an auto-hidden one leaves only a thin edge).
    private static bool TrayShown(out Native.RECT t, IntPtr tray)
    {
        t = default;
        if (tray == IntPtr.Zero || !Native.GetWindowRect(tray, out t) || !Native.IsWindowVisible(tray)) return false;
        var monitor = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(Native.MonitorFromWindow(tray, 2), ref monitor)) return false;
        return Math.Min(t.Bottom, monitor.rcMonitor.Bottom) - Math.Max(t.Top, monitor.rcMonitor.Top) >= t.Bottom - t.Top - 2;
    }

    private bool TrayShown(out Native.RECT t) => TrayShown(out t, _taskbar.Handle);

    private void Reposition()
    {
        if (!IsVisible || _disposed || _repositioning) return;
        IntPtr tray = _taskbar.Handle;
        if (tray == IntPtr.Zero || _layout is null || !Native.GetWindowRect(tray, out var t))
        {
            HideNative();
            return;
        }
        if (!EnsureHost(tray)) { HideNative(); return; }
        _repositioning = true;
        try
        {
            var dpi = VisualTreeHelper.GetDpi(_pill);
            double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;
            double trayW = t.Right - t.Left, trayH = t.Bottom - t.Top;
            var monitor = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
            if (!Native.IsWindowVisible(tray) || !Native.GetMonitorInfo(Native.MonitorFromWindow(tray, 2), ref monitor) ||
                Math.Min(t.Bottom, monitor.rcMonitor.Bottom) - Math.Max(t.Top, monitor.rcMonitor.Top) < trayH - 2)
            {
                HideNative();
                return;
            }
            // A resized or vertical taskbar needs a new safe layout.
            if (trayH > trayW || Math.Abs(_layout.Width - trayW) > 1 || Math.Abs(_layout.Height - trayH) > 1)
            {
                _layout = null;
                ++_layoutGeneration;
                HideNative();
                RefreshLayout();
                return;
            }
            Rect area = _layout.FreeArea(_settings.BandPosition, 6 * sx);
            if (area.IsEmpty) { HideNative(); return; }
            double widthPx = Math.Floor(area.Width);
            if (!FitContent(widthPx / sx, (trayH - 4) / sy))
            {
                HideNative();
                return;
            }
            double preferred = _settings.BandPosition is "Right" or "Start" ? area.Right - widthPx : area.Left;
            double x = Math.Clamp(preferred + _settings.BandOffset * sx, area.Left, area.Right - widthPx);
            // Follow the taskbar in physical screen coordinates. The tracker
            // checks geometry independently of the metrics sampling.
            int pixelX = (int)Math.Ceiling(x), pixelWidth = (int)Math.Floor(widthPx);
            int pixelHeight = Math.Max(1, (int)trayH - 4);
            int screenX = t.Left + pixelX, screenY = t.Top + 2;

            bool resized = !_shownValid || pixelWidth != _shownW || pixelHeight != _shownH;
            _pill.Width = pixelWidth / sx;
            _pill.Height = pixelHeight / sy;
            _pill.Measure(new Size(_pill.Width, _pill.Height));
            _pill.Arrange(new Rect(0, 0, _pill.Width, _pill.Height));
            _pill.UpdateLayout();

            if (resized || screenX != _shownX || screenY != _shownY)
            {
                Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, screenX, screenY, pixelWidth, pixelHeight,
                    Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
                _shownX = screenX; _shownY = screenY; _shownW = pixelWidth; _shownH = pixelHeight;
                _shownValid = true;
            }
            SetPainting(true);
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
