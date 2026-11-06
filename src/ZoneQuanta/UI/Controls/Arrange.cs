using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ZoneQuanta.Core.Settings;

namespace ZoneQuanta.UI.Controls;

using Panel = System.Windows.Controls.Panel;

// Page content that can be picked up and put elsewhere. A panel opts in with Arrange.Key; its children take
// part through Arrange.Id (or their text). Grid children swap cells, other panels swap positions. The order is
// kept in settings (Layouts) unless the panel hands it to a commit handler (time zones reorder the clocks).
public static class Arrange
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(Arrange), new PropertyMetadata(null, OnKeyChanged));
    public static readonly DependencyProperty IdProperty = DependencyProperty.RegisterAttached("Id", typeof(string), typeof(Arrange));
    // Presses inside are left alone (content with its own dragging, such as the quick grid).
    public static readonly DependencyProperty IgnoreProperty = DependencyProperty.RegisterAttached("Ignore", typeof(bool), typeof(Arrange));

    public static string? GetKey(DependencyObject d) => (string?)d.GetValue(KeyProperty);
    public static void SetKey(DependencyObject d, string? v) => d.SetValue(KeyProperty, v);
    public static string? GetId(DependencyObject d) => (string?)d.GetValue(IdProperty);
    public static void SetId(DependencyObject d, string? v) => d.SetValue(IdProperty, v);
    public static bool GetIgnore(DependencyObject d) => (bool)d.GetValue(IgnoreProperty);
    public static void SetIgnore(DependencyObject d, bool v) => d.SetValue(IgnoreProperty, v);

    public static AppSettings? Settings { get; set; }

    private static readonly Duration Slide = TimeSpan.FromMilliseconds(170);
    private static readonly ConditionalWeakTable<Panel, List<string>> Defaults = new();
    private static readonly ConditionalWeakTable<Panel, Action<IReadOnlyList<string>>> Commits = new();

    public static void SetCommit(Panel panel, Action<IReadOnlyList<string>> commit) => Commits.AddOrUpdate(panel, commit);

    private static void OnKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Panel p && e.OldValue is null) p.Loaded += (_, _) => ApplySaved(p);
    }

    private static string? IdOf(UIElement e) =>
        GetId(e) ?? (e is ContentControl { Content: string s } && s.Length > 0 ? s : null);

    private static List<UIElement> Items(Panel p) => p.Children.OfType<UIElement>().Where(c => IdOf(c) is not null).ToList();

    // Items in the order of their places: cells row by row for a grid, positions for any other panel.
    private static List<UIElement> Ordered(Panel p) => p is Grid
        ? Items(p).OrderBy(Grid.GetRow).ThenBy(Grid.GetColumn).ToList()
        : Items(p);

    private static List<string> Ids(IEnumerable<UIElement> items) => items.Select(i => IdOf(i)!).ToList();

    private static void ApplySaved(Panel p)
    {
        string? key = GetKey(p);
        if (key is null) return;
        if (!Defaults.TryGetValue(p, out _)) Defaults.Add(p, Ids(Ordered(p)));
        if (Commits.TryGetValue(p, out _)) return;
        if (Settings?.Layouts.TryGetValue(key, out var saved) == true) Place(p, Sorted(p, saved));
    }

    // Saved ids first, in their order; items the saved list does not know keep their relative order after them.
    private static List<UIElement> Sorted(Panel p, IReadOnlyList<string> ids)
    {
        var current = Ordered(p);
        var byId = current.ToDictionary(i => IdOf(i)!, i => i);
        var result = ids.Where(byId.ContainsKey).Distinct().Select(id => byId[id]).ToList();
        result.AddRange(current.Where(i => !result.Contains(i)));
        return result;
    }

    private static void Place(Panel p, List<UIElement> order)
    {
        if (p is Grid)
        {
            var cells = Ordered(p).Select(i => (Row: Grid.GetRow(i), Col: Grid.GetColumn(i), RowSpan: Grid.GetRowSpan(i), ColSpan: Grid.GetColumnSpan(i))).ToList();
            for (int k = 0; k < order.Count && k < cells.Count; k++)
            {
                Grid.SetRow(order[k], cells[k].Row);
                Grid.SetColumn(order[k], cells[k].Col);
                Grid.SetRowSpan(order[k], cells[k].RowSpan);
                Grid.SetColumnSpan(order[k], cells[k].ColSpan);
            }
            return;
        }
        var slots = Items(p).Select(i => p.Children.IndexOf(i)).OrderBy(i => i).ToList();
        if (Items(p).SequenceEqual(order)) return;
        foreach (var i in order) p.Children.Remove(i);
        for (int k = 0; k < order.Count; k++) p.Children.Insert(Math.Min(slots[k], p.Children.Count), order[k]);
    }

    // Nearest item the press is on, innermost first (a switch inside a card before the card).
    public static UIElement? ItemAt(DependencyObject? source, DependencyObject stop)
    {
        for (var d = source; d is not null && d != stop; d = Parent(d))
        {
            if (d is UIElement u && VisualTreeHelper.GetParent(u) is Panel p && GetKey(p) is not null && IdOf(u) is not null && u.IsEnabled) return u;
        }
        return null;
    }

    public static DependencyObject? Parent(DependencyObject d) =>
        d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

    // Every arranged panel of a page, for "restore default layout".
    public static List<Panel> PanelsIn(DependencyObject root)
    {
        var list = new List<Panel>();
        void Walk(DependencyObject d)
        {
            if (d is Panel p && GetKey(p) is not null && !Commits.TryGetValue(p, out _)) list.Add(p);
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(root);
        return list;
    }

    public static bool HasSaved(Panel p) => GetKey(p) is { } k && Settings?.Layouts.ContainsKey(k) == true;

    public static void Reset(Panel p)
    {
        if (GetKey(p) is not { } key) return;
        if (Defaults.TryGetValue(p, out var ids)) Place(p, Sorted(p, ids));
        Settings?.SetLayout(key, null);
        Core.Log.Write($"arrange reset {key}");
    }

    // One pick-up: follows the pointer, the others slide aside, the drop keeps the new order.
    public sealed class Lift
    {
        private readonly Panel _panel;
        private readonly UIElement _item;
        private readonly List<string> _before;
        private readonly Vector _grab;
        private readonly TranslateTransform _move = new();
        private readonly ScaleTransform _scale = new(1, 1);
        private UIElement? _lastSwap;

        public Lift(UIElement item, MouseEventArgs e)
        {
            _item = item;
            _panel = (Panel)VisualTreeHelper.GetParent(item);
            _before = Ids(Ordered(_panel));
            _grab = e.GetPosition(_panel) - Origin(item);
            var group = new TransformGroup();
            group.Children.Add(_scale);
            group.Children.Add(_move);
            item.RenderTransformOrigin = new Point(0.5, 0.5);
            item.RenderTransform = group;
            Panel.SetZIndex(item, 10);
            var grow = new DoubleAnimation(1.035, TimeSpan.FromMilliseconds(130)) { EasingFunction = new CubicEase() };
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            item.Opacity = 0.95;
            if (item is Control c) c.SetResourceReference(Control.BackgroundProperty, "RaisedBrush");
            item.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.16 };
            _panel.CaptureMouse();
            Core.Log.Write($"arrange lift {GetKey(_panel)}/{IdOf(item)}");
        }

        public Panel Panel => _panel;

        private static Point Origin(UIElement e) => (Point)VisualTreeHelper.GetOffset(e);
        private static Rect Bounds(UIElement e) => new(Origin(e), e.RenderSize);

        public void Move(MouseEventArgs e)
        {
            var p = e.GetPosition(_panel);
            if (_lastSwap is not null && !Bounds(_lastSwap).Contains(p)) _lastSwap = null;
            var order = Ordered(_panel);
            var target = order.FirstOrDefault(i => i != _item && i != _lastSwap && i.IsVisible && Bounds(i).Contains(p));
            if (target is not null)
            {
                var before = order.ToDictionary(i => i, Origin);
                int to = order.IndexOf(target);
                order.Remove(_item);
                order.Insert(to, _item);
                Place(_panel, order);
                _panel.UpdateLayout();
                foreach (var i in order)
                {
                    if (i == _item) continue;
                    var d = before[i] - Origin(i);
                    if (d.Length < 0.5) continue;
                    var t = new TranslateTransform(d.X, d.Y);
                    i.RenderTransform = t;
                    var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                    t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, Slide) { EasingFunction = ease });
                    t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, Slide) { EasingFunction = ease });
                }
                _lastSwap = target;
            }
            var at = p - _grab - Origin(_item);
            _move.X = at.X;
            _move.Y = at.Y;
        }

        public void Drop(bool commit)
        {
            if (_panel.IsMouseCaptured) _panel.ReleaseMouseCapture();
            var shown = Origin(_item) + new Vector(_move.X, _move.Y);
            if (!commit) Place(_panel, Sorted(_panel, _before));
            _panel.UpdateLayout();
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var from = shown - Origin(_item);
            var settle = new TranslateTransform(from.X, from.Y);
            _item.RenderTransform = settle;
            settle.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, Slide) { EasingFunction = ease });
            settle.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, Slide) { EasingFunction = ease });
            _item.Opacity = 1;
            _item.Effect = null;
            if (_item is Control c) c.ClearValue(Control.BackgroundProperty);
            Panel.SetZIndex(_item, 0);
            var now = Ids(Ordered(_panel));
            string key = GetKey(_panel)!;
            Core.Log.Write($"arrange drop {key}/{IdOf(_item)} {(commit && !now.SequenceEqual(_before) ? "moved" : "unchanged")}");
            if (!commit || now.SequenceEqual(_before)) return;
            if (Commits.TryGetValue(_panel, out var handler)) handler(now);
            else Settings?.SetLayout(key, now);
        }
    }
}

// Presses on page content: moving at once drags the window (a click stays a click); holding still for
// WindowDrag.HoldMs on an arrangeable card, switch or tile picks it up instead.
internal sealed class ContentDrag
{
    private readonly FrameworkElement _area;
    private readonly Window _window;
    private readonly System.Windows.Threading.DispatcherTimer _hold = new() { Interval = TimeSpan.FromMilliseconds(WindowDrag.HoldMs) };
    private UIElement? _candidate;
    private Arrange.Lift? _lift;
    private Point _downAt;
    private bool _armed;
    private MouseEventArgs? _last;

    public ContentDrag(FrameworkElement area, Window window)
    {
        _area = area;
        _window = window;
        _hold.Tick += (_, _) => PickUp();
        area.PreviewMouseLeftButtonDown += OnDown;
        area.PreviewMouseMove += OnMove;
        area.PreviewMouseLeftButtonUp += OnUp;
        // LostMouseCapture bubbles from children: only the lifted panel losing it ends the lift.
        area.LostMouseCapture += (_, e) =>
        {
            if (_lift is not null && e.OriginalSource == _lift.Panel) End(commit: false);
        };
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        Reset();
        if (e.ClickCount != 1 || e.OriginalSource is not DependencyObject src || Excluded(src, e)) return;
        _armed = true;
        _downAt = e.GetPosition(_window);
        _last = e;
        _candidate = Arrange.ItemAt(src, _area);
        if (_candidate is not null) _hold.Start();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_lift is not null)
        {
            if (e.LeftButton != MouseButtonState.Pressed || !Platform.Native.LeftButtonDown()) End(commit: false); // release went elsewhere
            else _lift.Move(e);
            e.Handled = true;
            return;
        }
        if (!_armed) return;
        _last = e;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            Reset();
            return;
        }
        var d = e.GetPosition(_window) - _downAt;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        Reset();
        WindowDrag.Begin(_window);
        e.Handled = true;
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_lift is not null)
        {
            End(commit: true);
            e.Handled = true;
        }
        Reset();
    }

    private void PickUp()
    {
        _hold.Stop();
        // The page may have rebuilt the item meanwhile (a zone rename commits on focus loss): lift only a live one.
        if (!_armed || _candidate is null || _last is null || Mouse.LeftButton != MouseButtonState.Pressed || !Platform.Native.LeftButtonDown()
            || !_candidate.IsVisible || VisualTreeHelper.GetParent(_candidate) is not Panel p || Arrange.GetKey(p) is null)
        {
            Reset();
            return;
        }
        _armed = false;
        _lift = new Arrange.Lift(_candidate, _last);
    }

    private void End(bool commit)
    {
        var lift = _lift;
        _lift = null;
        lift?.Drop(commit);
    }

    private void Reset()
    {
        _hold.Stop();
        _armed = false;
        _candidate = null;
    }

    // Controls whose own dragging matters (sliders, scroll bars, lists, typed text), other windows (popups,
    // menus) and content marked Arrange.Ignore.
    private bool Excluded(DependencyObject src, MouseButtonEventArgs e)
    {
        if (PresentationSource.FromDependencyObject(src) is { } ps && ps != PresentationSource.FromVisual(_window)) return true;
        for (var d = src; d is not null && d != _area; d = Arrange.Parent(d))
        {
            if (d is Slider or ScrollBar or Thumb or ListBox or ComboBox or PasswordBox or RichTextBox or Popup) return true;
            if (Arrange.GetIgnore(d)) return true;
            if (d is TextBox box && WindowDrag.OnCharacters(box, e)) return true;
        }
        return false;
    }
}
