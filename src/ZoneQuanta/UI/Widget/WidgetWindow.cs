using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ZoneQuanta.Core.Settings;
using ZoneQuanta.Core.Time;
using ZoneQuanta.Platform;
using ZoneQuanta.UI.Controls;

namespace ZoneQuanta.UI.Widget;

public sealed class WidgetWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ClockStrip _strip;
    private readonly ScaleTransform _scale = new(1, 1);
    private IntPtr _hwnd;
    private bool _suppressed;
    private bool _applyingPosition;
    private bool _lift;

    public WidgetWindow(AppSettings settings, TimeEngine engine)
    {
        _settings = settings;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        Title = "ZoneQuanta";
        Focusable = false;

        _strip = new ClockStrip(settings, engine, compact: true) { LayoutTransform = _scale };
        Content = _strip;

        SourceInitialized += OnSourceInitialized;
        StateChanged += (_, _) => Revive();
        LocationChanged += OnLocationChanged;
        SizeChanged += OnSizeChanged;
        MouseLeftButtonDown += OnMouseDown;
        _settings.PropertyChanged += OnSettingChanged;

        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
        Closed += (_, _) =>
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
        };
    }

    public void SetDesktopLift(bool lift)
    {
        if (_lift == lift) return;
        _lift = lift;
        ApplyTopmost();
    }

    private void ApplyTopmost() => Topmost = _settings.Topmost || _lift;

    private void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (!IsVisible) return;
        Place();
        ReassertTopmost();
    });

    private void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason is Microsoft.Win32.SessionSwitchReason.SessionUnlock or Microsoft.Win32.SessionSwitchReason.ConsoleConnect or Microsoft.Win32.SessionSwitchReason.RemoteConnect)
            Dispatcher.BeginInvoke(() => { Revive(); ReassertTopmost(); });
    }

    public void Tick(DateTimeOffset now)
    {
        if (IsVisible) _strip.Update(now);
    }

    // Unconditional repaint of the text, for the moment a hidden widget is about to be shown again.
    public void Refresh(DateTimeOffset now) => _strip.Update(now);

    public void SetSuppressed(bool suppressed)
    {
        if (_suppressed == suppressed) return;
        _suppressed = suppressed;
        SyncVisibility();
    }

    // "Show desktop" and minimize-all either iconify or hide top-level windows; the widget has to stay up.
    public void Revive()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible) return;
        if (!Native.IsIconic(_hwnd) && Native.IsWindowVisible(_hwnd)) return;
        Native.ShowWindow(_hwnd, Native.SW_SHOWNOACTIVATE);
        ReassertTopmost();
    }

    public void ReassertTopmost()
    {
        if (_hwnd != IntPtr.Zero && Topmost && IsVisible)
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    private const double EdgeGap = 20;

    // The spot the user chose is the single source of truth: explicit coordinates (Pin empty) or an
    // edge / corner / centre pin. The window is always derived from it, so a size change (new text,
    // scale, flip) can never drag a locked widget away from where it was put.
    public void MoveTo(double left, double top)
    {
        _settings.Pin = "";
        _settings.Left = left;
        _settings.Top = top;
        Place();
        if (!_settings.AllowOffScreen)
        {
            _settings.Left = Left;
            _settings.Top = Top;
        }
    }

    public void Anchor(string where)
    {
        _settings.Pin = where;
        Place();
    }

    private void Place()
    {
        if (_hwnd == IntPtr.Zero && !IsVisible) return;
        double w = ActualWidth > 0 ? ActualWidth : 400, h = ActualHeight > 0 ? ActualHeight : 150;
        double x = _settings.Left ?? Left, y = _settings.Top ?? Top;
        if (double.IsNaN(x) || double.IsNaN(y)) { x = 0; y = 0; }

        string pin = _settings.Pin;
        if (pin.Length > 0)
        {
            var area = WorkArea();
            x = pin.Contains('L') ? area.Left + EdgeGap : pin.Contains('R') ? area.Right - w - EdgeGap : area.Left + (area.Width - w) / 2;
            y = pin.Contains('T') ? area.Top + EdgeGap : pin.Contains('B') ? area.Bottom - h - EdgeGap : area.Top + (area.Height - h) / 2;
            _settings.Left = x;
            _settings.Top = y;
        }

        if (!_settings.AllowOffScreen) (x, y) = Clamp(x, y);
        if (Math.Abs(x - Left) < 0.01 && Math.Abs(y - Top) < 0.01) return;
        _applyingPosition = true;
        Left = x;
        Top = y;
        _applyingPosition = false;
    }

    public void ApplyInitial(DateTimeOffset now)
    {
        ApplyScaleAndOpacity();
        ApplyTopmost();
        _strip.Update(now);
        Show();
        SyncVisibility();
        UpdateLayout();
        PlaceInitially();
    }

    private void PlaceInitially()
    {
        if (_settings.Pin.Length == 0 && _settings.Left is { } l && _settings.Top is { } t)
        {
            _applyingPosition = true;
            Left = l;
            Top = t;
            _applyingPosition = false;
            Place();
        }
        else
        {
            Anchor(_settings.Pin.Length > 0 ? _settings.Pin : "TR");
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        Native.SetExStyle(_hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE, true);
        Native.SetExStyle(_hwnd, Native.WS_EX_TRANSPARENT, _settings.ClickThrough);
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.ClickThrough):
                if (_hwnd != IntPtr.Zero) Native.SetExStyle(_hwnd, Native.WS_EX_TRANSPARENT, _settings.ClickThrough);
                break;
            case nameof(AppSettings.Topmost):
                ApplyTopmost();
                break;
            case nameof(AppSettings.Scale):
            case nameof(AppSettings.Opacity):
                ApplyScaleAndOpacity();
                break;
            case nameof(AppSettings.WidgetVisible):
                SyncVisibility();
                break;
            case nameof(AppSettings.AllowOffScreen):
                Place();
                break;
        }
    }

    private void ApplyScaleAndOpacity()
    {
        _scale.ScaleX = _scale.ScaleY = _settings.Scale;
        Opacity = _settings.Opacity;
    }

    private void SyncVisibility()
    {
        bool show = _settings.WidgetVisible && !_suppressed;
        if (show && !IsVisible) Show();
        else if (!show && IsVisible) Hide();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.LockPosition || _settings.ClickThrough || e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); }
        catch (InvalidOperationException) { }
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (_applyingPosition) return;
        _settings.Pin = "";
        double l = Left, t = Top;
        if (!_settings.AllowOffScreen)
        {
            var (cl, ct) = Clamp(l, t);
            if (Math.Abs(cl - l) > 0.5 || Math.Abs(ct - t) > 0.5)
            {
                _applyingPosition = true;
                Left = cl;
                Top = ct;
                _applyingPosition = false;
                l = cl;
                t = ct;
            }
        }
        _settings.Left = l;
        _settings.Top = t;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.PreviousSize.Width < 1 || !IsVisible) return;
        Place();
    }

    private (double, double) Clamp(double left, double top)
    {
        var area = WorkArea();
        double w = ActualWidth, h = ActualHeight;
        return (Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - w)),
                Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - h)));
    }

    private Rect WorkArea()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        System.Drawing.Rectangle r;
        if (!double.IsNaN(Left) && !double.IsNaN(Top))
        {
            var px = new System.Drawing.Point((int)(Left * dpi.DpiScaleX), (int)(Top * dpi.DpiScaleY));
            r = System.Windows.Forms.Screen.FromPoint(px).WorkingArea;
        }
        else
        {
            r = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
        }
        return new Rect(r.Left / dpi.DpiScaleX, r.Top / dpi.DpiScaleY, r.Width / dpi.DpiScaleX, r.Height / dpi.DpiScaleY);
    }
}
