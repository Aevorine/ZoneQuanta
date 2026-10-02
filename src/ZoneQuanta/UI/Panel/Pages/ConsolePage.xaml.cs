using System.Windows.Controls;
using ZoneQuanta.UI.Controls;

namespace ZoneQuanta.UI.Panel.Pages;

public partial class ConsolePage : UserControl
{
    public ConsolePage(IPanelHost host)
    {
        InitializeComponent();
        DataContext = host.Settings;
        var strip = new ClockStrip(host.Settings, host.Engine, allowAnimation: false);
        Preview.Content = strip;
        host.Tick += now => { if (IsVisible) strip.Update(now); };
    }
}
