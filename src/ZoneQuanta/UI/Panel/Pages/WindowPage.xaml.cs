using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ZoneQuanta.Core.Settings;

namespace ZoneQuanta.UI.Panel.Pages;

public partial class WindowPage : UserControl
{
    private static readonly (string Where, string Name)[] Spots =
    {
        ("TL", "左上"), ("T", "上"), ("TR", "右上"),
        ("L", "左"), ("C", "居中"), ("R", "右"),
        ("BL", "左下"), ("B", "下"), ("BR", "右下"),
    };

    private readonly IPanelHost _host;

    public WindowPage(IPanelHost host)
    {
        _host = host;
        InitializeComponent();
        DataContext = host.Settings;

        foreach (var (where, name) in Spots)
        {
            var b = new Button { Content = name, Style = (Style)FindResource("Action"), Margin = new Thickness(3) };
            b.Click += (_, _) => host.AnchorWidget(where);
            Anchors.Children.Add(b);
        }

        ResetButton.Click += (_, _) => host.ResetWidget();
        NudgeLeft.Click += (_, _) => Nudge(-10, 0);
        NudgeRight.Click += (_, _) => Nudge(10, 0);
        NudgeUp.Click += (_, _) => Nudge(0, -10);
        NudgeDown.Click += (_, _) => Nudge(0, 10);

        XBox.LostKeyboardFocus += (_, _) => Commit();
        YBox.LostKeyboardFocus += (_, _) => Commit();
        XBox.KeyDown += OnEnter;
        YBox.KeyDown += OnEnter;

        host.Settings.PropertyChanged += OnSettingChanged;
        Unloaded += (_, _) => host.Settings.PropertyChanged -= OnSettingChanged;
        Loaded += (_, _) =>
        {
            host.Settings.PropertyChanged -= OnSettingChanged;
            host.Settings.PropertyChanged += OnSettingChanged;
            Refresh();
        };
        Refresh();
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.Left) or nameof(AppSettings.Top)) Refresh();
    }

    private void Refresh()
    {
        var (x, y) = _host.WidgetPosition;
        if (!XBox.IsKeyboardFocused) XBox.Text = ((int)x).ToString(CultureInfo.InvariantCulture);
        if (!YBox.IsKeyboardFocused) YBox.Text = ((int)y).ToString(CultureInfo.InvariantCulture);
    }

    private void Nudge(double dx, double dy)
    {
        var (x, y) = _host.WidgetPosition;
        _host.MoveWidget(x + dx, y + dy);
    }

    private void Commit()
    {
        var (x, y) = _host.WidgetPosition;
        if (double.TryParse(XBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double nx)) x = nx;
        if (double.TryParse(YBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double ny)) y = ny;
        _host.MoveWidget(x, y);
        Refresh();
    }

    private void OnEnter(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Commit();
    }
}
