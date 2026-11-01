using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ZoneQuanta.Platform;

// Gives a WindowChrome window a native maximize: it fills the work area instead of overhanging the
// screen, leaves an auto-hide taskbar its reveal edge, and presents a drawn button to Windows as the
// real maximize button, which is what makes Windows 11 offer Snap Layouts on hover.
internal sealed class MaximizeFrame
{
    private const int RevealPx = 2;
    private readonly FrameworkElement _button;
    private bool _hover, _pressed;

    public event Action<bool>? HoverChanged;
    public event Action? Clicked;

    public MaximizeFrame(HwndSource source, FrameworkElement button)
    {
        _button = button;
        source.AddHook(Hook); // hooks run newest first, so this sees messages before WindowChrome does
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        bool onButton = wParam.ToInt64() == Native.HTMAXBUTTON;
        switch (msg)
        {
            case Native.WM_GETMINMAXINFO:
                FitWorkArea(hwnd, lParam); // left unhandled: WPF still applies MinWidth / MinHeight
                break;
            case Native.WM_NCHITTEST:
                if (Contains(lParam))
                {
                    handled = true;
                    return new IntPtr(Native.HTMAXBUTTON);
                }
                break;
            case Native.WM_SETCURSOR:
                if ((lParam.ToInt64() & 0xFFFF) == Native.HTMAXBUTTON)
                {
                    Native.SetCursor(Native.LoadCursor(IntPtr.Zero, new IntPtr(Native.IDC_HAND)));
                    handled = true;
                    return new IntPtr(1);
                }
                break;
            case Native.WM_NCMOUSEMOVE:
                if (onButton && !_hover) TrackLeave(hwnd);
                SetHover(onButton);
                break;
            case Native.WM_NCMOUSELEAVE:
                _pressed = false;
                SetHover(false);
                break;
            case Native.WM_NCLBUTTONDOWN:
            case Native.WM_NCLBUTTONDBLCLK:
                if (onButton)
                {
                    _pressed = true;
                    handled = true; // DefWindowProc would paint the classic button over ours
                }
                break;
            case Native.WM_NCLBUTTONUP:
                if (onButton && _pressed)
                {
                    handled = true;
                    SetHover(false);
                    Clicked?.Invoke();
                }
                _pressed = false;
                break;
        }
        return IntPtr.Zero;
    }

    private bool Contains(IntPtr lParam)
    {
        if (!_button.IsVisible) return false;
        long v = lParam.ToInt64();
        var p = _button.PointFromScreen(new Point((short)v, (short)(v >> 16)));
        return p.X >= 0 && p.Y >= 0 && p.X < _button.ActualWidth && p.Y < _button.ActualHeight;
    }

    private void SetHover(bool on)
    {
        if (on == _hover) return;
        _hover = on;
        HoverChanged?.Invoke(on);
    }

    private static void TrackLeave(IntPtr hwnd)
    {
        var track = new Native.TRACKMOUSEEVENT
        {
            cbSize = Marshal.SizeOf<Native.TRACKMOUSEEVENT>(),
            dwFlags = Native.TME_LEAVE | Native.TME_NONCLIENT,
            hwndTrack = hwnd,
        };
        Native.TrackMouseEvent(ref track);
    }

    private static void FitWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        IntPtr monitor = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST);
        var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        if (monitor == IntPtr.Zero || !Native.GetMonitorInfo(monitor, ref mi)) return;

        var area = LeaveRevealEdges(mi.rcWork, mi.rcMonitor);
        var info = Marshal.PtrToStructure<Native.MINMAXINFO>(lParam);
        info.MaxPosition = new Native.POINT { X = area.Left - mi.rcMonitor.Left, Y = area.Top - mi.rcMonitor.Top };
        info.MaxSize = new Native.POINT { X = area.Right - area.Left, Y = area.Bottom - area.Top };
        Marshal.StructureToPtr(info, lParam, false);
    }

    // An auto-hide taskbar reserves no work area, and Explorer stops revealing it once a window's client
    // area covers the monitor (it takes that for a full-screen app). Staying clear of its edge prevents that.
    private static Native.RECT LeaveRevealEdges(Native.RECT work, Native.RECT monitor)
    {
        var bar = new Native.APPBARDATA { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), rc = monitor };
        for (uint edge = 0; edge < 4; edge++)
        {
            bar.uEdge = edge;
            if (Native.SHAppBarMessage(Native.ABM_GETAUTOHIDEBAREX, ref bar) == IntPtr.Zero) continue;
            switch (edge)
            {
                case 0: work.Left += RevealPx; break;
                case 1: work.Top += RevealPx; break;
                case 2: work.Right -= RevealPx; break;
                default: work.Bottom -= RevealPx; break;
            }
        }
        return work;
    }
}
