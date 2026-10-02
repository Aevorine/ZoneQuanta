using System;
using System.Windows;
using System.Windows.Controls;
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
        Arc.SetResourceReference(DayArc.AccentProperty, accent);
    }

    public void Redraw() => Arc.InvalidateVisual();

    public void Apply(ZoneSnapshot s, AppSettings o)
    {
        bool animate = o.Animate && _allowAnimation;
        var t = s.Time;

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
        if (o.ShowSeconds) Secs.SetText($"{t.Second:00}", animate);

        Arc.Visibility = o.ShowDayNight ? Visibility.Visible : Visibility.Collapsed;
        if (o.ShowDayNight)
        {
            Arc.IsDay = s.IsDay;
            Arc.Progress = Math.Round(s.DayProgress, 3);
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
