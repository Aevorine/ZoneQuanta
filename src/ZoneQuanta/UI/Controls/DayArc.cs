using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ZoneQuanta.UI.Controls;

public sealed class DayArc : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(DayArc), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsDayProperty = DependencyProperty.Register(
        nameof(IsDay), typeof(bool), typeof(DayArc), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(DayArc), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
        nameof(Phase), typeof(double), typeof(DayArc), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AnimatedProperty = DependencyProperty.Register(
        nameof(Animated), typeof(bool), typeof(DayArc), new PropertyMetadata(false, (d, _) => ((DayArc)d).SyncAnimation()));

    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public bool IsDay { get => (bool)GetValue(IsDayProperty); set => SetValue(IsDayProperty, value); }
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public double Phase { get => (double)GetValue(PhaseProperty); set => SetValue(PhaseProperty, value); }
    public bool Animated { get => (bool)GetValue(AnimatedProperty); set => SetValue(AnimatedProperty, value); }

    private static readonly (double X, double Y, double Offset)[] Stars = { (0.22, 0.30, 0.0), (0.47, 0.12, 0.35), (0.74, 0.34, 0.7), (0.60, 0.50, 0.15) };

    public DayArc()
    {
        IsVisibleChanged += (_, _) => SyncAnimation();
        Loaded += (_, _) => SyncAnimation();
    }

    protected override Size MeasureOverride(Size availableSize) => new(64, 34);

    private void SyncAnimation()
    {
        if (Animated && IsVisible && IsLoaded)
        {
            var a = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(6)) { RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(a, 12);
            BeginAnimation(PhaseProperty, a);
        }
        else
        {
            BeginAnimation(PhaseProperty, null);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double r = 27, cx = 32, cy = 30;
        var line = (Brush)(TryFindResource("LineBrush") ?? Brushes.DimGray);
        var sun = (Brush)(TryFindResource("SunBrush") ?? Brushes.Gold);
        var moon = (Brush)(TryFindResource("MoonBrush") ?? Brushes.LightSteelBlue);
        var card = (Brush)(TryFindResource("CardBrush") ?? Brushes.Black);

        var track = new Pen(line, 1.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, DashStyle = new DashStyle(new double[] { 1.2, 2.4 }, 0) };
        var done = new Pen(Accent, 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var ground = new Pen(line, 1.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

        dc.DrawLine(ground, new Point(cx - r - 4, cy + 1.5), new Point(cx + r + 4, cy + 1.5));
        dc.DrawGeometry(null, track, Arc(cx, cy, r, 1.0));

        double p = Math.Clamp(Progress, 0, 1);
        if (p > 0.01) dc.DrawGeometry(null, done, Arc(cx, cy, r, p));

        foreach (double t in new[] { 0.25, 0.5, 0.75 })
        {
            double ta = Math.PI * (1 - t);
            var inner = new Point(cx + (r - 3) * Math.Cos(ta), cy - (r - 3) * Math.Sin(ta));
            var outer = new Point(cx + (r + 1) * Math.Cos(ta), cy - (r + 1) * Math.Sin(ta));
            dc.DrawLine(ground, inner, outer);
        }

        double a = Math.PI * (1 - p);
        var dot = new Point(cx + r * Math.Cos(a), cy - r * Math.Sin(a));
        double wave = Math.Sin(Phase * 2 * Math.PI);

        if (IsDay) DrawSun(dc, dot, sun, wave);
        else DrawMoon(dc, dot, moon, card, cx, cy);
    }

    private void DrawSun(DrawingContext dc, Point c, Brush sun, double wave)
    {
        var glow = new RadialGradientBrush(((SolidColorBrush)sun).Color, Colors.Transparent) { Opacity = 0.55 + 0.15 * wave };
        dc.DrawEllipse(glow, null, c, 9.5, 9.5);

        var ray = new Pen(sun, 1.1) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        double spin = Phase * Math.PI / 4;
        for (int i = 0; i < 8; i++)
        {
            double ang = spin + i * Math.PI / 4;
            double from = 4.8, to = 6.4 + 1.1 * wave;
            dc.DrawLine(ray, new Point(c.X + from * Math.Cos(ang), c.Y + from * Math.Sin(ang)),
                             new Point(c.X + to * Math.Cos(ang), c.Y + to * Math.Sin(ang)));
        }
        dc.DrawEllipse(sun, null, c, 3.6, 3.6);
    }

    private void DrawMoon(DrawingContext dc, Point c, Brush moon, Brush card, double cx, double cy)
    {
        var glow = new RadialGradientBrush(((SolidColorBrush)moon).Color, Colors.Transparent) { Opacity = 0.35 };
        dc.DrawEllipse(glow, null, c, 8.5, 8.5);

        foreach (var (x, y, off) in Stars)
        {
            double tw = 0.5 + 0.5 * Math.Sin((Phase + off) * 2 * Math.PI);
            dc.PushOpacity(0.25 + 0.55 * tw);
            dc.DrawEllipse(moon, null, new Point(cx - 24 + x * 48, cy - 3 - y * 22), 0.9, 0.9);
            dc.Pop();
        }

        dc.DrawEllipse(moon, null, c, 4.2, 4.2);
        dc.DrawEllipse(card, null, new Point(c.X + 2.0, c.Y - 1.3), 3.4, 3.4);
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
