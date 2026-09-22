using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Rex.Core;
using Rex.Mirror.Services;
using Rex.Mirror.Session;

namespace Rex.Mirror.Views;

public sealed record ActionTileModel(string Id, string Label, string Detail, Geometry Icon);

public partial class ControlsPanel : UserControl
{
    private MainWindow? _window;
    private AppHost? _host;

    public ControlsPanel()
    {
        InitializeComponent();
    }

    public void Attach(MainWindow window, AppHost host)
    {
        _window = window;
        _host = host;
        NavigationTiles.ItemsSource = Tiles(["home", "back", "recents", "power", "wake", "sleep", "volume-up", "volume-down", "mute", "notifications", "quick-settings", "collapse"]);
        ViewTiles.ItemsSource = Tiles(["rotate-device", "rotate-left", "rotate-right", "pause", "resume", "reset-capture", "screenshot", "fullscreen", "fps"]);
        Refresh();
    }

    private ActionTileModel[] Tiles(string[] ids) =>
        ids.Select(id => MirrorActions.Find(id)!)
            .Select(a => new ActionTileModel(a.Id, a.Label, a.Detail, (Geometry)FindResource(IconKey(a.Id))))
            .ToArray();

    private static string IconKey(string id) => id switch
    {
        "home" => "IconHome",
        "back" => "IconBack",
        "recents" => "IconRecents",
        "power" => "IconPower",
        "wake" => "IconSun",
        "sleep" => "IconMoon",
        "volume-up" => "IconVolumeUp",
        "volume-down" => "IconVolumeDown",
        "mute" => "IconVolumeDown",
        "notifications" => "IconBell",
        "quick-settings" => "IconSliders",
        "collapse" => "IconBack",
        "rotate-device" or "rotate-left" or "rotate-right" => "IconRotate",
        "pause" => "IconPause",
        "resume" => "IconPlay",
        "reset-capture" => "IconRefresh",
        "screenshot" => "IconCamera",
        "fullscreen" => "IconFullscreen",
        "fps" => "IconInfo",
        _ => "IconInfo",
    };

    public void Refresh()
    {
        if (_host is null || _window is null)
        {
            return;
        }

        var session = _host.Session;
        var mirroring = session.IsMirroring;
        SessionButton.Content = mirroring ? "Stop mirror" : session.Phase == SessionPhase.Stopped ? "Start mirror" : "Waiting for phone…";
        SessionButton.IsEnabled = mirroring || session.Phase == SessionPhase.Stopped;
        RestartButton.IsEnabled = mirroring;
        var ready = session.Devices.Any(d => d.IsReady);
        NavigationTiles.IsEnabled = ready;
        ViewTiles.IsEnabled = ready;

        ZoomLabel.Text = $"{_window.Host.Zoom * 100:0}%";
        ZoomReset.IsEnabled = _window.Host.View.IsZoomed;

        var guide = _window.Guide;
        PatternSection.Visibility = guide is not null ? Visibility.Visible : Visibility.Collapsed;
        if (guide is not null)
        {
            PatternToggle.Content = guide.IsVisible ? "Hide guide" : "Show guide";
            PatternCalibrate.Content = guide.IsCalibrating ? "Save calibration" : "Calibrate";
            PatternStatus.Text = guide.IsVisible
                ? "Guide is showing (source: " + guide.Source + "). Drag the pattern as usual; nothing is recorded."
                : "Appears automatically when the lock screen is black. Ctrl+Alt+P toggles it.";
        }
    }

    private async void OnTile(object sender, RoutedEventArgs e)
    {
        if (_window is not null && sender is Button { Tag: string id })
        {
            await _window.RunActionAsync(id);
            Refresh();
        }
    }

    private void OnSessionButton(object sender, RoutedEventArgs e)
    {
        if (_host is null)
        {
            return;
        }

        if (_host.Session.IsMirroring)
        {
            _host.Session.StopMirror();
        }
        else
        {
            _host.Session.StartAgain();
        }
    }

    private void OnRestart(object sender, RoutedEventArgs e) => _host?.Session.RestartMirror();

    private void OnPatternToggle(object sender, RoutedEventArgs e)
    {
        _window?.Guide?.Toggle();
        Refresh();
    }

    private void OnPatternCalibrate(object sender, RoutedEventArgs e)
    {
        _window?.Guide?.StartCalibration();
        _window?.Host.FocusChild();
        Refresh();
    }
}
