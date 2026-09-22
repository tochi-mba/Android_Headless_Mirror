using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror.Views;

/// <summary>App settings. Every change saves immediately; launch-time changes offer a restart.</summary>
public partial class SettingsPanel : UserControl
{
    private MainWindow? _window;
    private AppHost? _host;
    private bool _loading;
    private bool _launchSettingsDirty;

    public SettingsPanel()
    {
        InitializeComponent();
    }

    public void Attach(MainWindow window, AppHost host)
    {
        _window = window;
        _host = host;
        Refresh();
    }

    public void Refresh()
    {
        if (_host is null)
        {
            return;
        }

        _loading = true;
        try
        {
            var c = _host.Config;
            SelectTag(MaxSize, c.Mirror.MaxSize.ToString(CultureInfo.InvariantCulture));
            SelectTag(MaxFps, c.Mirror.MaxFps.ToString(CultureInfo.InvariantCulture));
            SelectTag(BitRate, c.Mirror.VideoBitRate.ToUpperInvariant());
            SelectTag(VideoCodec, c.Mirror.VideoCodec);
            Audio.IsChecked = c.Mirror.Audio;
            AudioDup.IsChecked = c.Mirror.AudioDup;
            Record.IsChecked = c.Mirror.RecordOnStart;
            TurnScreenOff.IsChecked = c.Session.TurnScreenOff;
            StayAwake.IsChecked = c.Session.StayAwake;
            PowerOffOnClose.IsChecked = c.Session.PowerOffOnClose;
            RestartOnCrash.IsChecked = c.Session.RestartOnUnexpectedExit;
            TwoFinger.IsChecked = c.Touchpad.Enabled && c.Touchpad.TwoFingerToAndroid;
            Sensitivity.Value = c.Touchpad.Sensitivity;
            SensitivityValue.Text = c.Touchpad.Sensitivity.ToString("0.0", CultureInfo.InvariantCulture) + "×";
            HostZoom.IsChecked = c.Zoom.Enabled;
            Navigator.IsChecked = c.Zoom.ShowNavigator;
            PatternEnabled.IsChecked = c.PatternGuide.Enabled;
            PatternAuto.IsChecked = c.PatternGuide.AutoShowOnKeyguard;
            PatternDiscover.IsChecked = c.PatternGuide.AutoDiscoverGeometry;
            StartWithWindows.IsChecked = StartupRegistration.IsEnabled();
            RunInBackground.IsChecked = c.App.RunInBackground;
            OpenOnConnect.IsChecked = c.App.OpenOnConnect;
            ConfirmWrites.IsChecked = c.App.ConfirmSensitiveWrites;
            Wireless.IsChecked = c.Wireless.Enabled;
            WirelessTcpip.IsChecked = c.Wireless.EnableTcpipWhenUsbAvailable;
            ExtraArgs.Text = c.Mirror.ExtraArgs;
            RestartBar.Visibility = _launchSettingsDirty && _host.Session.IsMirroring ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _loading = false;
        }
    }

    private static void SelectTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals((string)item.Tag, tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        combo.SelectedIndex = -1;
    }

    private static string SelectedTag(ComboBox combo, string fallback) => (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? fallback;

    private void Save(Action<RexConfig> mutate, bool launchTime)
    {
        if (_loading || _host is null)
        {
            return;
        }

        _host.UpdateConfig(mutate);
        if (launchTime)
        {
            _launchSettingsDirty = true;
        }

        Refresh();
    }

    private void OnMirrorChanged(object sender, RoutedEventArgs e) => Save(c =>
    {
        c.Mirror.MaxSize = int.Parse(SelectedTag(MaxSize, "1920"), CultureInfo.InvariantCulture);
        c.Mirror.MaxFps = int.Parse(SelectedTag(MaxFps, "60"), CultureInfo.InvariantCulture);
        c.Mirror.VideoBitRate = SelectedTag(BitRate, "12M");
        c.Mirror.VideoCodec = SelectedTag(VideoCodec, "h264");
        c.Mirror.Audio = Audio.IsChecked == true;
        c.Mirror.AudioDup = AudioDup.IsChecked == true;
        c.Mirror.RecordOnStart = Record.IsChecked == true;
        c.Session.TurnScreenOff = TurnScreenOff.IsChecked == true;
        c.Session.StayAwake = StayAwake.IsChecked == true;
        c.Session.PowerOffOnClose = PowerOffOnClose.IsChecked == true;
    }, launchTime: true);

    private void OnSessionChanged(object sender, RoutedEventArgs e) => Save(c =>
    {
        c.Session.RestartOnUnexpectedExit = RestartOnCrash.IsChecked == true;
        c.Wireless.Enabled = Wireless.IsChecked == true;
        c.Wireless.EnableTcpipWhenUsbAvailable = WirelessTcpip.IsChecked == true;
    }, launchTime: false);

    private void OnLiveChanged(object sender, RoutedEventArgs e) => Save(c =>
    {
        c.Touchpad.Enabled = true;
        c.Touchpad.TwoFingerToAndroid = TwoFinger.IsChecked == true;
        c.Zoom.Enabled = HostZoom.IsChecked == true;
        c.Zoom.WheelZoom = c.Zoom.Enabled;
        c.Zoom.PinchZoom = c.Zoom.Enabled;
        c.Zoom.ShowNavigator = Navigator.IsChecked == true;
        c.PatternGuide.Enabled = PatternEnabled.IsChecked == true;
        c.PatternGuide.AutoShowOnKeyguard = PatternAuto.IsChecked == true;
        c.PatternGuide.AutoDiscoverGeometry = PatternDiscover.IsChecked == true;
        c.App.RunInBackground = RunInBackground.IsChecked == true;
        c.App.OpenOnConnect = OpenOnConnect.IsChecked == true;
        c.App.ConfirmSensitiveWrites = ConfirmWrites.IsChecked == true;
    }, launchTime: false);

    private void OnSensitivity(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        SensitivityValue.Text = Sensitivity.Value.ToString("0.0", CultureInfo.InvariantCulture) + "×";
        if (!_loading && _host is not null)
        {
            var value = Math.Round(Sensitivity.Value, 2);
            _host.UpdateConfig(c => c.Touchpad.Sensitivity = value);
        }
    }

    private void OnStartWithWindows(object sender, RoutedEventArgs e) => _window?.SetStartWithWindows(StartWithWindows.IsChecked == true);

    private void OnRestartNow(object sender, RoutedEventArgs e)
    {
        _launchSettingsDirty = false;
        _host?.Session.RestartMirror();
        Refresh();
    }

    private void OnForgetLocks(object sender, RoutedEventArgs e)
    {
        var count = _host?.State.ResetLockScreen("ALL") ?? 0;
        Status.Text = count == 0 ? "No saved answers." : $"Forgot {count} phone(s). You'll be asked again on the next connection.";
    }

    private void OnDesktopShortcut(object sender, RoutedEventArgs e)
    {
        if (_host is null)
        {
            return;
        }

        try
        {
            var path = DesktopShortcut.Create(_host.ExecutablePath, _host.Paths.Root);
            Status.Text = "Shortcut created: " + path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            Status.Text = "Could not create the shortcut: " + ex.Message;
        }
    }

    private void OnExtraArgs(object sender, RoutedEventArgs e) => CommitExtraArgs();

    private void OnExtraArgsKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitExtraArgs();
        }
    }

    private void CommitExtraArgs()
    {
        if (_host is null || ExtraArgs.Text.Trim() == _host.Config.Mirror.ExtraArgs)
        {
            return;
        }

        try
        {
            ScrcpyArguments.SplitExtraArgs(ExtraArgs.Text);
            ExtraArgsError.Text = string.Empty;
            var text = ExtraArgs.Text.Trim();
            Save(c => c.Mirror.ExtraArgs = text, launchTime: true);
        }
        catch (FormatException ex)
        {
            ExtraArgsError.Text = ex.Message;
        }
    }

    private void OnOpenConfig(object sender, RoutedEventArgs e)
    {
        if (_host is not null)
        {
            Process.Start(new ProcessStartInfo(_host.Paths.Config) { UseShellExecute = true });
        }
    }

    private void OnRestoreConfig(object sender, RoutedEventArgs e)
    {
        if (_host is null)
        {
            return;
        }

        try
        {
            if (!ConfigFile.RestoreBackup(_host.Paths.Config))
            {
                Status.Text = "There is no previous configuration to restore.";
                return;
            }

            _host.ReloadConfigFromDisk();
            _launchSettingsDirty = true;
            Refresh();
            Status.Text = "Previous configuration restored.";
        }
        catch (InvalidOperationException ex)
        {
            Status.Text = ex.Message;
        }
    }
}
