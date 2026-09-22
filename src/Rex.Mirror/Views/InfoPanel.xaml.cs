using System.IO;
using System.Windows;
using System.Windows.Controls;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror.Views;

public partial class InfoPanel : UserControl
{
    private MainWindow? _window;
    private AppHost? _host;

    public InfoPanel()
    {
        InitializeComponent();
        ShortcutRows.ItemsSource = new[]
        {
            KeyValuePair.Create("Alt + wheel", "Zoom the PC view at the cursor"),
            KeyValuePair.Create("Alt + pinch", "Zoom the PC view on a touchpad"),
            KeyValuePair.Create("Alt + drag", "Pan while zoomed"),
            KeyValuePair.Create("Two fingers", "Pinch, rotate and pan on the phone"),
            KeyValuePair.Create("F11", "Fullscreen"),
            KeyValuePair.Create("Ctrl+Alt+P", "Show or hide the pattern guide"),
            KeyValuePair.Create("Ctrl+Alt+C", "Calibrate the pattern guide"),
        };
    }

    public void Attach(MainWindow window, AppHost host)
    {
        _window = window;
        _host = host;
        Refresh();
    }

    public void Refresh()
    {
        if (_host is null)
        {
            return;
        }

        var session = _host.Session;
        var rows = new List<KeyValuePair<string, string>>();
        if (session.Identity is { } id && session.ActiveDevice is { } device)
        {
            rows.Add(KeyValuePair.Create("Name", id.DisplayName));
            rows.Add(KeyValuePair.Create("Model", string.IsNullOrWhiteSpace(id.Model) ? "unknown" : id.Model));
            rows.Add(KeyValuePair.Create("Android", string.IsNullOrWhiteSpace(id.AndroidVersion) ? "unknown" : $"{id.AndroidVersion} (API {id.ApiLevel})"));
            rows.Add(KeyValuePair.Create("Serial", device.Serial));
            rows.Add(KeyValuePair.Create("Connection", device.Transport));
            if (id.DisplayWidth > 0)
            {
                rows.Add(KeyValuePair.Create("Display", $"{id.DisplayWidth} × {id.DisplayHeight}"));
            }

            if (session.Battery is { } battery)
            {
                rows.Add(KeyValuePair.Create("Battery", $"{battery.Level}%{(battery.Charging ? ", charging" : string.Empty)}"));
            }

            var profile = _host.State.GetDevice(device.Serial);
            rows.Add(KeyValuePair.Create("Lock type", string.IsNullOrEmpty(profile?.LockScreenMode) ? "not set" : profile!.LockScreenMode));
        }
        else if (session.Devices.Count > 0)
        {
            foreach (var d in session.Devices)
            {
                rows.Add(KeyValuePair.Create(d.Serial, $"{d.State} ({d.Transport})"));
            }
        }
        else
        {
            rows.Add(KeyValuePair.Create("Status", session.Message));
        }

        DeviceRows.ItemsSource = rows;

        ToolRows.ItemsSource = new[]
        {
            KeyValuePair.Create("scrcpy", session.Tools is null ? "not installed" : $"{session.Tools.Version}"),
            KeyValuePair.Create("Folder", _host.Paths.Root),
            KeyValuePair.Create("Version", CommandRouter.AppVersion),
            KeyValuePair.Create("Startup", StartupRegistration.IsEnabled() ? "starts with Windows" : "manual"),
        };

        LogText.Text = string.Join(Environment.NewLine, _host.Log.Tail(12));
    }

    private void OnOpenCaptures(object sender, RoutedEventArgs e)
    {
        if (_host is not null)
        {
            _window?.OpenFolder(_host.Paths.Inside(_host.Config.App.ScreenshotDirectory));
        }
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        if (_host is not null)
        {
            _window?.OpenFolder(_host.Paths.Logs);
        }
    }

    private async void OnCopyDiagnostics(object sender, RoutedEventArgs e)
    {
        if (_host is null)
        {
            return;
        }

        try
        {
            var report = await Diagnostics.BuildAsync(_host.Paths, _host.Log, _host.Runner);
            Clipboard.SetText(report.ToText());
            _window?.SetStatus("Diagnostics copied to the clipboard.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            _window?.SetStatus("Could not build diagnostics: " + ex.Message, true);
        }
    }
}
