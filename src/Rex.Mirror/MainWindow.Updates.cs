using System.Windows;
using System.Windows.Threading;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror;

/// <summary>
/// The optional check for a newer version: at start, and on an hourly timer that runs only while
/// the setting is on (the check itself happens at most once a day). A newer version is offered in
/// the notice bar once there is room: download it, read what is new, skip it, or not now.
/// </summary>
public partial class MainWindow
{
    private const string UpdateNotice = "update-available";

    private UpdateChecker? _updates;
    private DispatcherTimer? _updateTimer;
    private Version? _updateWaiting;

    /// <summary>The newer version on offer in the notice bar, or waiting for room there, for the status command.</summary>
    internal Version? UpdateOffered => _updateWaiting;

    /// <summary>Starts or stops the hourly look as the setting changes, and looks now when it is due.</summary>
    private void FollowUpdateSetting()
    {
        var on = _host.Config.App.CheckForUpdates;
        if (!on)
        {
            _updateTimer?.Stop();
            return;
        }

        _updates ??= new UpdateChecker(_host);
        if (_updateTimer is null)
        {
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
            _updateTimer.Tick += (_, _) => _ = LookForUpdateAsync();
        }

        _updateTimer.Start();
        _ = LookForUpdateAsync();
    }

    private async Task LookForUpdateAsync()
    {
        if (_updates is null || _quitting)
        {
            return;
        }

        if (await _updates.CheckAsync() is { } newer)
        {
            _updateWaiting = newer;
            OfferUpdate();
        }
    }

    /// <summary>The newer version in the notice bar, when nothing else is using it; otherwise it waits its turn.</summary>
    private void OfferUpdate()
    {
        if (_updateWaiting is not { } newer || _quitting)
        {
            return;
        }

        if (_tipShowing is not null || _usbNoticeShowing || _host.Session.PendingLockQuestionSerial is not null || NoticeBar.Visibility == Visibility.Visible)
        {
            return;
        }

        _tipShowing = UpdateNotice;
        NoticeTitle.Text = $"Version {newer} is out.";
        NoticeText.Text = $"You have {CommandRouter.AppVersion}. Download it from GitHub, or see what is new first.";
        ShowNoticeAnswers(question: false);
        NoticeDismiss.Visibility = Visibility.Collapsed;
        ShowUpdateButtons(true);
        NoticeBar.Visibility = Visibility.Visible;
    }

    private void ShowUpdateButtons(bool shown)
    {
        foreach (var button in new[] { NoticeUpdateGet, NoticeUpdateNotes, NoticeUpdateLater, NoticeUpdateSkip })
        {
            button.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnUpdateAnswer(object sender, RoutedEventArgs e)
    {
        if (_updateWaiting is not { } newer || sender is not FrameworkElement { Tag: string answer })
        {
            return;
        }

        switch (answer)
        {
            case "get":
                OpenSitePage(UpdateCheck.ReleasePage(newer), $"version {newer}");
                break;
            case "notes":
                // What is new stays open as a question until it is answered another way.
                OpenSitePage(SiteLinks.ChangelogFor(newer.ToString()), "what is new");
                return;
            case "skip":
                _host.State.SetUi(_host.State.Ui with { SkippedVersion = newer.ToString() });
                SetStatus($"Version {newer} will not be offered again; a later one will.");
                break;
        }

        // Not now, downloaded or skipped: it is not offered again until the next check finds it.
        _updateWaiting = null;
        _tipShowing = null;
        ShowUpdateButtons(false);
        NoticeBar.Visibility = Visibility.Collapsed;
    }
}
