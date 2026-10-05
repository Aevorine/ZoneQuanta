using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ZoneQuanta.Core.Monitor;

namespace ZoneQuanta.UI.Panel.Pages;

public partial class MonitorPage : UserControl
{
    private readonly IPanelHost _host;
    private readonly Dictionary<string, TextBlock> _values = new();

    public MonitorPage(IPanelHost host)
    {
        _host = host;
        InitializeComponent();
        var s = host.Settings;
        DataContext = s;

        foreach (var (key, title, accent) in new[]
                 { ("up", "上传", "Accent2Brush"), ("down", "下载", "Accent1Brush"), ("today", "今日流量", "TextBrush"), ("total", "总速", "TextBrush"), ("mem", "内存", "TextBrush"), ("cpu", "CPU", "TextBrush") })
        {
            var label = new TextBlock { Text = title, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            var value = new TextBlock { FontSize = 20, FontWeight = FontWeights.Light, HorizontalAlignment = HorizontalAlignment.Center, Text = "--" };
            value.SetResourceReference(TextBlock.ForegroundProperty, accent);
            var stack = new StackPanel { Children = { label, value } };
            Preview.Children.Add(stack);
            _values[key] = value;
        }

        foreach (var (unit, text) in new[] { ("Auto", "自动"), ("K", "K"), ("M", "M"), ("G", "G") })
        {
            var r = new RadioButton
            {
                Content = text,
                Style = (Style)FindResource("Segment"),
                GroupName = "unit",
                Margin = new Thickness(2, 0, 2, 0),
                IsChecked = s.SpeedUnit == unit,
            };
            r.Checked += (_, _) => s.SpeedUnit = unit;
            Scales.Children.Add(r);
        }

        foreach (var (pos, text) in new[] { ("Left", "最左侧"), ("Start", "开始按钮左侧"), ("Right", "通知区左侧") })
        {
            var r = new RadioButton
            {
                Content = text,
                Style = (Style)FindResource("Segment"),
                GroupName = "pos",
                Margin = new Thickness(2, 0, 2, 0),
                IsChecked = s.BandPosition == pos,
            };
            r.Checked += (_, _) => s.BandPosition = pos;
            Positions.Children.Add(r);
        }

        ByteMode.IsChecked = !s.SpeedBits;
        BitMode.IsChecked = s.SpeedBits;
        ByteMode.Checked += (_, _) => s.SpeedBits = false;
        BitMode.Checked += (_, _) => s.SpeedBits = true;

        host.Tick += _ => { if (IsVisible) Refresh(); };
    }

    private void Refresh()
    {
        var m = _host.Latest;
        var s = _host.Settings;
        _values["up"].Text = UnitFormat.Speed(m.UpBps, s.SpeedBits, s.SpeedUnit);
        _values["down"].Text = UnitFormat.Speed(m.DownBps, s.SpeedBits, s.SpeedUnit);
        _values["today"].Text = UnitFormat.Size(_host.Totals.TodayTotal(), false, "Auto");
        _values["total"].Text = UnitFormat.Speed(m.UpBps + m.DownBps, s.SpeedBits, s.SpeedUnit);
        _values["mem"].Text = $"{m.Mem:0}%";
        _values["cpu"].Text = $"{m.Cpu:0}%";
    }
}
