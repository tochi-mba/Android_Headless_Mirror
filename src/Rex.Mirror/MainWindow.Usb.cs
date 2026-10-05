using System.Windows;
using Rex.Core;

namespace Rex.Mirror;

/// <summary>
/// The notice bar's part in USB recovery (<see cref="Services.UsbDoctor"/>): what Windows reports,
/// whether the app is already fixing it, and the two ways to fix it when it is not. It outranks a
/// hint and yields to the lock-screen question, which only comes with a phone that is already here.
/// </summary>
public partial class MainWindow
{
    private bool _usbNoticeShowing;

    /// <summary>Shows or clears the USB notice; true while it holds the notice bar.</summary>
    private bool ShowUsbNotice()
    {
        var doctor = _host.Usb;
        var notice = _fullscreen ? UsbNotice.None : doctor.Decision.Notice;
        if (notice == UsbNotice.None)
        {
            if (_usbNoticeShowing)
            {
                _usbNoticeShowing = false;
                ShowUsbAnswers(UsbNotice.None);
            }

            return false;
        }

        // A hint that was up gives the bar away without counting as seen, so it comes back later.
        _tipShowing = null;
        _usbNoticeShowing = true;
        var (title, text) = UsbRecoveryText.For(notice, doctor.Problems, doctor.Decision.Attempts);
        NoticeTitle.Text = title;
        NoticeText.Text = text;
        foreach (var button in new[] { NoticePattern, NoticeOther, NoticeNone, NoticeLater, NoticeDismiss, NoticeWhatsNew, NoticeBeside, NoticeBesideNotNow, NoticeBesideNever })
        {
            button.Visibility = Visibility.Collapsed;
        }

        ShowUsbAnswers(notice);
        NoticeBar.Visibility = Visibility.Visible;
        return true;
    }

    /// <summary>
    /// The buttons that fit the notice: both fixes when nothing will happen by itself, the prompted
    /// repair once automatic attempts have run out, and nothing but Hide while the task is working.
    /// </summary>
    private void ShowUsbAnswers(UsbNotice notice)
    {
        var (repair, automatic) = UsbAnswers(notice, _host.Usb.RepairsAutomatically);
        NoticeUsbRepair.Visibility = repair ? Visibility.Visible : Visibility.Collapsed;
        NoticeUsbAuto.Visibility = automatic ? Visibility.Visible : Visibility.Collapsed;
        NoticeUsbHide.Visibility = notice == UsbNotice.None ? Visibility.Collapsed : Visibility.Visible;
        NoticeUsbRepair.IsEnabled = NoticeUsbAuto.IsEnabled = !_host.Usb.Busy;
    }

    /// <summary>Which fixes a USB notice offers: (the prompted repair, setting up auto-repair).</summary>
    internal static (bool Repair, bool Automatic) UsbAnswers(UsbNotice notice, bool repairsAutomatically) => notice switch
    {
        UsbNotice.Offer => (true, !repairsAutomatically),
        UsbNotice.Advice => (true, false),
        _ => (false, false),
    };

    private void OnUsbChanged()
    {
        if (_quitting)
        {
            return;
        }

        OnSessionChanged();
        SettingsPanel.Refresh();
    }

    private async void OnUsbRepair(object sender, RoutedEventArgs e) => await RepairUsbAsync();

    private async void OnUsbAutoRepair(object sender, RoutedEventArgs e)
    {
        SetStatus("Waiting for administrator approval…");
        var (ok, message) = await _host.Usb.EnableAutoRepairAsync();
        SetStatus(message, isError: !ok);
    }

    private void OnUsbHide(object sender, RoutedEventArgs e) => _host.Usb.Dismiss();
}
