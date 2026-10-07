using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Rex.Core;
using Rex.Mirror.Mirror;
using Rex.Mirror.Services;
using Rex.Mirror.Views.Settings;

namespace Rex.Mirror.Views;

/// <summary>App settings. Every change saves immediately; launch-time changes offer a restart.</summary>
public partial class SettingsPanel : UserControl
{
    private MainWindow? _window;
    private AppHost? _host;
    private bool _loading;
    private ISettingsGroup[] _settingsGroups = [];

    internal bool Loading => _loading;

    public SettingsPanel()
    {
        InitializeComponent();
        _settingsGroups = [ProfilesSettingsGroup, SoundSettingsGroup, CopiesSettingsGroup, SecondScreenSettingsGroup, SecondPhoneSettingsGroup, AppsSettingsGroup, TransferSettingsGroup, HudSettingsGroup, WindowSettingsGroup, GlobalKeysSettingsGroup, KeysSettingsGroup, ControlsTabSettingsGroup, ConnectionSettingsGroup];
        PolishRows();
        StampConfigPaths();
    }

    /// <summary>
    /// Gives every row laid out in the markup the same treatment. A switch sits beside its label;
    /// anything wider (a list, a set of chips) goes under it at the full width, so the labels do not
    /// line up against a ragged column of controls. And every control is named after its label for
    /// assistive technology, which a row's header does not do by itself.
    /// </summary>
    private void PolishRows()
    {
        var inline = (Style)FindResource("SettingRow");
        var stacked = (Style)FindResource("SettingRowStacked");
        foreach (var row in Descendants(this).OfType<HeaderedContentControl>().Where(r => r.GetType() == typeof(HeaderedContentControl)).ToArray())
        {
            if (ReferenceEquals(row.Style, inline) && row.Content is not CheckBox)
            {
                row.Style = stacked;
            }

            if (row.Content is FrameworkElement control && string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(control)) &&
                LabelOf(row) is { Length: > 0 } label)
            {
                System.Windows.Automation.AutomationProperties.SetName(control, label);
            }
        }
    }

    private void StampConfigPaths()
    {
        var named = Descendants(this).Prepend(this).OfType<FrameworkElement>()
            .Where(element => element.Name.Length > 0)
            .ToDictionary(element => element.Name, StringComparer.Ordinal);
        foreach (var (path, controlName) in SettingsCatalogue.Controls)
        {
            if (named.TryGetValue(controlName, out var control))
            {
                SettingRows.SetConfigPath(control, path);
                DependencyObject? up = control;
                while (up is not null && up is not HeaderedContentControl)
                {
                    up = LogicalTreeHelper.GetParent(up);
                }

                if (up is HeaderedContentControl row && LabelOf(row) is { Length: > 0 } label)
                {
                    _labels[path] = label;
                }
            }
        }
    }

    private readonly Dictionary<string, string> _labels = new(StringComparer.Ordinal);

    /// <summary>A setting's words as its row shows them, or its config path when it has no row.</summary>
    internal string LabelFor(string path) => _labels.TryGetValue(path, out var label) ? label : path;

    /// <summary>Settings whose parent is off stay in sight but cannot be changed (<see cref="SettingsDependencies"/>).</summary>
    private void ApplyDependencies(RexConfig c)
    {
        var on = SettingsDependencies.Of(c);
        Sensitivity.IsEnabled = on.Sensitivity;
        foreach (var control in new UIElement[] { ZoomAnchor, InvertWheel, ResetOnRotate, Navigator, MaximumZoom, WheelSpeed })
        {
            control.IsEnabled = on.Zoom;
        }

        foreach (var control in new UIElement[] { NavigatorPicture, NavigatorAlways, NavigatorCorner, NavigatorWidth, NavigatorOpacity, NavigatorFrameRate })
        {
            control.IsEnabled = on.Navigator;
        }

        PatternAuto.IsEnabled = on.PatternOptions;
        PatternDiscover.IsEnabled = on.PatternOptions;
        WirelessTcpip.IsEnabled = on.WirelessTcpip;
        AudioSource.IsEnabled = on.Audio;
        AudioBitRate.IsEnabled = on.Audio;
        AudioDup.IsEnabled = on.AudioDup;
        AmbientOptions.IsEnabled = on.Ambient;
    }

    /// <summary>A row's label: its header text, or the first line of a header that also has a description.</summary>
    internal static string? LabelOf(HeaderedContentControl row) => row.Header switch
    {
        string text => text,
        Panel { Children.Count: > 0 } panel when panel.Children[0] is TextBlock first => first.Text,
        _ => null,
    };

    public void Attach(MainWindow window, AppHost host)
    {
        _window = window;
        _host = host;
        foreach (var group in _settingsGroups)
        {
            group.Attach(this, window, host);
        }

        IndexSettings();
        _loading = true;
        try
        {
            var open = host.State.Ui.SettingsOpen;
            foreach (var group in Groups())
            {
                group.IsExpanded = open.Contains(group.Name, StringComparer.Ordinal);
                group.Expanded += OnGroupExpansionChanged;
                group.Collapsed += OnGroupExpansionChanged;
            }
        }
        finally
        {
            _loading = false;
        }
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
            SelectTag(VideoBuffer, c.Mirror.VideoBufferMs.ToString(CultureInfo.InvariantCulture));
            DownsizeOnError.IsChecked = c.Mirror.DownsizeOnError;
            SelectTag(RenderDriver, c.Mirror.RenderDriver);
            Audio.IsChecked = c.Mirror.Audio;
            AudioDup.IsChecked = c.Mirror.AudioDup;
            SelectTag(AudioSource, c.Mirror.AudioSource);
            SelectTag(AudioBitRate, c.Mirror.AudioBitRate);
            AudioSource.IsEnabled = c.Mirror.Audio;
            AudioBitRate.IsEnabled = c.Mirror.Audio;
            AudioDupHint.Visibility = c.Mirror.Audio && !c.Mirror.AudioDupPossible ? Visibility.Visible : Visibility.Collapsed;
            SelectTag(RecordFormat, c.Mirror.RecordFormat);
            SelectTag(ScreenOffTimeout, c.Session.ScreenOffTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
            KeepPcAwake.IsChecked = c.Session.KeepPcAwake;
            RefreshInput(c);
            RefreshWindow(c);
            RefreshRemaining(c);
            RefreshScrcpy(c);
            Record.IsChecked = c.Mirror.RecordOnStart;
            TurnScreenOff.IsChecked = c.Session.TurnScreenOff;
            StayAwake.IsChecked = c.Session.StayAwake;
            PowerOffOnClose.IsChecked = c.Session.PowerOffOnClose;
            RestartOnCrash.IsChecked = c.Session.RestartOnUnexpectedExit;
            TwoFinger.IsChecked = c.Touchpad.TwoFingerToAndroid;
            Sensitivity.Value = c.Touchpad.Sensitivity;
            SensitivityValue.Text = c.Touchpad.Sensitivity.ToString("0.0", CultureInfo.InvariantCulture) + "×";
            HostZoom.IsChecked = c.Zoom.Enabled;
            Navigator.IsChecked = c.Zoom.ShowNavigator;
            NavigatorPicture.IsChecked = c.Zoom.NavigatorPicture;
            NavigatorAlways.IsChecked = c.Zoom.NavigatorAlways;
            AmbientEnabled.IsChecked = c.Ambient.Enabled;
            AmbientOptions.IsEnabled = c.Ambient.Enabled;
            SelectTag(AmbientPlacement, c.Ambient.Placement);
            SelectTag(AmbientScaling, c.Ambient.Scaling);
            AmbientOpacity.Value = c.Ambient.Opacity;
            AmbientBlur.Value = c.Ambient.Blur;
            AmbientSize.Value = c.Ambient.Size;
            AmbientOffsetX.Value = c.Ambient.OffsetX;
            AmbientOffsetY.Value = c.Ambient.OffsetY;
            AmbientEdgeFade.Value = c.Ambient.EdgeFade;
            AmbientTintStrength.Value = c.Ambient.TintStrength;
            AmbientTintHue.Value = c.Ambient.TintHue;
            SelectTag(AmbientWhenZoomed, c.Ambient.WhenZoomed);
            AmbientFlip.IsChecked = c.Ambient.FlipHorizontal;
            AmbientFrameRate.Value = c.Ambient.FrameRate;
            SelectTag(NavigatorCorner, c.Zoom.NavigatorCorner);
            NavigatorWidth.Value = c.Zoom.NavigatorWidth;
            NavigatorOpacity.Value = c.Zoom.NavigatorOpacity;
            NavigatorFrameRate.Value = c.Zoom.NavigatorFrameRate;
            ShowAmbientValues(c);
            MaximumZoom.Value = c.Zoom.MaxZoom;
            WheelSpeed.Value = c.Zoom.WheelStep;
            foreach (var group in _settingsGroups)
            {
                group.Refresh(c);
            }
            // scrcpy only keeps the playback going on the phone, so another source rules it out.
            // What the audio rows can do follows from the others; see ApplyDependencies.
            PatternEnabled.IsChecked = c.PatternGuide.Enabled;
            PatternAuto.IsChecked = c.PatternGuide.AutoShowOnKeyguard;
            PatternDiscover.IsChecked = c.PatternGuide.AutoDiscoverGeometry;
            StartWithWindows.IsChecked = StartupRegistration.IsEnabled();
            RunInBackground.IsChecked = c.App.RunInBackground;
            OpenOnConnect.IsChecked = c.App.OpenOnConnect;
            ConfirmWrites.IsChecked = c.App.ConfirmSensitiveWrites;
            Wireless.IsChecked = c.Wireless.Enabled;
            AutoRepairUsb.IsChecked = c.App.AutoRepairUsb;
            AutoRepairUsbState.Text = UsbSettingText(c.App.AutoRepairUsb, _host.Usb.AutoRepair);
            WirelessTcpip.IsChecked = c.Wireless.EnableTcpipWhenUsbAvailable;
            ExtraArgs.Text = c.Mirror.ExtraArgs;
            CompatibilityKeyboard.IsChecked = c.Mirror.CompatibilityKeyboard;
            ScreenshotLocation.Text = _host.Paths.ScreenshotFolder(c.App.ScreenshotDirectory);
            ApplyDependencies(c);
            _window?.RefreshRestartNotice();
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>What the USB auto-repair switch does right now, under its label.</summary>
    internal static string UsbSettingText(bool enabled, UsbAutoRepairStatus task) => (enabled, task.State) switch
    {
        (false, _) => "Off: the app offers the fix and asks first.",
        (true, UsbAutoRepairState.Installed) => "On. Windows resets a phone it could not read without asking.",
        (true, UsbAutoRepairState.Outdated) => "Set up by another version. Choose Fix automatically when the notice offers it to update it.",
        _ => "Needs one administrator approval, offered the first time Windows can't read the phone.",
    };

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

    internal void Save(Action<RexConfig> mutate)
    {
        if (_loading || _host is null)
        {
            return;
        }

        _host.UpdateConfig(mutate);
        Refresh();
    }

    private void OnMirrorChanged(object sender, RoutedEventArgs e) => Save(c =>
    {
        c.Mirror.MaxSize = int.Parse(SelectedTag(MaxSize, "1920"), CultureInfo.InvariantCulture);
        c.Mirror.MaxFps = int.Parse(SelectedTag(MaxFps, "60"), CultureInfo.InvariantCulture);
        c.Mirror.VideoBitRate = SelectedTag(BitRate, "12M");
        var codec = SelectedTag(VideoCodec, "h264");
        if (codec != c.Mirror.VideoCodec)
        {
            // An encoder makes one codec: keep it only where the phone is known to make the new one with it.
            var known = _host!.Session.ActiveDevice is { } device ? _host.Session.EncodersOf(device.Serial) : null;
            c.Mirror.VideoEncoder = EncoderList.KeepFor(c.Mirror.VideoEncoder, codec, known);
        }

        c.Mirror.VideoCodec = codec;
        c.Mirror.Audio = Audio.IsChecked == true;
        c.Mirror.AudioDup = AudioDup.IsChecked == true;
        c.Mirror.AudioSource = SelectedTag(AudioSource, "auto");
        c.Mirror.AudioBitRate = SelectedTag(AudioBitRate, MirrorSettings.DefaultAudioBitRate);
        c.Mirror.VideoBufferMs = int.Parse(SelectedTag(VideoBuffer, "0"), CultureInfo.InvariantCulture);
        c.Mirror.DownsizeOnError = DownsizeOnError.IsChecked == true;
        c.Mirror.RenderDriver = SelectedTag(RenderDriver, string.Empty);
        c.Mirror.RecordFormat = SelectedTag(RecordFormat, "mp4");
        c.Session.ScreenOffTimeoutSeconds = int.Parse(SelectedTag(ScreenOffTimeout, "0"), CultureInfo.InvariantCulture);
        c.Session.KeepPcAwake = KeepPcAwake.IsChecked == true;
        c.Mirror.RecordOnStart = Record.IsChecked == true;
        c.Mirror.CompatibilityKeyboard = CompatibilityKeyboard.IsChecked == true;
        c.Session.TurnScreenOff = TurnScreenOff.IsChecked == true;
        c.Session.StayAwake = StayAwake.IsChecked == true;
        c.Session.PowerOffOnClose = PowerOffOnClose.IsChecked == true;
    });

    private void OnSessionChanged(object sender, RoutedEventArgs e) => Save(c =>
    {
        c.Session.RestartOnUnexpectedExit = RestartOnCrash.IsChecked == true;
        c.Wireless.Enabled = Wireless.IsChecked == true;
        c.Wireless.EnableTcpipWhenUsbAvailable = WirelessTcpip.IsChecked == true;
        c.App.AutoRepairUsb = AutoRepairUsb.IsChecked == true;
    });

    private void OnLiveChanged(object sender, RoutedEventArgs e) => Save(c =>
    {
        c.Touchpad.TwoFingerToAndroid = TwoFinger.IsChecked == true;
        c.Zoom.Enabled = HostZoom.IsChecked == true;
        c.Zoom.ShowNavigator = Navigator.IsChecked == true;
        c.Zoom.NavigatorPicture = NavigatorPicture.IsChecked == true;
        c.Zoom.NavigatorAlways = NavigatorAlways.IsChecked == true;
        c.Ambient.Enabled = AmbientEnabled.IsChecked == true;
        c.Ambient.Placement = SelectedTag(AmbientPlacement, "around");
        c.Ambient.Scaling = SelectedTag(AmbientScaling, "cover");
        c.Ambient.FlipHorizontal = AmbientFlip.IsChecked == true;
        c.Ambient.WhenZoomed = SelectedTag(AmbientWhenZoomed, "show");
        c.Zoom.NavigatorCorner = SelectedTag(NavigatorCorner, "bottom-right");
        c.PatternGuide.Enabled = PatternEnabled.IsChecked == true;
        c.PatternGuide.AutoShowOnKeyguard = PatternAuto.IsChecked == true;
        c.PatternGuide.AutoDiscoverGeometry = PatternDiscover.IsChecked == true;
        c.App.RunInBackground = RunInBackground.IsChecked == true;
        c.App.OpenOnConnect = OpenOnConnect.IsChecked == true;
        c.App.ConfirmSensitiveWrites = ConfirmWrites.IsChecked == true;
    });

    private void OnSensitivity(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_host is null) return;
        SensitivityValue.Text = Sensitivity.Value.ToString("0.0", CultureInfo.InvariantCulture) + "×";
        if (!_loading && _host is not null)
        {
            var value = Math.Round(Sensitivity.Value, 2);
            _host.UpdateConfig(c => c.Touchpad.Sensitivity = value);
        }
    }

    private void OnStartWithWindows(object sender, RoutedEventArgs e) => _window?.SetStartWithWindows(StartWithWindows.IsChecked == true);

    private void OnForgetLocks(object sender, RoutedEventArgs e)
    {
        var count = _host?.State.ResetLockScreen("ALL") ?? 0;
        _window?.SetStatus(count == 0 ? "No lock answers were saved." : $"Forgot the lock of {count} phone(s). Each is asked again on its next connection.");
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
        if (_host is null) return;
        if (ExtraArgs.Text.Trim() == _host.Config.Mirror.ExtraArgs)
        {
            ExtraArgsError.Text = string.Empty;
            return;
        }

        try
        {
            ScrcpyArguments.SplitExtraArgs(ExtraArgs.Text);
            ExtraArgsError.Text = string.Empty;
            var text = ExtraArgs.Text.Trim();
            Save(c => c.Mirror.ExtraArgs = text);
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
                _window?.SetStatus("There is no earlier change to undo.", isError: true);
                return;
            }

            _host.ReloadConfigFromDisk();
            Refresh();
            _window?.SetStatus("The last change is undone.");
        }
        catch (InvalidOperationException ex)
        {
            _host.Log.Error("Could not restore the previous configuration backup", ex);
            _window?.SetStatus(ex.Message, isError: true);
        }
    }

    /// <summary>Sliders preview live and are written a moment after the drag ends.</summary>
    private void OnAppearanceChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Sliders raise ValueChanged while the XAML is still loading (Minimum coerces Value); the
        // labels do not exist yet and Refresh() writes them once the host is attached.
        if (_host is null) return;
        NavigatorWidthValue.Text = $"{NavigatorWidth.Value:0} px";
        NavigatorOpacityValue.Text = $"{NavigatorOpacity.Value * 100:0}%";
        NavigatorFrameRateValue.Text = $"{NavigatorFrameRate.Value:0} fps";
        MaximumZoomValue.Text = MaximumZoom.Value.ToString("0.0", CultureInfo.InvariantCulture) + "×";
        WheelSpeedValue.Text = $"{WheelSpeed.Value * 100:0}% per notch";
        if (_loading) return;
        var maxZoom = MaximumZoom.Value;
        var wheelStep = Math.Round(WheelSpeed.Value, 2);
        var navigatorWidth = Math.Round(NavigatorWidth.Value);
        var navigatorOpacity = Math.Round(NavigatorOpacity.Value, 2);
        var navigatorFrameRate = Math.Round(NavigatorFrameRate.Value);
        _host.PreviewConfig(c =>
        {
            c.Zoom.MaxZoom = maxZoom;
            c.Zoom.WheelStep = wheelStep;
            c.Zoom.NavigatorWidth = navigatorWidth;
            c.Zoom.NavigatorOpacity = navigatorOpacity;
            c.Zoom.NavigatorFrameRate = navigatorFrameRate;
        });
    }

    private void OnAmbientChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_host is null) return;
        var ambient = new AmbientSettings
        {
            Opacity = Math.Round(AmbientOpacity.Value, 2),
            Blur = Math.Round(AmbientBlur.Value),
            Size = Math.Round(AmbientSize.Value, 2),
            OffsetX = Math.Round(AmbientOffsetX.Value, 2),
            OffsetY = Math.Round(AmbientOffsetY.Value, 2),
            EdgeFade = Math.Round(AmbientEdgeFade.Value, 2),
            TintStrength = Math.Round(AmbientTintStrength.Value, 2),
            TintHue = Math.Round(AmbientTintHue.Value),
            FrameRate = Math.Round(AmbientFrameRate.Value),
        };
        ShowAmbientValues(new RexConfig { Ambient = ambient, App = _host.Config.App });
        if (_loading) return;
        _host.PreviewConfig(c =>
        {
            c.Ambient.Opacity = ambient.Opacity;
            c.Ambient.Blur = ambient.Blur;
            c.Ambient.Size = ambient.Size;
            c.Ambient.OffsetX = ambient.OffsetX;
            c.Ambient.OffsetY = ambient.OffsetY;
            c.Ambient.EdgeFade = ambient.EdgeFade;
            c.Ambient.TintStrength = ambient.TintStrength;
            c.Ambient.TintHue = ambient.TintHue;
            c.Ambient.FrameRate = ambient.FrameRate;
        });
    }

    private void ShowAmbientValues(RexConfig c)
    {
        AmbientOpacityValue.Text = $"{c.Ambient.Opacity * 100:0}%";
        AmbientBlurValue.Text = c.Ambient.Blur < 0.5 ? "sharp" : $"{c.Ambient.Blur:0} px";
        AmbientSizeValue.Text = $"{c.Ambient.Size * 100:0}%";
        AmbientOffsetXValue.Text = Offset(c.Ambient.OffsetX, "left", "right");
        AmbientOffsetYValue.Text = Offset(c.Ambient.OffsetY, "up", "down");
        AmbientEdgeFadeValue.Text = c.Ambient.EdgeFade < 0.005 ? "off" : $"{c.Ambient.EdgeFade * 100:0}%";
        AmbientTintValue.Text = c.Ambient.TintStrength < 0.005 ? "off" : $"{c.Ambient.TintStrength * 100:0}%";
        var (red, green, blue) = AmbientLayout.HueToRgb(c.Ambient.TintHue);
        AmbientTintHueSwatch.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(red, green, blue));
        System.Windows.Automation.AutomationProperties.SetHelpText(AmbientTintHueSwatch, $"Hue {c.Ambient.TintHue:0} degrees");
        AmbientTintHue.IsEnabled = c.Ambient.TintStrength >= 0.005;
        AmbientFrameRateValue.Text = $"{c.Ambient.FrameRate:0} fps";

        static string Offset(double value, string negative, string positive) =>
            Math.Abs(value) < 0.005 ? "centred" : $"{Math.Abs(value) * 100:0}% {(value < 0 ? negative : positive)}";
    }

    private void OnResetAmbient(object sender, RoutedEventArgs e) => Save(c => c.Ambient = new AmbientSettings());

    private async void OnResetEverything(object sender, RoutedEventArgs e)
    {
        if (_window is null || _host is null)
        {
            return;
        }

        var confirmed = await _window.ConfirmAsync(
            "Reset every app setting?",
            "Everything in this panel goes back to how it ships, including the soft background and the fullscreen controls. Your phone is not touched, and Undo last change puts this back.",
            "Reset everything");
        if (!confirmed)
        {
            return;
        }

        Save(c => c.Reset());

        _window?.SetStatus("Every app setting is back to how it ships.");
    }

    private void OnChooseScreenshotFolder(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;
        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose where screenshots are saved",
            InitialDirectory = PickerFolder.Ready(_host.Paths.ScreenshotFolder(_host.Config.App.ScreenshotDirectory), _host.Paths.Root),
        };
        if (picker.ShowDialog(_window!) == true)
            Save(c => c.App.ScreenshotDirectory = picker.FolderName);
    }

    private void OnDefaultScreenshotFolder(object sender, RoutedEventArgs e) =>
        Save(c => c.App.ScreenshotDirectory = new AppSettings().ScreenshotDirectory);
}
