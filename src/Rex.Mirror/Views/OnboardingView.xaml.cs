using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Rex.Core;
using Rex.Mirror.Services;
using Rex.Mirror.Session;

namespace Rex.Mirror.Views;

/// <summary>
/// The guided first run. Every step reflects live state: the engine step completes when scrcpy
/// is found (bundled by the installer, or downloaded here), and the phone step follows ADB until
/// the first phone is authorised, at which point the mirror opens and this view goes away.
/// </summary>
public partial class OnboardingView : UserControl
{
    private MainWindow? _window;
    private AppHost? _host;
    private bool _installing;

    public OnboardingView()
    {
        InitializeComponent();
    }

    public void Attach(MainWindow window, AppHost host)
    {
        _window = window;
        _host = host;
        StartWithWindows.IsChecked = StartupRegistration.IsEnabled();
    }

    /// <summary>True while the first run has not finished: no scrcpy yet, or no phone ever mirrored.</summary>
    public static bool IsNeeded(AppHost host) =>
        host.Session.Phase == SessionPhase.NeedsSetup ||
        (!host.Session.IsMirroring && host.State.Devices.Count == 0 && !host.State.Ui.SetupDismissed);

    public void Refresh()
    {
        if (_host is null)
        {
            return;
        }

        var session = _host.Session;
        var toolsReady = session.Tools is not null;
        MarkStep(Badge1, Badge1Text, "1", toolsReady);
        InstallRow.Visibility = toolsReady ? Visibility.Collapsed : Visibility.Visible;
        ToolsText.Text = toolsReady
            ? $"scrcpy {session.Tools!.Version} is ready."
            : "scrcpy, the open-source engine from Genymobile, is downloaded from GitHub and its SHA-256 checksum is verified before anything is installed.";

        var unauthorized = session.Devices.Any(d => d.IsUnauthorized);
        var ready = session.Devices.Any(d => d.IsReady);
        MarkStep(Badge2, Badge2Text, "2", ready);
        MarkStep(Badge3, Badge3Text, "3", ready);
        SkipButton.Visibility = toolsReady ? Visibility.Visible : Visibility.Collapsed;

        (PhoneStatus.Text, var dot) = !toolsReady
            ? ("Finish step 1 first.", "Muted")
            : ready ? ("Phone found. Opening the mirror…", "Signal")
            : unauthorized ? ("Phone found. Tap Allow on the phone and tick “Always allow”.", "Live")
            : session.Devices.Count > 0 ? (DeviceStateText.Describe(session.Devices), "Live")
            : ("Waiting for a phone…", "Muted");
        PhoneDot.Fill = (Brush)FindResource(dot);
    }

    private void MarkStep(Border badge, TextBlock text, string number, bool done)
    {
        text.Text = done ? "✓" : number;
        text.Foreground = (Brush)FindResource(done ? "Ink" : "Text");
        badge.Background = (Brush)FindResource(done ? "Signal" : "Raised");
    }

    private async void OnInstall(object sender, RoutedEventArgs e)
    {
        if (_host is null || _installing)
        {
            return;
        }

        _installing = true;
        InstallButton.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        var progress = new Progress<InstallProgress>(p =>
        {
            Progress.Value = p.Fraction;
            InstallStatus.Text = p.Stage;
        });

        try
        {
            await _host.Session.InstallToolsAsync(progress, CancellationToken.None);
            InstallStatus.Text = string.Empty;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException or UnauthorizedAccessException)
        {
            _host.Log.Error("scrcpy install failed", ex);
            InstallStatus.Text = "Install failed: " + ex.Message;
            InstallButton.Content = "Try again";
            InstallButton.IsEnabled = true;
            Progress.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _installing = false;
            Refresh();
        }
    }

    private void OnStartWithWindows(object sender, RoutedEventArgs e) =>
        _window?.SetStartWithWindows(StartWithWindows.IsChecked == true);

    private void OnSkip(object sender, RoutedEventArgs e)
    {
        if (_host is null)
        {
            return;
        }

        _host.State.SetUi(_host.State.Ui with { SetupDismissed = true });
        _window?.RefreshAll();
    }
}
