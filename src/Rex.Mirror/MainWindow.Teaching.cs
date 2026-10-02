using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
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
    private TaskCompletionSource<(string? Choice, bool ApplyToAll)>? _confirmChoice;
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
        _confirmChoice?.TrySetResult((null, false));
        _confirmChoice = null;
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _confirm = pending;

        ConfirmTitle.Text = title;
        ConfirmBody.Text = body;
        ConfirmAccept.Content = action;
        ConfirmAccept.Tag = null;
        ConfirmCancel.Content = "Cancel";
        ConfirmCancel.Tag = null;
        ConfirmMiddle.Visibility = Visibility.Collapsed;
        ConfirmApplyToAll.Visibility = Visibility.Collapsed;
        ConfirmRisk.Text = risk?.ToUpperInvariant() ?? string.Empty;
        ConfirmRisk.Visibility = string.IsNullOrWhiteSpace(risk) ? Visibility.Collapsed : Visibility.Visible;
        ShowConfirmSheet(true);
        AutomationProperties.SetName(ConfirmSheet, title + ". " + body);
        PreviewKeyDown -= OnConfirmKey;
        PreviewKeyDown += OnConfirmKey;
        Dispatcher.BeginInvoke(() => ConfirmCancel.Focus(), DispatcherPriority.Input);
        return pending.Task;
    }

    /// <summary>Asks a question with three named answers; Escape and the last button return null.</summary>
    public Task<(string? Choice, bool ApplyToAll)> ConfirmChoiceAsync(
        string title, string body, string first, string second, string third, string? applyToAllText = null)
    {
        _confirm?.TrySetResult(false);
        _confirm = null;
        _confirmChoice?.TrySetResult((null, false));
        var pending = new TaskCompletionSource<(string?, bool)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _confirmChoice = pending;
        ConfirmTitle.Text = title;
        ConfirmBody.Text = body;
        ConfirmAccept.Content = first;
        ConfirmAccept.Tag = first;
        ConfirmMiddle.Content = second;
        ConfirmMiddle.Tag = second;
        ConfirmMiddle.Visibility = Visibility.Visible;
        ConfirmCancel.Content = third;
        ConfirmCancel.Tag = third;
        ConfirmRisk.Visibility = Visibility.Collapsed;
        ConfirmApplyToAll.Content = applyToAllText ?? string.Empty;
        ConfirmApplyToAll.IsChecked = false;
        ConfirmApplyToAll.Visibility = applyToAllText is null ? Visibility.Collapsed : Visibility.Visible;
        ShowConfirmSheet(true);
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
            if (_confirmChoice is not null) CloseChoice(null);
            else CloseConfirm(false);
            e.Handled = true;
        }
    }

    /// <summary>
    /// The phone's picture is a window of its own, drawn over anything the app draws where it is,
    /// so while the sheet asks, the pictures and the overlay step aside instead of cutting through it.
    /// </summary>
    private void ShowConfirmSheet(bool shown)
    {
        ConfirmSheet.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        Host.SetShown(PictureShowable);
        UpdateCopiesShown();
        TrackOverlay();
    }

    /// <summary>Whether the phone's own picture may be on screen: mirroring, past the first-run guide, and nothing asking over it.</summary>
    internal bool PictureShowable =>
        _host.Session.IsMirroring && !OnboardingView.IsNeeded(_host) && ConfirmSheet.Visibility != Visibility.Visible;

    private void CloseConfirm(bool accepted)
    {
        PreviewKeyDown -= OnConfirmKey;
        ShowConfirmSheet(false);
        var pending = _confirm;
        _confirm = null;
        pending?.TrySetResult(accepted);
    }

    private void CloseChoice(string? choice)
    {
        PreviewKeyDown -= OnConfirmKey;
        ShowConfirmSheet(false);
        var pending = _confirmChoice;
        _confirmChoice = null;
        pending?.TrySetResult((choice, ConfirmApplyToAll.IsChecked == true));
        ConfirmCancel.Content = "Cancel";
        ConfirmCancel.Tag = null;
        ConfirmMiddle.Visibility = Visibility.Collapsed;
        ConfirmApplyToAll.Visibility = Visibility.Collapsed;
    }

    private void OnConfirmAccept(object sender, RoutedEventArgs e)
    {
        if (_confirmChoice is not null) CloseChoice(ConfirmAccept.Tag as string);
        else CloseConfirm(true);
    }

    private void OnConfirmMiddle(object sender, RoutedEventArgs e) => CloseChoice(ConfirmMiddle.Tag as string);

    private void OnConfirmCancel(object sender, RoutedEventArgs e)
    {
        if (_confirmChoice is not null) CloseChoice(ConfirmCancel.Tag as string);
        else CloseConfirm(false);
    }

    /// <summary>Offers a hint once, and never while something is being asked in the same bar.</summary>
    public void ShowTipOnce(string id)
    {
        if (_fullscreen || _tipShowing is not null || _usbNoticeShowing || TourLayer.IsRunning ||
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
        NoticeWhatsNew.Visibility = Visibility.Collapsed;
        _usbNoticeShowing = false;
        ShowUsbAnswers(UsbNotice.None);
    }

    private void OnDismissTip(object sender, RoutedEventArgs e)
    {
        if (_tipShowing == WhatsNewNotice)
        {
            RememberThisVersion();
            _tipShowing = null;
        }
        else if (_tipShowing is { } id)
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
        System.Windows.Automation.AutomationProperties.SetName(menu, "Choose which phone to mirror");
        var active = _host.Session.ActiveDevice?.Serial;
        foreach (var device in devices)
        {
            var serial = device.Serial;
            var (name, detail) = PhoneLabel(device, _host.State.GetDevice(serial), serial == active ? _host.Session.Identity : null);
            var header = new StackPanel();
            header.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold });
            header.Children.Add(new TextBlock { Text = detail, FontSize = 11, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 2, 0, 0) });
            var item = new MenuItem
            {
                Header = header,
                IsChecked = serial == active,
                IsEnabled = device.IsReady,
                Tag = serial,
            };
            System.Windows.Automation.AutomationProperties.SetName(item, $"{name}, {detail}");
            item.Click += (_, _) => ChoosePhone(serial);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    /// <summary>
    /// How a phone reads in the picker: its name, then how it is connected and whether it can be
    /// mirrored. A phone that is still asking for permission, or has dropped offline, is listed
    /// but cannot be picked, and says why.
    /// </summary>
    internal static (string Name, string Detail) PhoneLabel(AdbDevice device, DeviceProfile? profile, DeviceIdentity? identity)
    {
        // A phone that reports no marketing name gets its serial as a display name, which is the
        // least useful thing to show; the model saved for it reads better.
        var name = identity?.DisplayName is { Length: > 0 } shown && shown != device.Serial ? shown
            : profile?.Name is { Length: > 0 } saved && saved != device.Serial ? saved
            : profile?.Model is { Length: > 0 } model ? model
            : device.Model.Length > 0 ? device.Model
            : device.Serial;
        var state = device.State switch
        {
            "device" when identity is not null => "mirroring now",
            "device" => "ready",
            "unauthorized" => "tap Allow on the phone",
            "offline" => "offline",
            _ => device.State,
        };
        return (name, $"{(device.IsTcp ? "Wireless" : "USB")} · {state}");
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
