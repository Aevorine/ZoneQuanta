using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace ZoneQuanta.UI.Panel;

// Dragging a nav item up or down moves the item: it lifts as soon as the pointer travels, follows it, the
// others slide aside, and the release drops it into its slot (order handed to Save). Dragging it sideways
// moves the window instead, like the sidebar's empty parts, and so does pulling a lifted item out of the bar
// sideways. No hold either way; a click still navigates; Alt+Up / Alt+Down moves the selected item.
internal sealed class NavReorder
{
    private static readonly Duration Slide = TimeSpan.FromMilliseconds(140);
    private readonly StackPanel _nav;
    private readonly Action<IReadOnlyList<string>> _save;
    private ButtonBase? _pressed, _lifted;
    private Point _downAt;
    private int _from, _to;
    private double _slot;

    public NavReorder(StackPanel nav, Action<IReadOnlyList<string>> save)
    {
        _nav = nav;
        _save = save;
        nav.PreviewMouseLeftButtonDown += OnDown;
        nav.PreviewMouseMove += OnMove;
        nav.PreviewMouseLeftButtonUp += OnUp;
        // LostMouseCapture bubbles: the pressed item losing capture to the nav must not end the lift.
        nav.LostMouseCapture += (_, e) => { if (_lifted is not null && e.OriginalSource == nav) Drop(commit: false); };
    }

    private ButtonBase? ItemAt(object source)
    {
        for (var d = source as DependencyObject; d is not null && d != _nav; d = VisualTreeHelper.GetParent(d))
            if (d is ButtonBase b && _nav.Children.Contains(b)) return b;
        return null;
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _pressed = e.ClickCount == 1 ? ItemAt(e.OriginalSource) : null;
        if (_pressed is not null) _downAt = e.GetPosition(_nav);
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(_nav);
        // The release was never seen (it went to another window or a menu loop): end the lift, change nothing.
        if (_lifted is not null && (e.LeftButton != MouseButtonState.Pressed || !Platform.Native.LeftButtonDown()))
        {
            Drop(commit: false);
            return;
        }
        if (_lifted is null)
        {
            if (_pressed is null) return;
            if (e.LeftButton != MouseButtonState.Pressed) { _pressed = null; return; }
            double dx = p.X - _downAt.X, dy = p.Y - _downAt.Y;
            if (dx * dx + dy * dy < DecideAt * DecideAt) return;
            var item = _pressed;
            _pressed = null;
            // The bar runs up and down: along it is a reorder, across it a window move.
            if (Math.Abs(dx) > Math.Abs(dy))
            {
                MoveWindow(item, "across");
                e.Handled = true;
                return;
            }
            Lift(item);
        }
        // Pulled well out of the bar sideways: the window follows instead and the item goes back.
        if (Math.Abs(p.X - _downAt.X) > _nav.ActualWidth * 0.5 + PullOut)
        {
            var item = _lifted!;
            Drop(commit: false);
            MoveWindow(item, "pulled out");
            e.Handled = true;
            return;
        }
        Follow(p.Y - _downAt.Y);
        e.Handled = true;
    }

    // Pointer travel before the direction is judged: a little more than the system's drag threshold, so a
    // slightly slanted start is not misread.
    private const double DecideAt = 7, PullOut = 36;

    private void MoveWindow(ButtonBase item, string how)
    {
        if (Window.GetWindow(_nav) is not { } window) return;
        Core.Log.Write($"nav window-move {(item as ContentControl)?.Content} ({how})");
        Controls.WindowDrag.Begin(window);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        _pressed = null;
        if (_lifted is null) return;
        Drop(commit: true);
        e.Handled = true;
    }

    // Alt+Up / Alt+Down anywhere in the window (nav items take no focus): the current page's item moves one
    // slot with the same slide as a drag. Not while a list or text field has focus: Alt+Down opens a list there.
    public void OnWindowKey(object sender, KeyEventArgs e)
    {
        if (_lifted is not null || e.Key != Key.System || e.SystemKey is not (Key.Up or Key.Down)) return;
        if (Keyboard.FocusedElement is DependencyObject f && Controls.WindowDrag.Ancestor<Control>(f) is ComboBox or TextBoxBase) return;
        var item = _nav.Children.OfType<ToggleButton>().FirstOrDefault(t => t.IsChecked == true);
        if (item is null) return;
        int from = _nav.Children.IndexOf(item), to = from + (e.SystemKey == Key.Up ? -1 : 1);
        e.Handled = true;
        if (to < 0 || to >= _nav.Children.Count) return;
        double slot = item.ActualHeight + item.Margin.Top + item.Margin.Bottom;
        _nav.Children.RemoveAt(from);
        _nav.Children.Insert(to, item);
        Settle(item, (from - to) * slot);
        Core.Log.Write($"nav key-move {(item as ContentControl)?.Content} {from}->{to}");
        _save(Order());
    }

    private void Lift(ButtonBase item)
    {
        _lifted = item;
        _from = _to = _nav.Children.IndexOf(item);
        _slot = item.ActualHeight + item.Margin.Top + item.Margin.Bottom;
        System.Windows.Controls.Panel.SetZIndex(item, 1);
        var scale = new ScaleTransform(1, 1);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(new TranslateTransform());
        item.RenderTransformOrigin = new Point(0.5, 0.5);
        item.RenderTransform = group;
        var grow = new DoubleAnimation(1.04, TimeSpan.FromMilliseconds(120));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        item.Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Direction = 270, Opacity = 0.18 };
        // Taking the capture also ends the item's own press, so releasing it later is not a click.
        _nav.CaptureMouse();
        Core.Log.Write($"nav lift {(item as ContentControl)?.Content}");
    }

    // The lifted item tracks the pointer (kept inside the list); the slot it would land in opens up.
    private void Follow(double dy)
    {
        dy = Math.Clamp(dy, -_from * _slot, (_nav.Children.Count - 1 - _from) * _slot);
        Shift(_lifted!).Y = dy;
        int to = Math.Clamp(_from + (int)Math.Round(dy / _slot), 0, _nav.Children.Count - 1);
        if (to == _to) return;
        _to = to;
        for (int i = 0; i < _nav.Children.Count; i++)
        {
            if (i == _from) continue;
            double y = _from < to && i > _from && i <= to ? -_slot : to < _from && i >= to && i < _from ? _slot : 0;
            Shift((UIElement)_nav.Children[i]).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, Slide));
        }
    }

    private void Drop(bool commit)
    {
        var item = _lifted!;
        _lifted = null;
        double dy = Shift(item).Y;
        foreach (UIElement c in _nav.Children)
        {
            var t = Shift(c);
            t.BeginAnimation(TranslateTransform.YProperty, null);
            t.Y = 0;
        }
        item.RenderTransform = null;
        item.Effect = null;
        System.Windows.Controls.Panel.SetZIndex(item, 0);
        if (_nav.IsMouseCaptured) _nav.ReleaseMouseCapture();
        Core.Log.Write($"nav drop {(item as ContentControl)?.Content} {_from}->{_to} {(commit ? "commit" : "cancel")}");
        if (!commit || _to == _from)
        {
            if (dy != 0) Settle(item, dy);
            return;
        }
        _nav.Children.RemoveAt(_from);
        _nav.Children.Insert(_to, item);
        Settle(item, dy - (_to - _from) * _slot);
        _save(Order());
    }

    // Slides an item from offset to its place.
    private static void Settle(UIElement item, double offset)
    {
        var t = new TranslateTransform(0, offset);
        item.RenderTransform = t;
        t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, Slide) { EasingFunction = new CubicEase() });
    }

    private List<string> Order() => _nav.Children.OfType<ContentControl>().Select(c => c.Content as string ?? "").ToList();

    private static TranslateTransform Shift(UIElement e)
    {
        if (e.RenderTransform is TranslateTransform t) return t;
        if (e.RenderTransform is TransformGroup g && g.Children.OfType<TranslateTransform>().FirstOrDefault() is { } inner) return inner;
        var fresh = new TranslateTransform();
        e.RenderTransform = fresh;
        return fresh;
    }
}
