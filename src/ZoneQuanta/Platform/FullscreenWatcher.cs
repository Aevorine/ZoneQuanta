using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ZoneQuanta.Platform;

internal static class FullscreenWatcher
{
    private static readonly uint SelfPid = (uint)Environment.ProcessId;

    public static bool IsFullscreenActive()
    {
        if (Native.SHQueryUserNotificationState(out int state) == 0 && (state == 2 || state == 3 || state == 4))
            return true;
        return ForegroundCoversMonitor();
    }

    public static bool IsDesktopForeground()
    {
        IntPtr hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == SelfPid) return false;

        var cls = new StringBuilder(32);
        Native.GetClassName(hwnd, cls, cls.Capacity);
        return cls.ToString() is "Progman" or "WorkerW";
    }

    private static bool ForegroundCoversMonitor()
    {
        IntPtr hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == SelfPid) return false;

        var cls = new StringBuilder(64);
        Native.GetClassName(hwnd, cls, cls.Capacity);
        string name = cls.ToString();
        if (name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;

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
