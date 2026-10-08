using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ZoneQuanta.Platform;

internal static class FullscreenWatcher
{
    private static readonly uint SelfPid = (uint)Environment.ProcessId;

    public static bool IsFullscreenActive()
    {
        IntPtr hwnd = Native.GetForegroundWindow();
        // Showing the desktop (Win+D, Win+M, the corner button, a click on the wallpaper) makes Windows
        // report "busy" exactly as it does for a full-screen app. It is not one: the clock and the band
        // must stay on screen then, so shell windows, our own windows and "no foreground" never count.
        if (hwnd == IntPtr.Zero || IsShellWindow(hwnd) || OwnedBySelf(hwnd)) return false;

        if (Native.SHQueryUserNotificationState(out int state) == 0 && (state == 2 || state == 3 || state == 4))
            return true;
        return ForegroundCoversMonitor(hwnd);
    }

    public static bool IsDesktopForeground()
    {
        IntPtr hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || OwnedBySelf(hwnd)) return false;
        return ClassOf(hwnd) is "Progman" or "WorkerW";
    }

    private static bool OwnedBySelf(IntPtr hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == SelfPid;
    }

    private static bool IsShellWindow(IntPtr hwnd) =>
        ClassOf(hwnd) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";

    private static string ClassOf(IntPtr hwnd)
    {
        var cls = new StringBuilder(64);
        Native.GetClassName(hwnd, cls, cls.Capacity);
        return cls.ToString();
    }

    private static bool ForegroundCoversMonitor(IntPtr hwnd)
    {
        if (!Native.GetWindowRect(hwnd, out var r)) return false;
        IntPtr mon = Native.MonitorFromWindow(hwnd, 2);
        var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        if (mon == IntPtr.Zero || !Native.GetMonitorInfo(mon, ref mi)) return false;

        var m = mi.rcMonitor;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        int mw = m.Right - m.Left, mh = m.Bottom - m.Top;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom && w <= mw + 2 && h <= mh + 2;
    }
}
