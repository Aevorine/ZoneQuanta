using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ZoneQuanta.Core.Update;

namespace ZoneQuanta.UI.Panel.Pages;

public partial class UpdatePage : UserControl
{
    private readonly UpdateCoordinator _updates;

    public UpdatePage(IPanelHost host)
    {
        InitializeComponent();
        DataContext = host.Settings;
        _updates = host.Updates;

        VersionText.Text = $"v{UpdateService.CurrentVersion}";
        FlavorText.Text = UpdateService.Flavor == "full" ? "独立版（内置运行时）" : "轻量版（需 .NET 8 桌面运行时）";

        CheckButton.Click += async (_, _) => await _updates.CheckAsync();
        InstallButton.Click += async (_, _) => await _updates.InstallAsync();

        _updates.PropertyChanged += OnChanged;
        Unloaded += (_, _) => _updates.PropertyChanged -= OnChanged;
        Loaded += (_, _) =>
        {
            _updates.PropertyChanged -= OnChanged;
            _updates.PropertyChanged += OnChanged;
            Sync();
        };
        Sync();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.Invoke(Sync);

    private void Sync()
    {
        StatusText.Text = _updates.Status;
        Bar.Value = _updates.Progress;
        Bar.Visibility = _updates.Busy ? Visibility.Visible : Visibility.Hidden;
        CheckButton.IsEnabled = !_updates.Busy;
        InstallButton.IsEnabled = !_updates.Busy && _updates.Available is not null;
        InstallButton.Visibility = _updates.Available is not null ? Visibility.Visible : Visibility.Hidden;
    }
}
