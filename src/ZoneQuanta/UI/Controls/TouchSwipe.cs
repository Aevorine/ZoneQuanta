using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using ZoneQuanta.Core.Input;
using ZoneQuanta.Platform;

namespace ZoneQuanta.UI.Controls;

// Three-finger swipes over a window while it shows. Raw touchpad / touchscreen contacts are the main source (the system
// keeps three-finger gestures to itself); WPF touch events stand in for a touchscreen whose raw reports never arrive.
// Raw input is registered only while the window is visible, and kept for a short grace after it hides, so a swipe the
// system also acts on (show desktop takes the focus away mid-gesture) is still seen to its end.
public sealed class TouchSwipe
{
    private const long RawWins = 400, Settle = 700;
    private readonly Window _window;
    private readonly Action<SwipeDirection> _swiped;
    private readonly RawTouch _raw = new();
    // Thresholds are fractions of the surface: a touchpad is small, a screen large.
    private readonly SwipeRecognizer _pad = new(3, 0.16), _screen = new(3, 0.07), _touch = new(3, 0.07);
    private readonly Dictionary<int, Point> _touches = new();
    private readonly DispatcherTimer _release = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private IntPtr _hwnd;
    private long _rawScreenAt = long.MinValue / 2, _busyAt = long.MinValue / 2;
    private bool _failed, _readFailed;

    private TouchSwipe(Window window, Action<SwipeDirection> swiped)
    {
        _window = window;
        _swiped = swiped;
        window.SourceInitialized += (_, _) => Hook();
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) Hook();
        window.IsVisibleChanged += (_, _) => OnVisible();
        _release.Tick += (_, _) =>
        {
            _release.Stop();
            if (!_window.IsVisible) _raw.Unregister();
        };
        window.PreviewTouchDown += (_, e) => Touch(e, down: true);
        window.PreviewTouchMove += (_, e) => Touch(e, down: true);
        window.PreviewTouchUp += (_, e) => Touch(e, down: false);
        window.LostTouchCapture += (_, e) => Touch(e, down: false);
    }

    public static TouchSwipe Attach(Window window, Action<SwipeDirection> swiped) => new(window, swiped);

    // Three or more fingers are down, or were a moment ago: the focus may be leaving because of the gesture.
    public bool Busy => Math.Max(_pad.Down, Math.Max(_screen.Down, _touch.Down)) >= 3 || Environment.TickCount64 - _busyAt < Settle;

    private void Hook()
    {
        if (_hwnd != IntPtr.Zero) return;
        _hwnd = new WindowInteropHelper(_window).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        OnVisible();
    }

    private void OnVisible()
    {
        if (!_window.IsVisible)
        {
            _release.Stop();
            _release.Start();
            return;
        }
        _release.Stop();
        if (_hwnd == IntPtr.Zero || _failed || _raw.Register(_hwnd)) return;
        _failed = true;
        Core.Log.Write($"touch swipe: raw input unavailable ({System.Runtime.InteropServices.Marshal.GetLastWin32Error()}), touch events only");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Not handled: the default procedure still runs and frees the input.
        if (msg != RawTouch.WM_INPUT) return IntPtr.Zero;
        try { _raw.Read(lParam, OnFrame); }
        catch (Exception ex)
        {
            if (!_readFailed) Core.Log.Error("touch swipe read", ex);
            _readFailed = true;
        }
        return IntPtr.Zero;
    }

    private void OnFrame(RawTouch.Frame f)
    {
        long now = Environment.TickCount64;
        if (!f.TouchPad) _rawScreenAt = now;
        if (f.Down >= 3) _busyAt = now;
        var d = (f.TouchPad ? _pad : _screen).Feed(now, f.Down, f.X, f.Y, f.Aspect);
        Fire(d, f.TouchPad ? "touchpad" : "touchscreen");
    }

    private void Touch(TouchEventArgs e, bool down)
    {
        long now = Environment.TickCount64;
        if (down) _touches[e.TouchDevice.Id] = e.GetTouchPoint(null).Position;
        else _touches.Remove(e.TouchDevice.Id);
        // The raw reports of the same screen already count.
        if (now - _rawScreenAt < RawWins) return;
        double w = Math.Max(1, SystemParameters.PrimaryScreenWidth), h = Math.Max(1, SystemParameters.PrimaryScreenHeight);
        double sx = 0, sy = 0;
        foreach (var p in _touches.Values)
        {
            sx += p.X;
            sy += p.Y;
        }
        int n = _touches.Count;
        if (n >= 3) _busyAt = now;
        Fire(_touch.Feed(now, n, n > 0 ? sx / n / w : 0, n > 0 ? sy / n / h : 0, w / h), "touch");
    }

    private void Fire(SwipeDirection d, string source)
    {
        if (d == SwipeDirection.None || !_window.IsVisible) return;
        Core.Log.Write($"touch swipe {d.ToString().ToLowerInvariant()} ({source})");
        _swiped(d);
    }
}
