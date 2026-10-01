using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Rex.Core;
using Rex.Mirror.Mirror;
using Rex.Mirror.Native;

namespace Rex.Mirror;

/// <summary>
/// The window's own preferences, all applied live: staying on top, which side the panel is on,
/// which phone buttons the top bar shows, the status bar's hints and frame rate, and a word from
/// the tray when a phone comes or goes while the window is out of sight.
/// </summary>
public partial class MainWindow
{
    /// <summary>How long without a reading before the frame rate is taken to have stopped.</summary>
    private static readonly TimeSpan FrameRateStale = TimeSpan.FromSeconds(2.5);

    private DispatcherTimer? _frameRateTimer;
    private HashSet<string>? _knownPhones;

    /// <summary>The last notification the tray showed, for the status command and the tests.</summary>
    internal string LastNotification { get; private set; } = string.Empty;

    /// <summary>The side panel's scrolling area on screen, in physical pixels (empty when hidden).</summary>
    internal Rect SidebarViewport => _sidebarWheelBounds;

    internal bool SidebarOnLeft => ReferenceEquals(Body.ColumnDefinitions[0], SidebarColumn);

    internal bool HintsShowing => HintText.Visibility == Visibility.Visible;

    internal IEnumerable<string> QuickButtonsShowing =>
        new (Button Button, string Id)[] { (QuickHome, "home"), (QuickBack, "back"), (QuickRecents, "recents"), (QuickSleep, "sleep"), (QuickScreenshot, "screenshot"), (QuickSound, "sound") }
            .Where(pair => pair.Button.Visibility == Visibility.Visible)
            .Select(pair => pair.Id);

    internal int? FrameRateShowing => FrameRateText.Visibility == Visibility.Visible && int.TryParse(FrameRateText.Text.Split(' ')[0], out var rate) ? rate : null;

    private void ApplyWindowPreferences()
    {
        var app = _host.Config.App;
        Topmost = app.AlwaysOnTop;
        ApplySidebarSide(app.SidebarSide == "left");
        ApplyQuickButtons(app.TopBarButtons);
        HintText.Visibility = app.ShowHints ? Visibility.Visible : Visibility.Collapsed;
        foreach (var view in AllViews)
        {
            view.ResetZoomOnTurn = _host.Config.Zoom.ResetOnRotate;
        }

        if (!app.ShowFrameRate && _host.Session.FrameRateCounterOn == false)
        {
            HideFrameRate();
        }

        ApplyGlobalKeys();
        _sound?.Refresh();
    }

    /// <summary>
    /// The side panel to the right of the mirror or to its left. The panel's column moves with it,
    /// so its width, the splitter and hiding it all work the same on either side.
    /// </summary>
    private void ApplySidebarSide(bool left)
    {
        var columns = Body.ColumnDefinitions;
        if (ReferenceEquals(columns[0], SidebarColumn) == left)
        {
            return;
        }

        var splitter = columns[1];
        columns.Clear();
        columns.Add(left ? SidebarColumn : MirrorColumn);
        columns.Add(splitter);
        columns.Add(left ? MirrorColumn : SidebarColumn);
        Grid.SetColumn(MirrorArea, left ? 2 : 0);
        Grid.SetColumn(Sidebar, left ? 0 : 2);

        // The panel's edge faces the mirror, whichever side that is.
        Sidebar.BorderThickness = left ? new Thickness(0, 0, 1, 0) : new Thickness(1, 0, 0, 0);
        TrackOverlay();
    }

    /// <summary>The phone buttons in the top bar; a divider shows only between groups that both have one.</summary>
    private void ApplyQuickButtons(IReadOnlyCollection<string> wanted)
    {
        var buttons = new (Button Button, string Id)[]
        {
            (QuickHome, "home"), (QuickBack, "back"), (QuickRecents, "recents"), (QuickSleep, "sleep"), (QuickScreenshot, "screenshot"), (QuickSound, "sound"),
        };
        foreach (var (button, id) in buttons)
        {
            button.Visibility = wanted.Contains(id) ? Visibility.Visible : Visibility.Collapsed;
        }

        var navigation = wanted.Any(id => id is "home" or "back" or "recents");
        var screen = wanted.Any(id => id is "sleep" or "screenshot" or "sound");
        QuickDivider.Visibility = navigation && screen ? Visibility.Visible : Visibility.Collapsed;
        QuickEndDivider.Visibility = navigation || screen ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Ends a hold on the keyboard for the PC view once left Alt is seen to be up. The hold ends
    /// when the keyboard hook sees Alt released, but Windows skips a hook that answers late, so
    /// a busy moment could lose the release and leave typing cut off from the phone until Alt
    /// was pressed again.
    /// </summary>
    private void ReleaseStaleAltHold()
    {
        if (NativeMethods.IsKeyDown(NativeMethods.VK_LMENU))
        {
            return;
        }

        foreach (var view in AllViews)
        {
            if (view.HoldingKeyboard)
            {
                _host.Log.Info("Left Alt is up but its release never arrived; the phone has the keyboard again.");
                view.ReleaseKeyboard();
            }
        }
    }

    // ----- The frame rate in the status bar -----

    private void FollowFrameRate(ScrcpyProcess scrcpy)
    {
        scrcpy.FrameRateChanged += rate => Dispatcher.BeginInvoke(() =>
        {
            if (ReferenceEquals(scrcpy, _host.Session.Scrcpy))
            {
                ShowFrameRate(rate);
            }
        });
    }

    private void ShowFrameRate(int rate)
    {
        FrameRateText.Text = rate + " fps";
        FrameRateText.Visibility = Visibility.Visible;

        // scrcpy prints a reading every second while its counter runs; when they stop, so has it.
        _frameRateTimer ??= new DispatcherTimer { Interval = FrameRateStale };
        _frameRateTimer.Tick -= OnFrameRateStale;
        _frameRateTimer.Tick += OnFrameRateStale;
        _frameRateTimer.Stop();
        _frameRateTimer.Start();
    }

    private void OnFrameRateStale(object? sender, EventArgs e) => HideFrameRate();

    private void HideFrameRate()
    {
        _frameRateTimer?.Stop();
        FrameRateText.Visibility = Visibility.Collapsed;
    }

    // ----- A word from the tray when a phone comes or goes -----

    /// <summary>
    /// Says so from the tray when a phone becomes ready or goes away, if that is wanted and the
    /// window is out of sight; with the window on screen, the window itself says it.
    /// </summary>
    private void NoticeConnections()
    {
        var session = _host.Session;
        var ready = session.Devices.Where(d => d.IsReady).Select(d => d.Serial).ToHashSet(StringComparer.Ordinal);
        var known = _knownPhones;
        _knownPhones = ready;
        if (known is null || !_host.Config.App.NotifyConnections || (IsVisible && WindowState != WindowState.Minimized))
        {
            return;
        }

        foreach (var serial in ready.Except(known))
        {
            Notify("Phone connected", $"{PhoneName(serial)} is ready to mirror.");
        }

        foreach (var serial in known.Except(ready))
        {
            Notify("Phone disconnected", $"{PhoneName(serial)} is no longer connected.");
        }
    }

    private string PhoneName(string serial) =>
        _host.State.GetDevice(serial) is { Name.Length: > 0 } profile ? profile.Name : serial;

    private void Notify(string title, string text)
    {
        LastNotification = title + ": " + text;
        _host.Log.Info("Notified: " + LastNotification);
        _host.Tray?.Notify(title, text);
    }
}
