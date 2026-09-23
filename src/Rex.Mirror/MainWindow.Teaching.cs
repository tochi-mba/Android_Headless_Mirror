using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Rex.Core;
using Rex.Mirror.Views;

namespace Rex.Mirror;

/// <summary>
/// The parts of the window that teach it: the first-run tour, the one-time hints, the question it
/// asks before something risky, and the chip that picks between phones.
///
/// They live beside the window rather than inside it because none of them is about mirroring, and
/// the window is long enough already.
/// </summary>
public partial class MainWindow
{
    private bool _tourConsidered;
    private string? _tipShowing;
    private TaskCompletionSource<bool>? _confirm;
    private DispatcherTimer? _tipTimer;

    // ----- Teaching -----

    /// <summary>
    /// Shows the tour the first time the real window is used. It waits for the setup guide to be
    /// out of the way and for the window to actually be on screen, so nobody is toured while the
    /// app sits in the tray.
    /// </summary>
    private void ConsiderTour()
    {
        if (_tourConsidered || _fullscreen || !IsVisible || OnboardingVisible ||
            _host.State.Ui.TourSeenVersion >= Views.Tour.Version || ActualWidth <= 0)
        {
            return;
        }

        _tourConsidered = true;
        Dispatcher.BeginInvoke(StartTour, DispatcherPriority.Loaded);
    }

    /// <summary>Runs the tour from the beginning, whether or not it has been seen before.</summary>
    public void StartTour()
    {
        if (TourLayer.IsRunning || OnboardingVisible)
        {
            return;
        }

        // The tour talks about the side panel, so it opens it; someone who had it shut gets it
        // shut again afterwards rather than having the tour rearrange their window.
        var reopened = !_sidebarWanted;
        if (reopened)
        {
            SetSidebarVisible(true);
        }

        TourLayer.Start(
            Views.Tour.Steps(_host.Session.IsMirroring),
            name => FindName(name) as FrameworkElement,
            seen =>
            {
                if (reopened)
                {
                    SetSidebarVisible(false);
                }

                if (seen)
                {
                    _host.State.SetUi(_host.State.Ui with { TourSeenVersion = Views.Tour.Version });
                }
            });
    }

    /// <summary>
    /// Asks before something that cannot simply be undone, in the window and in the app's own
    /// voice. A system dialog would arrive light-themed over a dark window, and would answer with
    /// Yes and No rather than with the thing about to happen.
    /// </summary>
    public Task<bool> ConfirmAsync(string title, string body, string action, string? risk = null)
    {
        _confirm?.TrySetResult(false);
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _confirm = pending;

        ConfirmTitle.Text = title;
        ConfirmBody.Text = body;
        ConfirmAccept.Content = action;
        ConfirmRisk.Text = risk?.ToUpperInvariant() ?? string.Empty;
        ConfirmRisk.Visibility = string.IsNullOrWhiteSpace(risk) ? Visibility.Collapsed : Visibility.Visible;
        ConfirmSheet.Visibility = Visibility.Visible;
        AutomationProperties.SetName(ConfirmSheet, title + ". " + body);
        PreviewKeyDown -= OnConfirmKey;
        PreviewKeyDown += OnConfirmKey;
        Dispatcher.BeginInvoke(() => ConfirmCancel.Focus(), DispatcherPriority.Input);
        return pending.Task;
    }

    /// <summary>Escape means no, the way it does in every other dialog anyone has used.</summary>
    private void OnConfirmKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape && ConfirmSheet.Visibility == Visibility.Visible)
        {
            CloseConfirm(false);
            e.Handled = true;
        }
    }

    private void CloseConfirm(bool accepted)
    {
        PreviewKeyDown -= OnConfirmKey;
        ConfirmSheet.Visibility = Visibility.Collapsed;
        var pending = _confirm;
        _confirm = null;
        pending?.TrySetResult(accepted);
    }

    private void OnConfirmAccept(object sender, RoutedEventArgs e) => CloseConfirm(true);

    private void OnConfirmCancel(object sender, RoutedEventArgs e) => CloseConfirm(false);

    /// <summary>Offers a hint once, and never while something is being asked in the same bar.</summary>
    public void ShowTipOnce(string id)
    {
        if (_fullscreen || _tipShowing is not null || TourLayer.IsRunning ||
            _host.State.Ui.TipsSeen.Contains(id, StringComparer.Ordinal) ||
            _host.Session.PendingLockQuestionSerial is not null ||
            Tips.Find(id) is not { } tip)
        {
            return;
        }

        _tipShowing = id;
        NoticeTitle.Text = tip.Title;
        NoticeText.Text = tip.Text;
        ShowNoticeAnswers(question: false);
        NoticeBar.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Offers a hint once the moment has passed. A hint that arrives mid-gesture moves the window
    /// under the hand doing the gesture, which is exactly when nobody wants to read anything.
    /// </summary>
    public void ShowTipSoon(string id)
    {
        if (_tipTimer is not null || _host.State.Ui.TipsSeen.Contains(id, StringComparer.Ordinal))
        {
            return;
        }

        _tipTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _tipTimer.Tick += (_, _) =>
        {
            _tipTimer?.Stop();
            _tipTimer = null;
            ShowTipOnce(id);
        };
        _tipTimer.Start();
    }

    /// <summary>Marks a hint as shown without showing it, for the ones that teach somewhere else.</summary>
    public bool MarkTipSeen(string id)
    {
        if (_host.State.Ui.TipsSeen.Contains(id, StringComparer.Ordinal))
        {
            return false;
        }

        _host.State.SetUi(_host.State.Ui with { TipsSeen = [.. _host.State.Ui.TipsSeen, id] });
        return true;
    }

    /// <summary>Offers every hint again, from the Info panel.</summary>
    public void ForgetTips() => _host.State.SetUi(_host.State.Ui with { TipsSeen = [] });

    private void ShowNoticeAnswers(bool question)
    {
        foreach (var button in new[] { NoticePattern, NoticeOther, NoticeNone, NoticeLater })
        {
            button.Visibility = question ? Visibility.Visible : Visibility.Collapsed;
        }

        NoticeDismiss.Visibility = question ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnDismissTip(object sender, RoutedEventArgs e)
    {
        if (_tipShowing is { } id)
        {
            MarkTipSeen(id);
            _tipShowing = null;
        }

        NoticeBar.Visibility = Visibility.Collapsed;
    }

    /// <summary>Offers the phones ADB can see, once there is more than one to choose between.</summary>
    private void OnDeviceChip(object sender, RoutedEventArgs e)
    {
        var devices = _host.Session.Devices;
        if (devices.Count < 2)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = DeviceChip, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var active = _host.Session.ActiveDevice?.Serial;
        foreach (var device in devices)
        {
            var serial = device.Serial;
            var item = new MenuItem
            {
                Header = $"{_host.State.GetDevice(serial)?.Model ?? serial} · {device.Transport}",
                IsCheckable = true,
                IsChecked = serial == active,
            };
            item.Click += (_, _) => ChoosePhone(serial);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private void ChoosePhone(string serial)
    {
        if (serial == _host.Session.ActiveDevice?.Serial)
        {
            return;
        }

        _host.UpdateConfig(c => c.Session.PreferredSerial = serial);
        _host.Session.RestartMirror();
        SetStatus("Switching to " + serial + "…");
    }
}
