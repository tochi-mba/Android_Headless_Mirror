using System.Windows;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror;

/// <summary>
/// Help that lives on the website, and what the app says once after an update. An update that
/// brought something the person has not had yet opens its own short onboarding, made of only those
/// features, with a tour that points at each in the window; one that brought nothing to learn (a
/// fix, a first install, a downgrade) says only which version this is, in the bar the one-time
/// hints use, so a question about the lock screen or a phone Windows cannot read still comes first.
/// </summary>
public partial class MainWindow
{
    /// <summary>Stands in the hint slot while the notice says what is new.</summary>
    private const string WhatsNewNotice = "whats-new";

    /// <summary>While the update's tour runs its callouts sit over the mirror, so the picture steps aside.</summary>
    private bool _touringUpdate;

    /// <summary>Opens a page of the website, and says so, or says where to go when no browser opens.</summary>
    internal void OpenSitePage(string url, string what)
    {
        var opened = UrlOpener.Open(url, _host.Log);
        SetStatus(opened ? $"Opened {what} in your browser." : $"Could not open a browser. The page is at {url}", isError: !opened);
    }

    private void OfferWhatsNew()
    {
        var previous = _host.State.Ui.LastRunVersion;
        var current = CommandRouter.AppVersion;
        if (!WhatsNew.ShouldOffer(previous, current, _host.Config.App.ShowWhatsNew))
        {
            RememberThisVersion();
            return;
        }

        // The first-run guide and the tour come first; the update waits for the next start.
        if (OnboardingVisible || TourLayer.IsRunning)
        {
            return;
        }

        var features = WhatsNew.FeaturesBetween(previous, current);
        if (features.Count > 0)
        {
            ShowUpdateOnboarding(previous, features);
            return;
        }

        if (_tipShowing is not null || _usbNoticeShowing || _host.Session.PendingLockQuestionSerial is not null)
        {
            return;
        }

        _tipShowing = WhatsNewNotice;
        NoticeTitle.Text = $"Updated to {current}.";
        NoticeText.Text = "Fixes and small improvements. See the details, or carry on.";
        ShowNoticeAnswers(question: false);
        NoticeWhatsNew.ToolTip = "What changed in this version, on the website";
        NoticeWhatsNew.Visibility = Visibility.Visible;
        NoticeBar.Visibility = Visibility.Visible;
    }

    private void RememberThisVersion()
    {
        if (_host.State.Ui.LastRunVersion != CommandRouter.AppVersion)
        {
            _host.State.SetUi(_host.State.Ui with { LastRunVersion = CommandRouter.AppVersion });
        }
    }

    /// <summary>The notice's What's new: this version's notes on the website.</summary>
    private void OnWhatsNew(object sender, RoutedEventArgs e)
    {
        OpenSitePage(SiteLinks.ChangelogFor(CommandRouter.AppVersion), "what is new");
        OnDismissTip(sender, e);
    }

    /// <summary>The sheet of what is new, over the window; the phone's picture steps aside for it.</summary>
    private void ShowUpdateOnboarding(string? previous, IReadOnlyList<UpdateFeature> features)
    {
        UpdateOnboarding.Show(previous ?? string.Empty, CommandRouter.AppVersion, features);
        SyncPictureShown();
    }

    /// <summary>
    /// The update's own tour: one stop for each feature the person did not have yet, pointing at
    /// where it lives in the window. Nothing they already knew is shown again.
    /// </summary>
    internal void TourTheUpdate(IReadOnlyList<UpdateFeature> features)
    {
        FinishUpdateOnboarding(openFullNotes: false);
        if (TourLayer.IsRunning)
        {
            return;
        }

        // Like the first-run tour, it opens the side panel to point into it and shuts it again after.
        var reopened = !_sidebarWanted;
        if (reopened)
        {
            SetSidebarVisible(true);
        }

        _touringUpdate = true;
        SyncPictureShown();
        TourLayer.Start(
            features.Select(f => new Views.TourStep(f.Target, f.Title, $"{f.Body} {f.Where}.", "bottom")).ToArray(),
            name => FindName(name) as FrameworkElement,
            _ =>
            {
                _touringUpdate = false;
                SyncPictureShown();
                if (reopened)
                {
                    SetSidebarVisible(false);
                }
            });
    }

    /// <summary>Closes the update's onboarding, as seen, and opens this version's notes when asked.</summary>
    internal void FinishUpdateOnboarding(bool openFullNotes)
    {
        if (openFullNotes)
        {
            OpenSitePage(SiteLinks.ChangelogFor(CommandRouter.AppVersion), "the full release notes");
        }

        UpdateOnboarding.Close();
        RememberThisVersion();
        SyncPictureShown();
    }
}
