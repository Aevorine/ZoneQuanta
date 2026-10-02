using System;
using System.Windows;
using System.Windows.Media;

namespace ZoneQuanta.UI.Controls;

public sealed class DayArc : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(DayArc), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsDayProperty = DependencyProperty.Register(
        nameof(IsDay), typeof(bool), typeof(DayArc), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(DayArc), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public bool IsDay { get => (bool)GetValue(IsDayProperty); set => SetValue(IsDayProperty, value); }
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(64, 34);

    protected override void OnRender(DrawingContext dc)
    {
        double r = 28, cx = 32, cy = 31;
        var line = (Brush)(TryFindResource("LineBrush") ?? Brushes.DimGray);
        var sun = (Brush)(TryFindResource("SunBrush") ?? Brushes.Gold);
        var moon = (Brush)(TryFindResource("MoonBrush") ?? Brushes.LightSteelBlue);

        var track = new Pen(line, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var done = new Pen(Accent, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

        dc.DrawLine(track, new Point(cx - r - 3, cy + 1), new Point(cx + r + 3, cy + 1));
        dc.DrawGeometry(null, track, Arc(cx, cy, r, 1.0));

        double p = Math.Clamp(Progress, 0, 1);
        if (p > 0.01) dc.DrawGeometry(null, done, Arc(cx, cy, r, p));

        double a = Math.PI * (1 - p);
        var dot = new Point(cx + r * Math.Cos(a), cy - r * Math.Sin(a));
        Brush body = IsDay ? sun : moon;

        dc.PushOpacity(0.22);
        dc.DrawEllipse(body, null, dot, 8, 8);
        dc.Pop();
        dc.DrawEllipse(body, null, dot, 3.6, 3.6);
        if (!IsDay)
        {
            var cut = (Brush)(TryFindResource("CardBrush") ?? Brushes.Black);
            dc.DrawEllipse(cut, null, new Point(dot.X + 1.8, dot.Y - 1.2), 3, 3);
        }
    }

    private static Geometry Arc(double cx, double cy, double r, double p)
    {
        double a = Math.PI * (1 - p);
        var end = new Point(cx + r * Math.Cos(a), cy - r * Math.Sin(a));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(cx - r, cy), false, false);
            ctx.ArcTo(end, new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }
}
