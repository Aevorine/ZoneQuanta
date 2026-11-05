using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ZoneQuanta.UI.Panel;

// A nav item dragged straight away moves the window; held still for HoldMs it lifts instead, follows the
// pointer up or down and drops into its new slot (order handed to Save). A short click still navigates.
internal sealed class NavReorder
{
    private static readonly Duration Slide = TimeSpan.FromMilliseconds(140);
    private readonly StackPanel _nav;
    private readonly Action<IReadOnlyList<string>> _save;
    private readonly Action _moveWindow;
    private readonly DispatcherTimer _hold = new() { Interval = TimeSpan.FromMilliseconds(Controls.WindowDrag.HoldMs) };
    private ButtonBase? _pressed, _lifted;
    private Point _downAt;
    private int _from, _to;
    private double _slot;

    public NavReorder(StackPanel nav, Action<IReadOnlyList<string>> save, Action moveWindow)
    {
        _nav = nav;
        _save = save;
        _moveWindow = moveWindow;
        _hold.Tick += (_, _) => Lift();
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
        _pressed = ItemAt(e.OriginalSource);
        if (_pressed is null) return;
        _downAt = e.GetPosition(_nav);
        _hold.Start();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(_nav);
        if (_lifted is null)
        {
            // Moving before the hold elapses drags the window; the item is released, so it is not a click.
            if (_pressed is not null && (Math.Abs(p.X - _downAt.X) > SystemParameters.MinimumHorizontalDragDistance
                || Math.Abs(p.Y - _downAt.Y) > SystemParameters.MinimumVerticalDragDistance))
            {
                Cancel();
                _moveWindow();
                e.Handled = true;
            }
            return;
        }
        double dy = Math.Clamp(p.Y - _downAt.Y, -_from * _slot, (_nav.Children.Count - 1 - _from) * _slot);
        Shift(_lifted).Y = dy;
        int to = Math.Clamp(_from + (int)Math.Round(dy / _slot), 0, _nav.Children.Count - 1);
        if (to != _to)
        {
            _to = to;
            for (int i = 0; i < _nav.Children.Count; i++)
            {
                if (i == _from) continue;
                double y = _from < to && i > _from && i <= to ? -_slot : to < _from && i >= to && i < _from ? _slot : 0;
                Shift((UIElement)_nav.Children[i]).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, Slide));
            }
        }
        e.Handled = true;
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_lifted is not null)
        {
            Drop(commit: true);
            e.Handled = true;
        }
        Cancel();
    }

    private void Cancel()
    {
        _hold.Stop();
        _pressed = null;
    }

    private void Lift()
    {
        _hold.Stop();
        if (_pressed is null || Mouse.LeftButton != MouseButtonState.Pressed) return;
        _lifted = _pressed;
        _pressed = null;
        _from = _to = _nav.Children.IndexOf(_lifted);
        _slot = _lifted.ActualHeight + _lifted.Margin.Top + _lifted.Margin.Bottom;
        System.Windows.Controls.Panel.SetZIndex(_lifted, 1);
        var scale = new ScaleTransform(1, 1);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(new TranslateTransform());
        _lifted.RenderTransformOrigin = new Point(0.5, 0.5);
        _lifted.RenderTransform = group;
        var grow = new DoubleAnimation(1.04, TimeSpan.FromMilliseconds(120));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        _lifted.Opacity = 0.92;
        // Taking the capture also stops the item's own press, so releasing it later is not a click.
        _nav.CaptureMouse();
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
        item.Opacity = 1;
        System.Windows.Controls.Panel.SetZIndex(item, 0);
        if (_nav.IsMouseCaptured) _nav.ReleaseMouseCapture();
        if (!commit || _to == _from) return;

        _nav.Children.RemoveAt(_from);
        _nav.Children.Insert(_to, item);
        // Settle from where it was let go into its new slot.
        var settle = new TranslateTransform(0, dy - (_to - _from) * _slot);
        item.RenderTransform = settle;
        settle.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, Slide));
        _save(_nav.Children.OfType<ContentControl>().Select(c => c.Content as string ?? "").ToList());
    }

    private static TranslateTransform Shift(UIElement e)
    {
        if (e.RenderTransform is TranslateTransform t) return t;
        if (e.RenderTransform is TransformGroup g && g.Children.OfType<TranslateTransform>().FirstOrDefault() is { } inner) return inner;
        var fresh = new TranslateTransform();
        e.RenderTransform = fresh;
        return fresh;
    }
}
