using System.IO;
using System.Windows;
using System.Windows.Controls;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror.Views;

/// <summary>One line of the Info tab: a name, its value, and whether the value is set in the mono face.</summary>
public sealed record InfoRow(string Key, string Value, bool Mono = false);

public partial class InfoPanel : UserControl
{
    private MainWindow? _window;
    private AppHost? _host;

    public InfoPanel()
    {
        InitializeComponent();

        // One list, from the registry the window and the website also read, so it cannot drift.
        static KeyValuePair<string, string>[] Rows(IEnumerable<Shortcut> shortcuts) =>
            shortcuts.Select(s => KeyValuePair.Create(s.Gesture, s.Description)).ToArray();
        ShortcutKeys.ItemsSource = Rows(Shortcuts.All.Where(s => s.IsKey && !s.Browse));
        ShortcutBrowse.ItemsSource = Rows(Shortcuts.BrowseKeys);
        ShortcutGestures.ItemsSource = Rows(Shortcuts.All.Where(s => !s.IsKey));
        // The heading is set in capitals; the key is written the way it is everywhere else.
        BrowseHeading.Text = "BROWSE MODE · " + Shortcuts.Gesture("browse");
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
        var rows = new List<InfoRow>();
        if (session.Identity is { } id && session.ActiveDevice is { } device)
        {
            rows.Add(new InfoRow("Name", id.DisplayName));
            rows.Add(new InfoRow("Model", string.IsNullOrWhiteSpace(id.Model) ? "unknown" : id.Model));
            rows.Add(new InfoRow("Android", string.IsNullOrWhiteSpace(id.AndroidVersion) ? "unknown" : $"{id.AndroidVersion} (API {id.ApiLevel})"));
            rows.Add(new InfoRow("Serial", device.Serial, Mono: true));
            rows.Add(new InfoRow("Connection", device.Transport));
            if (id.DisplayWidth > 0)
            {
                rows.Add(new InfoRow("Display", $"{id.DisplayWidth} × {id.DisplayHeight}"));
            }

            if (session.Battery is { } battery)
            {
                rows.Add(new InfoRow("Battery", $"{battery.Level}%{(battery.Charging ? ", charging" : string.Empty)}"));
            }

            var profile = _host.State.GetDevice(device.Serial);
            rows.Add(new InfoRow("Lock type", string.IsNullOrEmpty(profile?.LockScreenMode) ? "not set" : profile!.LockScreenMode));
        }
        else if (session.Devices.Count > 0)
        {
            foreach (var d in session.Devices)
            {
                rows.Add(new InfoRow(d.Serial, $"{d.State} ({d.Transport})"));
            }
        }
        else
        {
            rows.Add(new InfoRow("Status", session.Message));
        }

        DeviceRows.ItemsSource = rows;

        ToolRows.ItemsSource = new[]
        {
            new InfoRow("scrcpy", session.Tools is null ? "not installed" : $"{session.Tools.Version}"),
            new InfoRow("Folder", _host.Paths.Root, Mono: true),
            new InfoRow("Version", CommandRouter.AppVersion, Mono: true),
            new InfoRow("Startup", StartupRegistration.IsEnabled() ? "starts with Windows" : "manual"),
        };

        LogText.Text = string.Join(Environment.NewLine, _host.Log.Tail(12));
    }

    private void OnTour(object sender, RoutedEventArgs e) => _window?.StartTour();

    private void OnResetTips(object sender, RoutedEventArgs e)
    {
        _window?.ForgetTips();
        _window?.SetStatus("The one-time hints will be offered again.");
    }

    private void OnOpenCaptures(object sender, RoutedEventArgs e)
    {
        if (_host is not null)
        {
            _window?.OpenFolder(_host.Paths.ScreenshotFolder(_host.Config.App.ScreenshotDirectory));
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
            _host.Log.Error("Could not build or copy diagnostics", ex);
            _window?.SetStatus("Could not build diagnostics: " + ex.Message, true);
        }
    }
}
