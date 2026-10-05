using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;

namespace ZoneQuanta.Platform;

// Coordinates are relative to Explorer's taskbar, so auto-hide movement does
// not invalidate the layout. Only rectangles are retained, never app names.
internal sealed record TaskbarLayout(double Width, double Height, Rect[] Occupied)
{
    public static TaskbarLayout? Read(IntPtr tray)
    {
        try
        {
            if (!Native.GetWindowRect(tray, out var bounds)) return null;
            var root = AutomationElement.FromHandle(tray);
            var elements = root.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition);
            var occupied = new List<Rect>();
            bool startFound = false, trayFound = false;
            foreach (AutomationElement element in elements)
            {
                var info = element.Current;
                var rect = info.BoundingRectangle;
                if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0) continue;
                string id = info.AutomationId, cls = info.ClassName;
                bool start = id.Contains("StartButton", StringComparison.OrdinalIgnoreCase) ||
                    info.Name is "Start" or "开始";
                bool notification = id.Contains("SystemTray", StringComparison.OrdinalIgnoreCase) ||
                    cls.Contains("SystemTray", StringComparison.OrdinalIgnoreCase);
                bool interactive = info.ControlType == ControlType.Button ||
                    info.ControlType == ControlType.ListItem || info.ControlType == ControlType.TabItem ||
                    info.ControlType == ControlType.CheckBox || info.ControlType == ControlType.RadioButton ||
                    info.ControlType == ControlType.Edit || info.ControlType == ControlType.MenuItem ||
                    info.ControlType == ControlType.Hyperlink ||
                    (bool)element.GetCurrentPropertyValue(AutomationElement.IsInvokePatternAvailableProperty);
                if (!start && !notification && !interactive) continue;
                rect.Offset(-bounds.Left, -bounds.Top);
                rect.Intersect(new Rect(0, 0, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
                if (rect.IsEmpty) continue;
                occupied.Add(rect);
                startFound |= start;
                trayFound |= notification;
            }
            IntPtr notify = Native.FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
            if (notify != IntPtr.Zero && Native.GetWindowRect(notify, out var n))
            {
                occupied.Add(new Rect(n.Left - bounds.Left, n.Top - bounds.Top, n.Right - n.Left, n.Bottom - n.Top));
                trayFound = true;
            }
            // Unrecognized shell layouts must not fall back to painting over icons.
            return startFound && trayFound
                ? new TaskbarLayout(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, occupied.ToArray())
                : null;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or ArgumentException or COMException)
        {
            return null;
        }
    }

    public Rect FreeArea(string position, double padding)
    {
        var spans = new List<(double Left, double Right)>();
        foreach (var rect in Occupied)
            spans.Add((Math.Max(0, rect.Left - padding), Math.Min(Width, rect.Right + padding)));
        spans.Sort((a, b) => a.Left.CompareTo(b.Left));
        var gaps = new List<Rect>();
        double cursor = padding;
        foreach (var span in spans)
        {
            if (span.Left > cursor) gaps.Add(new Rect(cursor, 0, span.Left - cursor, Height));
            cursor = Math.Max(cursor, span.Right);
        }
        if (Width - padding > cursor) gaps.Add(new Rect(cursor, 0, Width - padding - cursor, Height));
        if (gaps.Count == 0) return Rect.Empty;
        // Prefer a substantial gap near the requested side, rather than tiny
        // spaces between buttons. Position offsets are clamped to this gap.
        double widest = 0;
        foreach (var gap in gaps) widest = Math.Max(widest, gap.Width);
        gaps.RemoveAll(g => g.Width < widest * 0.75);
        return position == "Right" ? gaps[^1] : gaps[0];
    }
}
