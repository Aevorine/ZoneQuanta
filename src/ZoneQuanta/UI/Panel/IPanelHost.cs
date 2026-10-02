using System;
using ZoneQuanta.Core.Settings;
using ZoneQuanta.Core.Time;
using ZoneQuanta.Core.Update;

namespace ZoneQuanta.UI.Panel;

public interface IPanelHost
{
    AppSettings Settings { get; }
    TimeEngine Engine { get; }
    UpdateCoordinator Updates { get; }
    (double X, double Y) WidgetPosition { get; }
    void MoveWidget(double x, double y);
    void AnchorWidget(string where);
    void ResetWidget();
    void OpenSettingsFolder();
    void ExitApp();
    event Action<DateTimeOffset> Tick;
}
