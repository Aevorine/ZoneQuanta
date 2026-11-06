using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ZoneQuanta.Core.Settings;

namespace ZoneQuanta.UI.Quick;

// Spacing of the grouped board: presets, six sliders, reset. Changes apply live (the board rebuilds once a
// slider rests for 90 ms). A slider shows the value in effect, the default included; double-click its name
// to return that one to the default.
public sealed class SpacingPanel : Border
{
    private sealed record Row(string Label, Func<AppSettings, int> Raw, Func<BoardMetrics, AppSettings, double> Shown, Action<AppSettings, int> Set, int Min, int Max, int Step);

    private static readonly Row[] Rows =
    {
        new("分组间距", s => s.QuickGroupGap == 14 ? -1 : s.QuickGroupGap, (m, _) => m.Group, (s, v) => s.QuickGroupGap = v < 0 ? 14 : v, 0, 40, 1),
        new("横向间距", s => s.QuickGapX, (m, _) => m.GapX, (s, v) => s.QuickGapX = v, 0, 80, 1),
        new("纵向间距", s => s.QuickGapY, (m, _) => m.GapY, (s, v) => s.QuickGapY = v, 0, 64, 1),
        new("左右边距", s => s.QuickPadX, (m, _) => m.PadX, (s, v) => s.QuickPadX = v, 0, 56, 1),
        new("上下边距", s => s.QuickPadY, (m, _) => m.PadY, (s, v) => s.QuickPadY = v, 0, 48, 1),
    };

    // Gap X, gap Y, border X, border Y, group gap (-1: default); fill width. The icon size never changes.
    private static readonly (string Name, int[] V, bool Fill)[] Presets =
    {
        ("紧凑", new[] { 18, 6, 10, 4, 10 }, false),
        ("标准", new[] { -1, -1, -1, -1, 14 }, true),
        ("宽松", new[] { 56, 34, 36, 28, 18 }, true),
    };

    private readonly AppSettings _s;
    private readonly List<(Row Row, Slider Slider, TextBlock Value, TextBlock Label)> _rows = new();
    private readonly List<RadioButton> _presets = new();
    private readonly Button _reset;
    private readonly ToggleButton _fill = new() { Content = "铺满宽度", Margin = new Thickness(-12, 4, -8, 0) };
    private bool _syncing;

    public SpacingPanel(AppSettings s)
    {
        _s = s;
        Width = 360;
        Padding = new Thickness(16, 12, 12, 14);
        CornerRadius = new CornerRadius(14);
        BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, "CardBrush");
        SetResourceReference(BorderBrushProperty, "LineBrush");

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        _reset = new Button { Tag = Application.Current.FindResource("Icon.Reset"), ToolTip = "恢复默认", Margin = new Thickness(0, -6, -4, -6) };
        _reset.SetResourceReference(StyleProperty, "IconButton");
        _reset.Click += (_, _) => Apply(Presets[1].V, Presets[1].Fill);
        DockPanel.SetDock(_reset, Dock.Right);
        head.Children.Add(_reset);
        var title = new TextBlock { Text = "间距", VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(StyleProperty, "Heading");
        head.Children.Add(title);

        var presets = new UniformGrid { Rows = 1, Margin = new Thickness(0, 0, 4, 12) };
        foreach (var (name, v, fill) in Presets)
        {
            var r = new RadioButton { Content = name, GroupName = "quick-spacing-" + GetHashCode(), Margin = new Thickness(0, 0, 6, 0) };
            r.SetResourceReference(StyleProperty, "Segment");
            r.Click += (_, _) => Apply(v, fill);
            _presets.Add(r);
            presets.Children.Add(r);
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        for (int i = 0; i < Rows.Length; i++)
        {
            var row = Rows[i];
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
            var label = new TextBlock { Text = row.Label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0), Background = Brushes.Transparent };
            label.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount != 2) return;
                row.Set(_s, -1);
                e.Handled = true;
            };
            var slider = new Slider { Minimum = row.Min, Maximum = row.Max, SmallChange = row.Step, LargeChange = row.Step * 4, IsSnapToTickEnabled = true, TickFrequency = row.Step, VerticalAlignment = VerticalAlignment.Center };
            slider.SetResourceReference(StyleProperty, "Range");
            System.Windows.Automation.AutomationProperties.SetName(slider, row.Label);
            slider.ValueChanged += (_, e) =>
            {
                if (_syncing) return;
                row.Set(_s, (int)Math.Round(e.NewValue));
            };
            var value = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            value.SetResourceReference(StyleProperty, "Muted");
            System.Windows.Documents.Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
            Grid.SetRow(label, i);
            Grid.SetRow(slider, i);
            Grid.SetColumn(slider, 1);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 2);
            grid.Children.Add(label);
            grid.Children.Add(slider);
            grid.Children.Add(value);
            _rows.Add((row, slider, value, label));
        }

        _fill.SetResourceReference(StyleProperty, "SwitchRow");
        _fill.Click += (_, _) => { if (!_syncing) _s.QuickFill = _fill.IsChecked == true; };

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(presets);
        stack.Children.Add(grid);
        stack.Children.Add(_fill);
        Child = stack;
        Sync();
        PropertyChangedEventHandler h = (_, e) => { if (e.PropertyName is { } n && (n.StartsWith("QuickGap") || n.StartsWith("QuickPad") || n is nameof(AppSettings.QuickGroupGap) or nameof(AppSettings.QuickSpacing) or nameof(AppSettings.QuickFill) or nameof(AppSettings.QuickShowName))) Sync(); };
        Loaded += (_, _) => { _s.PropertyChanged -= h; _s.PropertyChanged += h; Sync(); };
        Unloaded += (_, _) => _s.PropertyChanged -= h;
    }

    private void Apply(int[] v, bool fill) => _s.SetQuickSpacing(v[0], v[1], v[2], v[3], v[4], fill);

    // Sliders and numbers show what is in effect; a default value is drawn muted, a chosen one in the accent.
    private void Sync()
    {
        _syncing = true;
        var m = BoardMetrics.From(_s);
        foreach (var (row, slider, value, label) in _rows)
        {
            double shown = row.Shown(m, _s);
            slider.Value = Math.Clamp(shown, row.Min, row.Max);
            value.Text = Math.Round(shown).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (row.Raw(_s) < 0) value.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            else value.SetResourceReference(TextBlock.ForegroundProperty, "Accent1Brush");
        }
        int[] now = { _s.QuickGapX, _s.QuickGapY, _s.QuickPadX, _s.QuickPadY, _s.QuickGroupGap };
        for (int i = 0; i < Presets.Length; i++) _presets[i].IsChecked = Presets[i].V.SequenceEqual(now) && Presets[i].Fill == _s.QuickFill;
        _reset.IsEnabled = _presets[1].IsChecked != true;
        _fill.IsChecked = _s.QuickFill;
        _syncing = false;
    }

    // The panel in a light-dismiss popup under an anchor button.
    public static Popup Flyout(UIElement anchor, AppSettings s)
    {
        var host = new Border { Margin = new Thickness(12, 4, 12, 16), Child = new SpacingPanel(s) };
        host.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.3, Direction = 270, Color = Colors.Black };
        var popup = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade, Child = host, HorizontalOffset = -300,
        };
        return popup;
    }
}
