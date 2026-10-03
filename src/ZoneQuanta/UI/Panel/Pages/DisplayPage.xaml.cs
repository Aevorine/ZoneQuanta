using System.Windows;
using System.Windows.Controls;
using ZoneQuanta.UI.Controls;
using ZoneQuanta.UI.Theme;

namespace ZoneQuanta.UI.Panel.Pages;

public partial class DisplayPage : UserControl
{
    public DisplayPage(IPanelHost host)
    {
        InitializeComponent();
        var settings = host.Settings;
        DataContext = settings;

        var strip = new ClockStrip(settings, host.Engine, allowAnimation: false, compact: true);
        Preview.Content = strip;
        host.Tick += now => { if (IsVisible) strip.Update(now); };

        foreach (var palette in ThemeManager.Palettes)
        {
            var name = palette.Name;
            var radio = new RadioButton
            {
                Content = palette.Display,
                Style = (Style)FindResource("Segment"),
                GroupName = "theme",
                Margin = new Thickness(2, 0, 2, 0),
                IsChecked = settings.Theme == name,
            };
            radio.Checked += (_, _) => settings.Theme = name;
            Themes.Children.Add(radio);
        }

        Horizontal.IsChecked = !settings.Vertical;
        Vertical.IsChecked = settings.Vertical;
        Horizontal.Checked += (_, _) => settings.Vertical = false;
        Vertical.Checked += (_, _) => settings.Vertical = true;
    }
}
