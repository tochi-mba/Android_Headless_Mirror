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
        RotationPortrait.ToolTip = Shortcuts.Tip("Lock the phone to portrait", "rotation-portrait");
        RotationLandscape.ToolTip = Shortcuts.Tip("Lock the phone to landscape", "rotation-landscape");
        RotationAuto.ToolTip = Shortcuts.Tip("Let the phone rotate by itself", "rotation-auto");
        PatternToggle.ToolTip = Shortcuts.Tip("Show or hide the nine-dot guide", "pattern-guide");
        PatternCalibrate.ToolTip = Shortcuts.Tip("Line the guide up with the arrow keys", "pattern-calibrate");
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
        RotationPortrait.IsEnabled = ready;
        RotationLandscape.IsEnabled = ready;
        RotationAuto.IsEnabled = ready;

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
                : $"Appears automatically when the lock screen is black. {Shortcuts.Gesture("pattern-guide")} toggles it.";
        }
    }

    private async void OnRotation(object sender, RoutedEventArgs e)
    {
        if (_window is null || sender is not System.Windows.Controls.Primitives.ToggleButton { Tag: string id })
        {
            return;
        }

        await _window.RunActionAsync(id);
        await RefreshRotationAsync();
        Refresh();
    }

    /// <summary>
    /// Shows which way the phone is actually locked, by asking the phone rather than remembering
    /// what was last pressed: rotation can be changed on the handset too, and a choice that lies
    /// about the current state is worse than no choice at all.
    /// </summary>
    public async Task RefreshRotationAsync()
    {
        var serial = _host?.Session.ActiveDevice?.Serial;
        var adb = _host?.Session.Adb;
        if (adb is null || string.IsNullOrEmpty(serial))
        {
            SetRotation(null);
            return;
        }

        var auto = await adb.GetSettingAsync(serial, "system", "accelerometer_rotation").ConfigureAwait(true);
        if (auto.Ok && auto.Text.Trim() == "1")
        {
            SetRotation(RotationAuto);
            return;
        }

        var user = await adb.GetSettingAsync(serial, "system", "user_rotation").ConfigureAwait(true);
        SetRotation(!user.Ok ? null : user.Text.Trim() switch
        {
            "0" or "2" => RotationPortrait,
            "1" or "3" => RotationLandscape,
            _ => null,
        });
    }

    private void SetRotation(System.Windows.Controls.Primitives.ToggleButton? chosen)
    {
        RotationPortrait.IsChecked = ReferenceEquals(chosen, RotationPortrait);
        RotationLandscape.IsChecked = ReferenceEquals(chosen, RotationLandscape);
        RotationAuto.IsChecked = ReferenceEquals(chosen, RotationAuto);
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
