using System;
using System.Windows;
using System.Windows.Input;

namespace ZoneQuanta.UI.Controls;

// Moves a window by pressing on an area and dragging. Blank areas move at once; over interactive
// content (a text field, buttons) the press must first be held still for HoldMs, so a quick drag keeps
// its usual meaning (selecting text) and a click stays a click.
public static class WindowDrag
{
    public const int HoldMs = 300;

    // holdOnContent = false: only blank areas move the window (the content has its own long press).
    public static void Attach(UIElement area, Window window, Func<object, bool> isBlank, bool holdOnContent = true)
    {
        Point at = default;
        long downTicks = 0;
        bool armed = false;
        area.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 1 || window.WindowState != WindowState.Normal) { armed = false; return; }
            if (isBlank(e.OriginalSource))
            {
                armed = false;
                window.DragMove();
                e.Handled = true;
                return;
            }
            at = e.GetPosition(window);
            downTicks = Environment.TickCount64;
            armed = holdOnContent;
        };
        area.PreviewMouseMove += (_, e) =>
        {
            if (!armed || e.LeftButton != MouseButtonState.Pressed) { armed = false; return; }
            var d = e.GetPosition(window) - at;
            if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            armed = false;
            // Moved before the hold elapsed: an ordinary drag inside the content, not a window move.
            if (Environment.TickCount64 - downTicks < HoldMs) return;
            Mouse.Capture(null);
            window.DragMove();
            e.Handled = true;
        };
        area.PreviewMouseLeftButtonUp += (_, _) => armed = false;
    }
}
