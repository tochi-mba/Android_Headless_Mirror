using System.Windows;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror;

/// <summary>
/// Help that lives on the website: opening its pages, and saying once after an update which version
/// this is. The notice uses the bar the one-time hints use, so a question about the lock screen or a
/// phone Windows cannot read still comes first; a notice pushed aside comes back at the next start.
/// </summary>
public partial class MainWindow
{
    /// <summary>Stands in the hint slot while the notice says what is new.</summary>
    private const string WhatsNewNotice = "whats-new";

    /// <summary>Opens a page of the website, and says so, or says where to go when no browser opens.</summary>
    internal void OpenSitePage(string url, string what)
    {
        var opened = UrlOpener.Open(url, _host.Log);
        SetStatus(opened ? $"Opened {what} in your browser." : $"Could not open a browser. The page is at {url}", isError: !opened);
    }

    private void OfferWhatsNew()
    {
        var current = CommandRouter.AppVersion;
        if (!WhatsNew.ShouldOffer(_host.State.Ui.LastRunVersion, current, _host.Config.App.ShowWhatsNew))
        {
            RememberThisVersion();
            return;
        }

        if (_tipShowing is not null || _usbNoticeShowing || _host.Session.PendingLockQuestionSerial is not null || TourLayer.IsRunning)
        {
            return;
        }

        _tipShowing = WhatsNewNotice;
        NoticeTitle.Text = $"Updated to {current}.";
        NoticeText.Text = "See what changed in this version, or carry on.";
        ShowNoticeAnswers(question: false);
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

    private void OnWhatsNew(object sender, RoutedEventArgs e)
    {
        OpenSitePage(SiteLinks.ChangelogFor(CommandRouter.AppVersion), "what is new");
        OnDismissTip(sender, e);
    }
}
