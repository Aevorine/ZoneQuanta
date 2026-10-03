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

        _strip = new ClockStrip(settings, engine) { LayoutTransform = _scale };
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
        if (!_settings.AllowOffScreen) MoveTo(Left, Top);
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

    public void SetSuppressed(bool suppressed)
    {
        if (_suppressed == suppressed) return;
        _suppressed = suppressed;
        SyncVisibility();
    }

    public void Revive()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible || !Native.IsIconic(_hwnd)) return;
        Native.ShowWindow(_hwnd, Native.SW_SHOWNOACTIVATE);
        ReassertTopmost();
    }

    public void ReassertTopmost()
    {
        if (_hwnd != IntPtr.Zero && Topmost && IsVisible)
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    public void MoveTo(double left, double top)
    {
        if (!_settings.AllowOffScreen) (left, top) = Clamp(left, top);
        _applyingPosition = true;
        Left = left;
        Top = top;
        _applyingPosition = false;
        _settings.Left = left;
        _settings.Top = top;
    }

    public void Anchor(string where)
    {
        var area = WorkArea();
        double w = ActualWidth > 0 ? ActualWidth : 400, h = ActualHeight > 0 ? ActualHeight : 150;
        const double gap = 20;
        double x = where.Contains('L') ? area.Left + gap : where.Contains('R') ? area.Right - w - gap : area.Left + (area.Width - w) / 2;
        double y = where.Contains('T') ? area.Top + gap : where.Contains('B') ? area.Bottom - h - gap : area.Top + (area.Height - h) / 2;
        MoveTo(x, y);
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
        if (_settings.Left is { } l && _settings.Top is { } t)
        {
            _applyingPosition = true;
            Left = l;
            Top = t;
            _applyingPosition = false;
            if (!_settings.AllowOffScreen) MoveTo(l, t);
        }
        else
        {
            Anchor("TR");
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
                if (!_settings.AllowOffScreen) MoveTo(Left, Top);
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
        if (e.PreviousSize.Width < 1 || double.IsNaN(Left) || double.IsNaN(Top) || !IsVisible) return;

        var area = WorkArea();
        double left = Left, top = Top;
        if (e.WidthChanged && left + e.PreviousSize.Width / 2 > area.Left + area.Width / 2)
            left -= e.NewSize.Width - e.PreviousSize.Width;
        if (e.HeightChanged && top + e.PreviousSize.Height / 2 > area.Top + area.Height / 2)
            top -= e.NewSize.Height - e.PreviousSize.Height;

        if (Math.Abs(left - Left) < 0.5 && Math.Abs(top - Top) < 0.5) return;
        _applyingPosition = true;
        Left = left;
        Top = top;
        _applyingPosition = false;
        _settings.Left = left;
        _settings.Top = top;
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
