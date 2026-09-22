using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror.Views;

/// <summary>First run: install scrcpy with a verified download, then explain the two phone-side steps.</summary>
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
            InstallStatus.Text = "Installed. Connect your phone.";
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
        }
    }

    private void OnStartWithWindows(object sender, RoutedEventArgs e) =>
        _window?.SetStartWithWindows(StartWithWindows.IsChecked == true);
}
