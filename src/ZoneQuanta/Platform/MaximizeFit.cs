using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ZoneQuanta.Platform;

// A maximized WindowChrome window fills the work area instead of overhanging the screen edges.
internal static class MaximizeFit
{
    private const int RevealPx = 2;

    public static void Attach(HwndSource source) => source.AddHook((IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) =>
    {
        if (msg == Native.WM_GETMINMAXINFO) Apply(hwnd, l); // left unhandled: WPF still applies MinWidth / MinHeight
        return IntPtr.Zero;
    });

    // Work area without the edges an auto-hide taskbar reveals from (pixels): a window placed over one of
    // them would keep the taskbar from showing, exactly like a maximized one.
    public static System.Drawing.Rectangle UsableArea(System.Drawing.Rectangle work, System.Drawing.Rectangle monitor)
    {
        var r = LeaveRevealEdges(new Native.RECT { Left = work.Left, Top = work.Top, Right = work.Right, Bottom = work.Bottom },
            new Native.RECT { Left = monitor.Left, Top = monitor.Top, Right = monitor.Right, Bottom = monitor.Bottom });
        return System.Drawing.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    public static void Apply(IntPtr hwnd, IntPtr lParam)
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
