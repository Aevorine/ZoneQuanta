using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ZoneQuanta.Core.Settings;
using ZoneQuanta.Core.Time;

namespace ZoneQuanta.UI.Controls;

public partial class ZoneCard : UserControl
{
    private static readonly string[] Weeks = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };

    private readonly bool _allowAnimation;

    public ZoneCard(int index, bool allowAnimation)
    {
        _allowAnimation = allowAnimation;
        InitializeComponent();
        string accent = index % 2 == 0 ? "Accent1Brush" : "Accent2Brush";
        LabelText.SetResourceReference(TextBlock.ForegroundProperty, accent);
        Arc.SetResourceReference(DayBar.AccentProperty, accent);
        WireInteraction();
    }

    public Func<DstInfo>? InfoProvider { get; set; }

    private bool _flipped;
    private ZoneSnapshot _snap;
    private bool _hasSnap;

    private void WireInteraction()
    {
        Root.MouseEnter += (_, _) => { AnimateHover(1.035); FadeGlow(0.22); };
        Root.MouseLeave += (_, _) => { AnimateHover(1.0); Untilt(); FadeGlow(0); };
        Root.MouseMove += (_, e) => Tilt(e.GetPosition(Root));
        Root.MouseLeftButtonUp += (_, _) => Flip();
    }

    private readonly RadialGradientBrush _glowBrush = new(Color.FromArgb(255, 255, 255, 255), Colors.Transparent) { RadiusX = 0.7, RadiusY = 0.9 };

    private void FadeGlow(double to)
    {
        if (!_allowAnimation) return;
        if (Glow.Background is null) Glow.Background = _glowBrush;
        Glow.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(160)));
    }

    private void Tilt(Point p)
    {
        if (!_allowAnimation || Root.ActualWidth < 1 || Root.ActualHeight < 1) return;
        double nx = p.X / Root.ActualWidth, ny = p.Y / Root.ActualHeight;
        var span = TimeSpan.FromMilliseconds(80);
        TiltSkew.BeginAnimation(SkewTransform.AngleXProperty, new DoubleAnimation((ny - 0.5) * 5, span));
        TiltSkew.BeginAnimation(SkewTransform.AngleYProperty, new DoubleAnimation((nx - 0.5) * -4, span));
        _glowBrush.GradientOrigin = _glowBrush.Center = new Point(nx, ny);
    }

    private void Untilt()
    {
        var span = TimeSpan.FromMilliseconds(220);
        TiltSkew.BeginAnimation(SkewTransform.AngleXProperty, new DoubleAnimation(0, span) { EasingFunction = new QuadraticEase() });
        TiltSkew.BeginAnimation(SkewTransform.AngleYProperty, new DoubleAnimation(0, span) { EasingFunction = new QuadraticEase() });
    }

    private void AnimateHover(double to)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(140)) { EasingFunction = new QuadraticEase() };
        HoverScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        HoverScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    public void Flip()
    {
        if (InfoProvider is null) return;
        if (!_flipped) FillBack(InfoProvider());

        void Swap()
        {
            _flipped = !_flipped;
            Front.Visibility = _flipped ? Visibility.Collapsed : Visibility.Visible;
            Back.Visibility = _flipped ? Visibility.Visible : Visibility.Collapsed;
        }

        var close = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(110)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        close.Completed += (_, _) =>
        {
            Swap();
            FlipScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        };
        FlipScale.BeginAnimation(ScaleTransform.ScaleXProperty, close);
    }

    private void FillBack(DstInfo i)
    {
        BackName.Text = i.Name;
        BackSun.Text = _hasSnap ? FormatSun(_snap.Sun) : string.Empty;
        string utc = FormatUtc(i.OffsetMinutes);
        if (!i.Supports)
        {
            BackState.Text = $"{utc} · 全年固定，无夏令时";
            BackNext.Text = string.Empty;
            BackYear.Text = string.Empty;
            return;
        }
        BackState.Text = $"{utc} · {(i.IsDst ? "夏令时生效中" : "标准时间")}";
        BackNext.Text = i.NextChange is { } c
            ? $"下次切换：{c:M月d日 HH:mm} → {(i.NextIsDst == true ? "夏令时" : "标准时间")}（{FormatUtc(i.OffsetAfter ?? 0)}）"
            : string.Empty;
        BackYear.Text = i.YearSpan ?? string.Empty;
    }

    private static string FormatSun(SunTimes sun)
    {
        if (sun.PolarDay) return "极昼 · 全天日照";
        if (sun.PolarNight) return "极夜 · 全天无日照";
        static string Clock(double m) { int v = (int)Math.Round(m) % 1440; return $"{v / 60:00}:{v % 60:00}"; }
        int len = (int)Math.Round(sun.SetMinutes - sun.RiseMinutes);
        return $"日出 {Clock(sun.RiseMinutes)} · 日落 {Clock(sun.SetMinutes)} · 昼长 {len / 60} 小时 {len % 60} 分";
    }

    private static string FormatUtc(int minutes)
    {
        int m = Math.Abs(minutes);
        string sign = minutes < 0 ? "-" : "+";
        return m % 60 == 0 ? $"UTC{sign}{m / 60}" : $"UTC{sign}{m / 60}:{m % 60:00}";
    }

    public void Redraw() => Arc.InvalidateVisual();

    public void Apply(ZoneSnapshot s, AppSettings o)
    {
        bool animate = o.Animate && _allowAnimation;
        var t = s.Time;
        _snap = s;
        _hasSnap = true;

        LabelText.Text = s.Label;
        TagText.Text = FormatTag(s);

        int hour = t.Hour;
        if (!o.Use24Hour)
        {
            AmPmText.Text = hour < 12 ? "上午" : "下午";
            hour %= 12;
            if (hour == 0) hour = 12;
        }
        else
        {
            AmPmText.Text = string.Empty;
        }
        AmPmText.Visibility = o.Use24Hour ? Visibility.Collapsed : Visibility.Visible;

        Main.SetText($"{hour:00}:{t.Minute:00}", animate);

        Secs.Visibility = o.ShowSeconds ? Visibility.Visible : Visibility.Collapsed;
        if (o.ShowSeconds) Secs.SetText($"{t.Second:00}", false);

        Arc.Visibility = o.ShowDayNight ? Visibility.Visible : Visibility.Collapsed;
        if (o.ShowDayNight)
        {
            Arc.Animated = animate;
            Arc.IsDay = s.IsDay;
            Arc.Now = t.Hour + t.Minute / 60.0 + t.Second / 3600.0;
            Arc.Rise = s.Sun.RiseMinutes / 60.0;
            Arc.Set = s.Sun.SetMinutes / 60.0;
            Arc.PolarDay = s.Sun.PolarDay;
            Arc.PolarNight = s.Sun.PolarNight;
        }

        DateText.Visibility = o.ShowDate ? Visibility.Visible : Visibility.Collapsed;
        if (o.ShowDate) DateText.Text = FormatDate(s);

        OffsetText.Visibility = o.ShowOffset ? Visibility.Visible : Visibility.Collapsed;
        if (o.ShowOffset) OffsetText.Text = FormatOffset(s);
    }

    private static string FormatTag(ZoneSnapshot s)
    {
        int m = Math.Abs(s.OffsetMinutes);
        string sign = s.OffsetMinutes < 0 ? "-" : "+";
        string text = m % 60 == 0 ? $"UTC{sign}{m / 60}" : $"UTC{sign}{m / 60}:{m % 60:00}";
        return s.IsDst ? text + " 夏令时" : text;
    }

    private static string FormatDate(ZoneSnapshot s)
    {
        string date = $"{s.Time.Month}月{s.Time.Day}日 {Weeks[(int)s.Time.DayOfWeek]}";
        return s.DayDelta switch
        {
            < 0 => "昨天 · " + date,
            > 0 => "明天 · " + date,
            _ => date,
        };
    }

    private static string FormatOffset(ZoneSnapshot s)
    {
        if (s.IsLocal) return "本机时区";
        if (s.RelativeToLocalMinutes == 0) return "与本地相同";
        int m = Math.Abs(s.RelativeToLocalMinutes);
        string span = m % 60 == 0 ? $"{m / 60} 小时" : $"{m / 60} 小时 {m % 60} 分";
        return (s.RelativeToLocalMinutes < 0 ? "比本地慢 " : "比本地快 ") + span;
    }
}
