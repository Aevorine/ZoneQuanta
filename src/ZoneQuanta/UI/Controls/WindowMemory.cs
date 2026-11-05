using System;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ZoneQuanta.Core.Settings;
using Forms = System.Windows.Forms;

namespace ZoneQuanta.UI.Controls;

// Remembers a resizable window's size, spot and maximized state. Saved when a move or resize ends, on
// maximize / restore, half a second after any other change (Win + arrows) and when the window hides.
// Only what the user changed is saved: a placement equal to what Place applied is not written back.
public sealed class WindowMemory
{
    public enum Spot { Last, Center, Cursor }

    private const int WM_SIZING = 0x0214, WM_EXITSIZEMOVE = 0x0232, WM_DPICHANGED = 0x02E0;
    private readonly Window _w;
    private readonly Func<WindowBounds?> _load;
    private readonly Action<WindowBounds?> _save;
    private readonly DispatcherTimer _settle = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _applying, _sizing;
    private long _placedAt;
    private WindowBounds? _last;

    // Proposed size in pixels while an edge is dragged; also fires with outline-only dragging.
    public event Action<Size>? Resizing;
    public event Action? ResizeEnded;

    public WindowMemory(Window window, Func<WindowBounds?> load, Action<WindowBounds?> save)
    {
        _w = window;
        _load = load;
        _save = save;
        _settle.Tick += (_, _) => Commit();
        var helper = new WindowInteropHelper(window);
        if (helper.Handle != IntPtr.Zero) Hook(helper.Handle);
        else window.SourceInitialized += (_, _) => Hook(helper.Handle);
        window.SizeChanged += (_, _) => Changed();
        window.LocationChanged += (_, _) => Changed();
        window.StateChanged += (_, _) => { if (!_applying && window.IsVisible) Commit(); };
        window.IsVisibleChanged += (_, e) => { if (!(bool)e.NewValue) Commit(); };
    }

    public double PixelScale => Math.Max(0.5, VisualTreeHelper.GetDpi(_w).DpiScaleX);

    // Remembered size clamped to the work area; remembered spot (Last), centre with the given rise, or the
    // pointer; maximized again last. With nothing remembered, `size` at the centre.
    public void Place(Spot spot, Size size, double rise = 0.5, bool onCursorScreen = true)
    {
        var b = _load();
        var screen = onCursorScreen
            ? Forms.Screen.FromPoint(Forms.Cursor.Position)
            : Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == b?.Screen) ?? Forms.Screen.PrimaryScreen!;
        double k = PixelScale;
        var work = Usable(screen, k);
        double w = Math.Min(b?.Width ?? size.Width, work.Width), h = Math.Min(b?.Height ?? size.Height, work.Height);
        if (_w.MinWidth > w) _w.MinWidth = w;
        if (_w.MinHeight > h) _w.MinHeight = h;

        double left = work.Left + (work.Width - w) / 2, top = work.Top + (work.Height - h) * rise;
        if (spot == Spot.Last && b is { HasPlace: true })
            (left, top) = (work.Left + b.CenterX * work.Width - w / 2, work.Top + b.CenterY * work.Height - h / 2);
        else if (spot == Spot.Cursor)
            (left, top) = (Forms.Cursor.Position.X / k - w / 2, Forms.Cursor.Position.Y / k - h * 0.3);
        left = Fit(left, work.Left, work.Right - w);
        top = Fit(top, work.Top, work.Bottom - h);

        _applying = true;
        try
        {
            if (_w.WindowState != WindowState.Normal) _w.WindowState = WindowState.Normal;
            _w.Left = left;
            _w.Top = top;
            _w.Width = w;
            _w.Height = h;
            if (b?.Maximized == true) _w.WindowState = WindowState.Maximized;
        }
        finally { _applying = false; }
        _settle.Stop();
        _placedAt = Environment.TickCount64;
        _last = Bounds(new Rect(left, top, w, h), work, b?.Maximized == true, screen.DeviceName);
    }

    // Shown on a monitor whose DPI differs from the one it was placed with: fit again in that monitor's
    // units and take the result as the baseline, so the scale change is not saved as a user change.
    private void Refit()
    {
        var hwnd = new WindowInteropHelper(_w).Handle;
        if (hwnd == IntPtr.Zero || _w.WindowState == WindowState.Minimized) return;
        var screen = Forms.Screen.FromHandle(hwnd);
        var work = Usable(screen, PixelScale);
        bool max = _w.WindowState == WindowState.Maximized;
        var r = max ? _w.RestoreBounds : new Rect(_w.Left, _w.Top, _w.ActualWidth, _w.ActualHeight);
        if (r.IsEmpty || double.IsNaN(r.Left)) return;
        if (!max)
        {
            double w = Math.Min(r.Width, work.Width), h = Math.Min(r.Height, work.Height);
            r = new Rect(Fit(r.Left, work.Left, work.Right - w), Fit(r.Top, work.Top, work.Bottom - h), w, h);
            _applying = true;
            try
            {
                _w.Left = r.Left;
                _w.Top = r.Top;
                _w.Width = r.Width;
                _w.Height = r.Height;
            }
            finally { _applying = false; }
        }
        _settle.Stop();
        _last = Bounds(r, work, max, screen.DeviceName);
    }

    // Clamp without Math.Clamp's min <= max precondition: px / scale rounding can leave max one ulp below min.
    private static double Fit(double v, double min, double max) => Math.Max(min, Math.Min(v, max));

    public void Commit()
    {
        _settle.Stop();
        if (_applying || _w.WindowState == WindowState.Minimized) return;
        var hwnd = new WindowInteropHelper(_w).Handle;
        if (hwnd == IntPtr.Zero) return;
        bool max = _w.WindowState == WindowState.Maximized;
        var r = max ? _w.RestoreBounds : new Rect(_w.Left, _w.Top, _w.ActualWidth, _w.ActualHeight);
        if (r.IsEmpty || double.IsNaN(r.Left) || r.Width < 1 || r.Height < 1) return;
        var screen = Forms.Screen.FromHandle(hwnd);
        var b = Bounds(r, Usable(screen, PixelScale), max, screen.DeviceName);
        if (Same(b, _last)) return;
        _last = b;
        _save(b);
    }

    private void Changed()
    {
        if (_applying || !_w.IsVisible) return;
        _settle.Stop();
        _settle.Start();
    }

    private void Hook(IntPtr hwnd) => HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
    {
        if (msg == WM_DPICHANGED && Environment.TickCount64 - _placedAt < 2000)
            _w.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, Refit);
        else if (msg == WM_SIZING)
        {
            _sizing = true;
            var r = System.Runtime.InteropServices.Marshal.PtrToStructure<Platform.Native.RECT>(lp);
            Resizing?.Invoke(new Size(r.Right - r.Left, r.Bottom - r.Top));
        }
        else if (msg == WM_EXITSIZEMOVE)
        {
            if (_sizing)
            {
                _sizing = false;
                ResizeEnded?.Invoke();
            }
            Commit();
        }
        return IntPtr.Zero;
    });

    private static WindowBounds Bounds(Rect r, Rect work, bool max, string screen) => new(
        Math.Round(r.Width), Math.Round(r.Height),
        Math.Round(Math.Clamp((r.Left + r.Width / 2 - work.Left) / work.Width, 0, 1), 4),
        Math.Round(Math.Clamp((r.Top + r.Height / 2 - work.Top) / work.Height, 0, 1), 4),
        max, screen);

    // Within a DIP of size and about 2 DIPs of centre: rounding, not a user change.
    private static bool Same(WindowBounds a, WindowBounds? b) =>
        b is not null && a.Maximized == b.Maximized && a.Screen == b.Screen &&
        Math.Abs(a.Width - b.Width) <= 1 && Math.Abs(a.Height - b.Height) <= 1 &&
        Math.Abs(a.CenterX - b.CenterX) <= 0.0015 && Math.Abs(a.CenterY - b.CenterY) <= 0.0025;

    private static Rect Usable(Forms.Screen screen, double k)
    {
        var px = Platform.MaximizeFit.UsableArea(screen.WorkingArea, screen.Bounds);
        return new Rect(px.Left / k, px.Top / k, px.Width / k, px.Height / k);
    }
}
