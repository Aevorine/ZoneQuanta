using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ZoneQuanta.UI.Controls;

public sealed class DayBar : FrameworkElement
{
    private static DependencyProperty Reg<T>(string name, T def, bool render = true, PropertyChangedCallback? changed = null) =>
        DependencyProperty.Register(name, typeof(T), typeof(DayBar),
            new FrameworkPropertyMetadata(def, render ? FrameworkPropertyMetadataOptions.AffectsRender : FrameworkPropertyMetadataOptions.None, changed));

    public static readonly DependencyProperty NowProperty = Reg("Now", 0.0);
    public static readonly DependencyProperty RiseProperty = Reg("Rise", 6.0);
    public static readonly DependencyProperty SetProperty = Reg("Set", 18.0);
    public static readonly DependencyProperty PolarDayProperty = Reg("PolarDay", false);
    public static readonly DependencyProperty PolarNightProperty = Reg("PolarNight", false);
    public static readonly DependencyProperty IsDayProperty = Reg("IsDay", true);
    public static readonly DependencyProperty AccentProperty = Reg<Brush>("Accent", Brushes.Gray);
    public static readonly DependencyProperty RevealProperty = Reg("Reveal", 1.0);
    public static readonly DependencyProperty AnimatedProperty = Reg("Animated", false, false, (d, _) => ((DayBar)d).SyncAnimation());

    public double Now { get => (double)GetValue(NowProperty); set => SetValue(NowProperty, value); }
    public double Rise { get => (double)GetValue(RiseProperty); set => SetValue(RiseProperty, value); }
    public double Set { get => (double)GetValue(SetProperty); set => SetValue(SetProperty, value); }
    public bool PolarDay { get => (bool)GetValue(PolarDayProperty); set => SetValue(PolarDayProperty, value); }
    public bool PolarNight { get => (bool)GetValue(PolarNightProperty); set => SetValue(PolarNightProperty, value); }
    public bool IsDay { get => (bool)GetValue(IsDayProperty); set => SetValue(IsDayProperty, value); }
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public double Reveal { get => (double)GetValue(RevealProperty); set => SetValue(RevealProperty, value); }
    public bool Animated { get => (bool)GetValue(AnimatedProperty); set => SetValue(AnimatedProperty, value); }

    private const double Height0 = 36, Pad = 9, BarY = 17, Thick = 8;
    private double Phase => Now * 3600 % 6 / 6.0;
    private double? _hoverHour;

    private static readonly double[] StarHours = { 0.9, 2.4, 3.9, 21.2, 22.4, 23.3 };

    public DayBar()
    {
        IsVisibleChanged += (_, _) => SyncAnimation();
        Loaded += (_, _) => SyncAnimation();
        MouseMove += (_, e) =>
        {
            double w = ActualWidth - 2 * Pad;
            if (w < 1) return;
            _hoverHour = Math.Clamp((e.GetPosition(this).X - Pad) / w * 24, 0, 24);
            InvalidateVisual();
        };
        MouseLeave += (_, _) =>
        {
            _hoverHour = null;
            InvalidateVisual();
        };
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 140 : Math.Max(120, availableSize.Width), Height0);

    private void SyncAnimation()
    {
        if (Animated && IsVisible && IsLoaded)
        {
            var reveal = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(900)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Timeline.SetDesiredFrameRate(reveal, 30);
            BeginAnimation(RevealProperty, reveal);
        }
        else
        {
            BeginAnimation(RevealProperty, null);
            SetCurrentValue(RevealProperty, 1.0);
        }
    }

    private double X(double hour, double width) => Pad + (width - 2 * Pad) * hour / 24.0;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        if (w < 40) return;

        var line = (Brush)(TryFindResource("LineBrush") ?? Brushes.DimGray);
        var muted = (Brush)(TryFindResource("MutedBrush") ?? Brushes.Gray);
        var text = (Brush)(TryFindResource("TextBrush") ?? Brushes.White);
        var sunBrush = (SolidColorBrush)(TryFindResource("SunBrush") ?? Brushes.Gold);
        var moon = (SolidColorBrush)(TryFindResource("MoonBrush") ?? Brushes.LightSteelBlue);
        var card = (Brush)(TryFindResource("CardBrush") ?? Brushes.Black);

        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, Height0));

        double reveal = Math.Clamp(Reveal, 0, 1);
        double now = ((Now % 24) + 24) % 24;
        double nowX = X(now, w);
        double top = BarY - Thick / 2, bottom = BarY + Thick / 2;

        // night track
        dc.DrawRoundedRectangle(line, null, new Rect(Pad - 4, top, w - 2 * Pad + 8, Thick), Thick / 2, Thick / 2);

        // daylight segment: dimmer for the part of the day still to come, full strength for the part already gone
        var edge = Lerp(sunBrush.Color, Color.FromRgb(0xD9, 0x7B, 0x3A), 0.55);
        double dayFrom = PolarDay ? 0 : Rise, dayTo = PolarDay ? 24 : Set;
        bool hasDay = !PolarNight && dayTo > dayFrom;
        if (hasDay)
        {
            double x1 = X(dayFrom, w), x2 = X(dayTo, w);
            var grad = new LinearGradientBrush(edge, edge, new Point(0, 0), new Point(1, 0));
            grad.GradientStops.Clear();
            grad.GradientStops.Add(new GradientStop(edge, 0));
            grad.GradientStops.Add(new GradientStop(sunBrush.Color, 0.5));
            grad.GradientStops.Add(new GradientStop(edge, 1));
            var rect = new Rect(x1, top, Math.Max(2, x2 - x1), Thick);
            double revealX = x1 + (rect.Width) * reveal;

            dc.PushClip(new RectangleGeometry(new Rect(0, 0, Math.Max(0, revealX), Height0)));
            dc.PushOpacity(0.38);
            dc.DrawRoundedRectangle(grad, null, rect, Thick / 2, Thick / 2);
            dc.Pop();
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, Math.Max(0, Math.Min(nowX, revealX)), Height0)));
            dc.DrawRoundedRectangle(grad, null, rect, Thick / 2, Thick / 2);
            dc.Pop();
            dc.Pop();
        }

        // hour ticks
        var minor = new Pen(muted, 0.6);
        var major = new Pen(muted, 1.0);
        for (int h = 0; h <= 24; h++)
        {
            bool big = h % 6 == 0;
            double x = X(h, w);
            dc.DrawLine(big ? major : minor, new Point(x, bottom + 1.5), new Point(x, bottom + (big ? 5.5 : 3.5)));
        }

        var font = TryFindResource("AppFont") as FontFamily ?? new FontFamily("Segoe UI");
        var face = new Typeface(font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        foreach (int h in new[] { 0, 6, 12, 18, 24 })
        {
            var t = new FormattedText(h.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 8.5, muted, ppd);
            double x = Math.Clamp(X(h, w) - t.Width / 2, 0, w - t.Width);
            dc.DrawText(t, new Point(x, bottom + 5.5));
        }

        // sunrise / sunset times
        if (hasDay && !PolarDay)
        {
            DrawTime(dc, face, ppd, dayFrom, w, sunBrush, top - 11.5);
            DrawTime(dc, face, ppd, dayTo, w, sunBrush, top - 11.5);
        }

        // stars on the night part of the track
        if (!IsDay || PolarNight)
        {
            foreach (double sh in StarHours)
            {
                if (hasDay && sh > dayFrom && sh < dayTo) continue;
                double tw = 0.5 + 0.5 * Math.Sin((Phase + sh * 0.37) * 2 * Math.PI);
                dc.PushOpacity(0.25 + 0.6 * tw);
                dc.DrawEllipse(moon, null, new Point(X(sh, w), BarY), 0.9, 0.9);
                dc.Pop();
            }
        }

        // hover scrubber
        if (_hoverHour is { } hh)
        {
            double hx = X(hh, w);
            dc.DrawLine(new Pen(text, 0.8) { DashStyle = DashStyles.Dot }, new Point(hx, top - 3), new Point(hx, bottom + 4));
            int minutes = (int)Math.Round(hh * 60);
            string label = $"{Math.Min(minutes / 60, 24):00}:{minutes % 60:00}";
            var t = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 9.5, text, ppd);
            dc.DrawRoundedRectangle(card, null, new Rect(Math.Clamp(hx - t.Width / 2 - 3, 0, w - t.Width - 6), 0, t.Width + 6, t.Height + 1), 3, 3);
            dc.DrawText(t, new Point(Math.Clamp(hx - t.Width / 2, 3, w - t.Width - 3), 0.5));
        }

        // current-time marker
        double wave = Math.Sin(Phase * 2 * Math.PI);
        var marker = new Point(nowX, BarY);
        if (IsDay) DrawSun(dc, marker, sunBrush, wave, reveal);
        else DrawMoon(dc, marker, moon, card, reveal);
    }

    private void DrawTime(DrawingContext dc, Typeface face, double ppd, double hour, double w, Brush brush, double y)
    {
        double wrapped = ((hour % 24) + 24) % 24;
        int minutes = (int)Math.Round(wrapped * 60) % 1440;
        var t = new FormattedText($"{minutes / 60:00}:{minutes % 60:00}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 8.5, brush, ppd);
        double x = Math.Clamp(X(Math.Min(hour, 24), w) - t.Width / 2, 0, w - t.Width);
        dc.DrawText(t, new Point(x, y));
    }

    private void DrawSun(DrawingContext dc, Point c, Brush sun, double wave, double reveal)
    {
        var glow = new RadialGradientBrush(((SolidColorBrush)sun).Color, Colors.Transparent) { Opacity = 0.6 + 0.15 * wave };
        dc.DrawEllipse(glow, null, c, 9 * reveal, 9 * reveal);

        var ray = new Pen(sun, 1.0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        double spin = Phase * Math.PI / 4;
        for (int i = 0; i < 8; i++)
        {
            double a = spin + i * Math.PI / 4;
            double from = 5.0 * reveal, to = (6.6 + 0.9 * wave) * reveal;
            dc.DrawLine(ray, new Point(c.X + from * Math.Cos(a), c.Y + from * Math.Sin(a)),
                             new Point(c.X + to * Math.Cos(a), c.Y + to * Math.Sin(a)));
        }
        dc.DrawEllipse(sun, null, c, 3.8 * reveal, 3.8 * reveal);
    }

    private static void DrawMoon(DrawingContext dc, Point c, Brush moon, Brush card, double reveal)
    {
        var glow = new RadialGradientBrush(((SolidColorBrush)moon).Color, Colors.Transparent) { Opacity = 0.45 };
        dc.DrawEllipse(glow, null, c, 8.5 * reveal, 8.5 * reveal);
        dc.DrawEllipse(moon, null, c, 4.4 * reveal, 4.4 * reveal);
        dc.DrawEllipse(card, null, new Point(c.X + 2.1, c.Y - 1.4), 3.6 * reveal, 3.6 * reveal);
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}
