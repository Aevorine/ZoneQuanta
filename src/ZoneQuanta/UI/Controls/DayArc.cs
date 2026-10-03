using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ZoneQuanta.UI.Controls;

public sealed class DayArc : FrameworkElement
{
    private static DependencyProperty Reg<T>(string name, T def, bool render = true, PropertyChangedCallback? changed = null) =>
        DependencyProperty.Register(name, typeof(T), typeof(DayArc),
            new FrameworkPropertyMetadata(def, render ? FrameworkPropertyMetadataOptions.AffectsRender : FrameworkPropertyMetadataOptions.None, changed));

    public static readonly DependencyProperty NowProperty = Reg("Now", 0.0);
    public static readonly DependencyProperty RiseProperty = Reg("Rise", 6.0);
    public static readonly DependencyProperty SetProperty = Reg("Set", 18.0);
    public static readonly DependencyProperty PolarDayProperty = Reg("PolarDay", false);
    public static readonly DependencyProperty PolarNightProperty = Reg("PolarNight", false);
    public static readonly DependencyProperty IsDayProperty = Reg("IsDay", true);
    public static readonly DependencyProperty AccentProperty = Reg<Brush>("Accent", Brushes.Gray);
    public static readonly DependencyProperty PhaseProperty = Reg("Phase", 0.0);
    public static readonly DependencyProperty RevealProperty = Reg("Reveal", 1.0);
    public static readonly DependencyProperty AnimatedProperty = Reg("Animated", false, false, (d, _) => ((DayArc)d).SyncAnimation());

    public double Now { get => (double)GetValue(NowProperty); set => SetValue(NowProperty, value); }
    public double Rise { get => (double)GetValue(RiseProperty); set => SetValue(RiseProperty, value); }
    public double Set { get => (double)GetValue(SetProperty); set => SetValue(SetProperty, value); }
    public bool PolarDay { get => (bool)GetValue(PolarDayProperty); set => SetValue(PolarDayProperty, value); }
    public bool PolarNight { get => (bool)GetValue(PolarNightProperty); set => SetValue(PolarNightProperty, value); }
    public bool IsDay { get => (bool)GetValue(IsDayProperty); set => SetValue(IsDayProperty, value); }
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public double Phase { get => (double)GetValue(PhaseProperty); set => SetValue(PhaseProperty, value); }
    public double Reveal { get => (double)GetValue(RevealProperty); set => SetValue(RevealProperty, value); }
    public bool Animated { get => (bool)GetValue(AnimatedProperty); set => SetValue(AnimatedProperty, value); }

    private const double Size0 = 40, C = 20, R = 15.5, Thick = 3.2;

    private static readonly (double X, double Y, double Offset)[] Stars =
        { (-4.5, -3.5, 0.0), (3.5, -5.0, 0.35), (5.5, 2.0, 0.7), (-2.5, 4.5, 0.15) };

    public DayArc()
    {
        IsVisibleChanged += (_, _) => SyncAnimation();
        Loaded += (_, _) => SyncAnimation();
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size0, Size0);

    private void SyncAnimation()
    {
        if (Animated && IsVisible && IsLoaded)
        {
            var phase = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(6)) { RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(phase, 12);
            BeginAnimation(PhaseProperty, phase);

            var reveal = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(800)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Timeline.SetDesiredFrameRate(reveal, 30);
            BeginAnimation(RevealProperty, reveal);
        }
        else
        {
            BeginAnimation(PhaseProperty, null);
            BeginAnimation(RevealProperty, null);
            SetCurrentValue(RevealProperty, 1.0);
        }
    }

    private static Point At(double hour, double radius)
    {
        double phi = (hour - 12) / 24.0 * 2 * Math.PI;
        return new Point(C + radius * Math.Sin(phi), C - radius * Math.Cos(phi));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var line = (Brush)(TryFindResource("LineBrush") ?? Brushes.DimGray);
        var muted = (Brush)(TryFindResource("MutedBrush") ?? Brushes.Gray);
        var sun = (SolidColorBrush)(TryFindResource("SunBrush") ?? Brushes.Gold);
        var moon = (SolidColorBrush)(TryFindResource("MoonBrush") ?? Brushes.LightSteelBlue);
        var card = (Brush)(TryFindResource("CardBrush") ?? Brushes.Black);

        double reveal = Math.Clamp(Reveal, 0, 1);
        var center = new Point(C, C);

        var face = new RadialGradientBrush(IsDay ? sun.Color : moon.Color, Colors.Transparent) { Opacity = IsDay ? 0.16 : 0.12 };
        dc.DrawEllipse(face, null, center, R + 3.5, R + 3.5);

        dc.DrawEllipse(null, new Pen(line, Thick), center, R, R);

        var dayPen = new Pen(sun, Thick) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (PolarDay)
        {
            dc.PushOpacity(reveal);
            dc.DrawEllipse(null, new Pen(sun, Thick), center, R, R);
            dc.Pop();
        }
        else if (!PolarNight)
        {
            double span = Math.Clamp(Set - Rise, 0, 24) * reveal;
            if (span > 0.05) dc.DrawGeometry(null, dayPen, Arc(Rise, Rise + span));
        }

        var tick = new Pen(muted, 0.9) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        for (int i = 0; i < 8; i++)
        {
            bool major = i % 2 == 0;
            dc.DrawLine(tick, At(i * 3, R - 3.6), At(i * 3, R - (major ? 6.6 : 5.2)));
        }

        double wave = Math.Sin(Phase * 2 * Math.PI);
        if (!IsDay) DrawStars(dc, moon);

        double now = ((Now % 24) + 24) % 24;
        var hand = new Pen(Accent, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawLine(hand, center, At(now, R - 7.5));
        dc.DrawEllipse(Accent, null, center, 1.7, 1.7);

        var marker = At(now, R);
        if (IsDay) DrawSun(dc, marker, sun, wave, reveal);
        else DrawMoon(dc, marker, moon, card, reveal);
    }

    private void DrawStars(DrawingContext dc, Brush moon)
    {
        foreach (var (x, y, off) in Stars)
        {
            double tw = 0.5 + 0.5 * Math.Sin((Phase + off) * 2 * Math.PI);
            dc.PushOpacity(0.25 + 0.6 * tw);
            dc.DrawEllipse(moon, null, new Point(C + x, C + y), 0.85, 0.85);
            dc.Pop();
        }
    }

    private void DrawSun(DrawingContext dc, Point c, Brush sun, double wave, double reveal)
    {
        var glow = new RadialGradientBrush(((SolidColorBrush)sun).Color, Colors.Transparent) { Opacity = 0.6 + 0.15 * wave };
        dc.DrawEllipse(glow, null, c, 8 * reveal, 8 * reveal);

        var ray = new Pen(sun, 1.0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        double spin = Phase * Math.PI / 4;
        for (int i = 0; i < 8; i++)
        {
            double a = spin + i * Math.PI / 4;
            double from = 4.2 * reveal, to = (5.6 + 0.9 * wave) * reveal;
            dc.DrawLine(ray, new Point(c.X + from * Math.Cos(a), c.Y + from * Math.Sin(a)),
                             new Point(c.X + to * Math.Cos(a), c.Y + to * Math.Sin(a)));
        }
        dc.DrawEllipse(sun, null, c, 3.2 * reveal, 3.2 * reveal);
    }

    private static void DrawMoon(DrawingContext dc, Point c, Brush moon, Brush card, double reveal)
    {
        var glow = new RadialGradientBrush(((SolidColorBrush)moon).Color, Colors.Transparent) { Opacity = 0.4 };
        dc.DrawEllipse(glow, null, c, 7.5 * reveal, 7.5 * reveal);
        dc.DrawEllipse(moon, null, c, 3.8 * reveal, 3.8 * reveal);
        dc.DrawEllipse(card, null, new Point(c.X + 1.8, c.Y - 1.2), 3.1 * reveal, 3.1 * reveal);
    }

    private static Geometry Arc(double fromHour, double toHour)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(At(fromHour, R), false, false);
            ctx.ArcTo(At(toHour, R), new Size(R, R), 0, toHour - fromHour > 12, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }
}
