using System;
using System.Threading.Tasks;
using ZoneQuanta.Core.Monitor;
using ZoneQuanta.Core.Settings;
using ZoneQuanta.Core.Time;
using ZoneQuanta.Core.Update;

namespace ZoneQuanta.UI.Panel;

public interface IPanelHost
{
    AppSettings Settings { get; }
    TimeEngine Engine { get; }
    UpdateCoordinator Updates { get; }
    Metrics Latest { get; }
    TrafficFile Totals { get; }
    (double X, double Y) WidgetPosition { get; }
    void MoveWidget(double x, double y);
    void AnchorWidget(string where);
    void ResetWidget();
    void OpenSettingsFolder();
    void ExitApp();
    Task<bool> SetTrackingAsync(bool on);
    event Action<DateTimeOffset> Tick;
}
