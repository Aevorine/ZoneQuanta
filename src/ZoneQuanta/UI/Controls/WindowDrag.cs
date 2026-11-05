using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ZoneQuanta.Platform;

namespace ZoneQuanta.UI.Controls;

// Moves a window by dragging part of it. The press is handed to the system caption drag, so Snap and
// dragging a maximized window back out behave exactly like the title bar.
public static class WindowDrag
{
    public const int HoldMs = 300;

    public enum Grip
    {
        None,
        Blank,   // moves at once; double-click maximizes / restores like a title bar
        Content, // buttons, nav items, empty part of a field: moves once the pointer travels, a click stays a click
        Text,    // typed characters: a plain drag selects, a still hold first moves the window
    }

    public static void Begin(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || Mouse.LeftButton != MouseButtonState.Pressed) return;
        Mouse.Capture(null);
        Native.BeginCaptionDrag(hwnd);
    }

    public static void ToggleMaximize(Window window)
    {
        if (window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip)
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    public static void Attach(UIElement area, Window window, Func<MouseButtonEventArgs, Grip> classify)
    {
        Point at = default;
        long downTicks = 0;
        var grip = Grip.None;
        TextBox? box = null;
        int selStart = 0, selLength = 0;
        area.PreviewMouseLeftButtonDown += (_, e) =>
        {
            grip = Grip.None;
            var g = classify(e);
            if (g == Grip.Blank)
            {
                if (e.ClickCount == 2) ToggleMaximize(window);
                else Begin(window);
                e.Handled = true;
                return;
            }
            if (g == Grip.None || e.ClickCount != 1) return;
            grip = g;
            at = e.GetPosition(window);
            downTicks = Environment.TickCount64;
            box = Ancestor<TextBox>(e.OriginalSource);
        };
        // Runs after the field placed its caret: a drag that turns into a window move gives the selection back.
        area.AddHandler(UIElement.MouseLeftButtonDownEvent, new MouseButtonEventHandler((_, _) =>
        {
            if (grip != Grip.None && box is not null) (selStart, selLength) = (box.SelectionStart, box.SelectionLength);
        }), handledEventsToo: true);
        area.PreviewMouseMove += (_, e) =>
        {
            if (grip == Grip.None) return;
            if (e.LeftButton != MouseButtonState.Pressed) { grip = Grip.None; return; }
            var d = e.GetPosition(window) - at;
            if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            var g = grip;
            grip = Grip.None;
            if (g == Grip.Text && Environment.TickCount64 - downTicks < HoldMs) return;
            box?.Select(selStart, selLength);
            Begin(window);
            e.Handled = true;
        };
        area.PreviewMouseLeftButtonUp += (_, _) => grip = Grip.None;
    }

    // Over typed characters of the field (snapToText = false gives -1 on padding, icon and empty space).
    public static bool OnCharacters(TextBox box, MouseEventArgs e) =>
        box.Text.Length > 0 && box.GetCharacterIndexFromPoint(e.GetPosition(box), false) >= 0;

    public static T? Ancestor<T>(object source) where T : DependencyObject
    {
        for (var d = source as DependencyObject; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is T t) return t;
        return null;
    }
}
