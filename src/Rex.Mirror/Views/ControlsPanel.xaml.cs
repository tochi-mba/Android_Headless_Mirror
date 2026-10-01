using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Rex.Core;
using Rex.Mirror.Services;
using Rex.Mirror.Session;

namespace Rex.Mirror.Views;

/// <summary>
/// One tile: the action it runs, its icon, and a tooltip that ends with every key that does the
/// same. The pause tile turns into Resume in place, so the tile a person just pressed keeps focus.
/// </summary>
public sealed class ActionTileModel : INotifyPropertyChanged
{
    public ActionTileModel(MirrorAction action, Geometry icon, string automationId)
    {
        AutomationId = automationId;
        Show(action, icon);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Stays the same when the tile changes action, so automation can hold on to it.</summary>
    public string AutomationId { get; }

    public string Id { get; private set; } = string.Empty;

    public string Label { get; private set; } = string.Empty;

    public string Tip { get; private set; } = string.Empty;

    public Geometry Icon { get; private set; } = Geometry.Empty;

    public void Show(MirrorAction action, Geometry icon)
    {
        if (Id == action.Id)
        {
            return;
        }

        Id = action.Id;
        Label = action.Label;
        Tip = Shortcuts.Tip(action.Detail, action.Id);
        Icon = icon;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}

public partial class ControlsPanel : UserControl
{
    /// <summary>What a tile or button says while there is no phone for it to act on.</summary>
    public const string NeedsPhone = "Connect a phone to use these";

    internal static readonly string[] PhoneTileIds =
        ["home", "back", "recents", "power", "wake", "sleep", "volume-up", "volume-down", "mute", "notifications", "quick-settings", "collapse"];

    /// <summary>
    /// What the PC does with the picture. Rotating the phone itself is under phone orientation, and
    /// scrcpy's FPS counter is left to the command line: it prints to a console nobody can see here.
    /// </summary>
    internal static readonly string[] ViewTileIds = ["rotate-left", "rotate-right", "pause", "reset-capture", "screenshot", "fullscreen"];

    /// <summary>Up then down, left then right, the way their keys sit; tap and like end the rows.</summary>
    internal static readonly string[] GestureTileIds = ["swipe-down", "swipe-up", "tap", "swipe-right", "swipe-left", "like"];

    private readonly ActionTileModel _pauseTile;
    private MainWindow? _window;
    private AppHost? _host;
    private readonly Dictionary<Guid, TransferRow> _transferRows = [];

    private sealed record TransferRow(Grid Root, TextBlock State, ProgressBar Progress, Button Action);

    /// <summary>
    /// True while the panel shows state it has read (the phone's orientation, browse mode). Only a
    /// person, or assistive technology acting for one, changing a choice should run its action.
    /// </summary>
    private bool _syncing;

    public ControlsPanel()
    {
        InitializeComponent();
        NavigationTiles.ItemsSource = Tiles(PhoneTileIds);
        var view = Tiles(ViewTileIds);
        _pauseTile = view.Single(t => t.Id == "pause");
        ViewTiles.ItemsSource = view;
        GestureTiles.ItemsSource = Tiles(GestureTileIds);
    }

    public void Attach(MainWindow window, AppHost host)
    {
        _window = window;
        _host = host;
        PatternToggle.ToolTip = Shortcuts.Tip("Show or hide the nine-dot guide", "pattern-guide");
        PatternCalibrate.ToolTip = Shortcuts.Tip("Line the guide up with the arrow keys", "pattern-calibrate");
        ZoomOut.ToolTip = Shortcuts.Tip("Zoom the PC view out", "zoom-out");
        ZoomIn.ToolTip = Shortcuts.Tip("Zoom the PC view in", "zoom-in");
        ZoomReset.ToolTip = Shortcuts.Tip("Fit the whole phone screen in the window", "zoom-reset");
        CopyAdd.ToolTip = Shortcuts.Tip("Show another live view of the phone beside this one", "copy-add");
        CopyRemove.ToolTip = Shortcuts.Tip("Close the last copy", "copy-remove");
        SendFiles.ToolTip = "Choose one or more files to send to the phone";
        SendCopiedFiles.ToolTip = Shortcuts.Tip("Send files copied in File Explorer", "send-copied-files");
        Refresh();
    }

    private ActionTileModel[] Tiles(IEnumerable<string> ids) =>
        ids.Select(id => new ActionTileModel(MirrorActions.Find(id)!, Icon(id), "tile-" + id)).ToArray();

    private Geometry Icon(string id) => (Geometry)FindResource(ActionIcons.For(id) ?? "IconInfo");

    /// <summary>The action the pause tile runs: it resumes a picture that is frozen and pauses one that is not.</summary>
    internal static string PauseTileAction(bool paused) => paused ? "resume" : "pause";

    /// <summary>What the session button says and does in each phase of the mirror.</summary>
    internal static (string Content, string Tip, bool Enabled) SessionButtonState(bool mirroring, SessionPhase phase) =>
        mirroring ? ("Stop mirror", "Close the mirror and keep waiting in the tray", true)
        : phase == SessionPhase.Stopped ? ("Start mirror", "Open the mirror again", true)
        : phase == SessionPhase.Starting ? ("Starting…", "The mirror is on its way", false)
        : ("Waiting for phone…", "The mirror opens by itself when a phone connects", false);

    public void Refresh()
    {
        // A refresh queued before quitting can run after the application's resources are gone.
        if (_host is null || _window is null || _window.Quitting)
        {
            return;
        }

        var session = _host.Session;
        var mirroring = session.IsMirroring;
        var (content, tip, enabled) = SessionButtonState(mirroring, session.Phase);
        SessionButton.Content = content;
        SessionButton.ToolTip = tip;
        SessionButton.IsEnabled = enabled;
        RestartButton.IsEnabled = mirroring;
        RestartButton.ToolTip = mirroring ? "Relaunch scrcpy (applies changed mirror settings)" : "There is no mirror to restart yet";

        var ready = session.Devices.Any(d => d.IsReady);
        NavigationTiles.IsEnabled = ready;
        ViewTiles.IsEnabled = ready;
        GestureTiles.IsEnabled = ready;
        Explain(RotationPortrait, ready, "rotation-portrait");
        Explain(RotationLandscape, ready, "rotation-landscape");
        Explain(RotationAuto, ready, "rotation-auto");
        Explain(RotateDevice, ready, "rotate-device");
        Explain(BrowseToggle, ready, "browse");
        Explain(KeyboardLayout, ready, "keyboard-layout");
        Explain(ClipboardCopy, ready, "copy");
        Explain(ClipboardCut, ready, "cut");
        Explain(ClipboardPaste, ready, "paste");
        Explain(ClipboardType, ready, "paste-text");

        _pauseTile.Show(MirrorActions.Find(PauseTileAction(_window.MirrorPaused))!, Icon(PauseTileAction(_window.MirrorPaused)));

        var browsing = _window.BrowseMode;
        Sync(() => BrowseToggle.IsChecked = browsing);
        BrowseState.Text = browsing ? "On" : "Off";
        BrowseState.Foreground = (Brush)FindResource(browsing ? "Signal" : "Muted");
        KeyboardStatus.Text = browsing
            ? "Browse mode is on: Up and Down move through a feed, Left and Right turn pages, Enter taps, L likes, M mutes, Backspace goes back. Esc leaves."
            : $"Typing goes straight to the phone. {Shortcuts.Gesture("browse")} turns on browse mode, where the arrow keys, Enter, L, M and Backspace drive a feed.";

        ZoomLabel.Text = $"{_window.Host.Zoom * 100:0}%";
        ZoomReset.IsEnabled = _window.Host.View.IsZoomed;

        // A disabled Add says why in the line under it: no room, the limit, or a phone on its side.
        var copies = _window.CopiesState;
        CopyAdd.IsEnabled = copies.CanAdd;
        CopyRemove.IsEnabled = copies.CanRemove;
        CopiesStatus.Text = copies.Summary;

        RefreshFiles();

        RefreshFavourites();

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

    internal void RefreshFiles()
    {
        if (_host is null || _window is null) return;
        FilesSection.Visibility = _host.Config.Transfer.Enabled ? Visibility.Visible : Visibility.Collapsed;
        var ready = _host.Session.Devices.Any(device => device.IsReady);
        SendFiles.IsEnabled = ready;
        SendCopiedFiles.IsEnabled = ready;
        AutomationProperties.SetHelpText(SendFiles, ready ? string.Empty : NeedsPhone);
        AutomationProperties.SetHelpText(SendCopiedFiles, ready ? string.Empty : NeedsPhone);
        var jobs = _window.Transfers;
        foreach (var removed in _transferRows.Keys.Where(id => jobs.All(job => job.Id != id)).ToArray())
        {
            _transferRows.Remove(removed);
        }

        foreach (var job in jobs)
        {
            if (!_transferRows.TryGetValue(job.Id, out var row))
            {
                row = CreateTransferRow(job);
                _transferRows[job.Id] = row;
            }

            UpdateTransferRow(row, job);
        }

        var ordered = jobs.Select(job => _transferRows[job.Id].Root).ToArray();
        if (!TransferRows.Children.Cast<UIElement>().SequenceEqual(ordered))
        {
            TransferRows.Children.Clear();
            foreach (var row in ordered) TransferRows.Children.Add(row);
        }

        ClearTransfers.IsEnabled = jobs.Any(job => TransferProgress.IsFinished(job.State));
        ClearTransfers.Visibility = jobs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private TransferRow CreateTransferRow(TransferJob job)
    {
        var root = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        AutomationProperties.SetAutomationId(root, "transfer-" + job.Id.ToString("N"));
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = job.Item.Entry.Name, TextTrimming = TextTrimming.CharacterEllipsis });
        var state = new TextBlock { Style = (Style)FindResource("MutedText"), FontSize = 11 };
        AutomationProperties.SetAutomationId(state, "transfer-state-" + job.Id.ToString("N"));
        text.Children.Add(state);
        var progress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 4,
            Margin = new Thickness(0, 4, 8, 0),
            IsHitTestVisible = false,
        };
        text.Children.Add(progress);
        root.Children.Add(text);
        var action = new Button { Tag = job.Id, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(6, 0, 0, 0) };
        action.Click += OnTransferAction;
        Grid.SetColumn(action, 1);
        root.Children.Add(action);
        return new TransferRow(root, state, progress, action);
    }

    private static void UpdateTransferRow(TransferRow row, TransferJob job)
    {
        var words = job.State == TransferState.Sending
            ? TransferProgress.Sending(job.Sent, job.Item.Entry.Size, job.BytesPerSecond)
            : TransferProgress.Words(job.State, job.Item.Target, job.Why);
        row.State.Text = words;
        row.Progress.Value = job.Percent;
        row.Progress.Visibility = job.State == TransferState.Sending ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(row.Root, job.Item.Entry.Name + ", " + words);

        var retry = job.State is TransferState.Failed or TransferState.Cancelled;
        var actionable = retry || job.State is TransferState.Waiting or TransferState.Sending or TransferState.Installing;
        row.Action.Visibility = actionable ? Visibility.Visible : Visibility.Collapsed;
        row.Action.Content = retry ? "Try again" : "Cancel";
        AutomationProperties.SetName(row.Action, (retry ? "Try " : "Cancel ") + job.Item.Entry.Name + (retry ? " again" : string.Empty));
        AutomationProperties.SetAutomationId(row.Action, (retry ? "transfer-retry-" : "transfer-cancel-") + job.Id.ToString("N"));
    }

    private async void OnSendFiles(object sender, RoutedEventArgs e)
    {
        if (_window is not null) await _window.SendFilesAsync();
    }

    private async void OnSendCopiedFiles(object sender, RoutedEventArgs e)
    {
        if (_window is not null) await _window.SendCopiedFilesAsync();
    }

    private void OnTransferAction(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid id } || _window is null) return;
        var state = _window.Transfers.FirstOrDefault(job => job.Id == id)?.State;
        if (state is TransferState.Failed or TransferState.Cancelled) _window.RetryTransfer(id);
        else _window.CancelTransfer(id);
    }

    private void OnClearTransfers(object sender, RoutedEventArgs e) => _window?.ClearFinishedTransfers();

    private string _favouritesShown = string.Empty;

    /// <summary>
    /// The APPS section: a tile for each favourite, up to the number set, in the person's order. It
    /// is rebuilt only when the favourites or the setting change, since the panel refreshes often.
    /// </summary>
    internal void RefreshFavourites()
    {
        if (_host is null || _window is null)
        {
            return;
        }

        var settings = _host.Config.Apps;
        AppsSection.Visibility = settings.FavouritesOnControls ? Visibility.Visible : Visibility.Collapsed;
        var favourites = _window.Favourites().Take(settings.FavouritesOnControlsMost).ToArray();
        var shown = string.Join('|', favourites.Select(a => a.Package + "=" + a.Name)) + "|" + settings.FavouritesOnControls;
        if (shown == _favouritesShown)
        {
            return;
        }

        _favouritesShown = shown;
        FavouriteTiles.Children.Clear();
        FavouritesHint.Visibility = favourites.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        for (var i = 0; i < favourites.Length; i++)
        {
            var app = favourites[i];
            var face = new StackPanel();
            var tile = AppsPanel.Tile(app, 24);
            tile.HorizontalAlignment = HorizontalAlignment.Center;
            face.Children.Add(tile);
            face.Children.Add(new TextBlock { Text = app.Name, FontSize = 11, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 5, 0, 0) });
            var key = i < Shortcuts.FavouriteKeys && settings.FavouriteKeys ? " · " + Shortcuts.Gesture(Shortcuts.FavouritePrefix + (i + 1)) : string.Empty;
            var button = new Button { Content = face, Margin = new Thickness(3), MinHeight = 58, Padding = new Thickness(4, 8, 4, 8), HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = $"Open {app.Name}{key}" };
            AutomationProperties.SetAutomationId(button, "favourite-tile-" + (i + 1));
            AutomationProperties.SetName(button, app.Name);
            button.Click += async (_, _) => await _window.OpenAppAsync(app, _host.Config.Apps.OpenFresh);
            FavouriteTiles.Children.Add(button);
        }
    }

    private void OnAllApps(object sender, RoutedEventArgs e) => _window?.ShowTab("apps");

    /// <summary>
    /// Turns a control on or off with the phone, and says why while it is off: a greyed button
    /// with no reason reads as broken. While on, its tooltip is the action's own words and keys.
    /// </summary>
    private static void Explain(Control control, bool ready, string actionId)
    {
        control.IsEnabled = ready;
        control.ToolTip = ready ? Shortcuts.Tip(MirrorActions.Find(actionId)!.Detail, actionId) : NeedsPhone;
        AutomationProperties.SetHelpText(control, ready ? string.Empty : NeedsPhone);
    }

    private void Sync(Action show)
    {
        _syncing = true;
        try
        {
            show();
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>
    /// Runs when a choice becomes checked, which a click, the keyboard and UI Automation all do;
    /// a click alone would leave Narrator users choosing a segment that does nothing.
    /// </summary>
    private async void OnRotation(object sender, RoutedEventArgs e)
    {
        if (_syncing || _window is null || sender is not ToggleButton { Tag: string id })
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

    private void SetRotation(ToggleButton? chosen) => Sync(() =>
    {
        RotationPortrait.IsChecked = ReferenceEquals(chosen, RotationPortrait);
        RotationLandscape.IsChecked = ReferenceEquals(chosen, RotationLandscape);
        RotationAuto.IsChecked = ReferenceEquals(chosen, RotationAuto);
    });

    private async void OnTile(object sender, RoutedEventArgs e)
    {
        if (_window is not null && sender is Button { Tag: string id })
        {
            await _window.RunActionAsync(id);
            Refresh();
        }
    }

    /// <summary>
    /// Turns browse mode the way the toggle was just turned, then shows what the window says,
    /// which is the truth. The keys that turn the mode on and off refresh the toggle too.
    /// </summary>
    private async void OnBrowseToggle(object sender, RoutedEventArgs e)
    {
        if (_syncing || _window is null || BrowseToggle.IsChecked == _window.BrowseMode)
        {
            return;
        }

        await _window.RunActionAsync("browse");
        Refresh();
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
