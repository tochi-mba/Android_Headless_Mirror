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

namespace Rex.Mirror.Views;

/// <summary>App settings. Every change saves immediately; launch-time changes offer a restart.</summary>
public partial class SettingsPanel : UserControl
{
    private MainWindow? _window;
    private AppHost? _host;
    private bool _loading;

    public SettingsPanel()
    {
        InitializeComponent();
        PolishRows();
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
        HudOptions.IsEnabled = on.Hud;
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
            AmbientFlip.IsChecked = c.Ambient.FlipHorizontal;
            AmbientFrameRate.Value = c.Ambient.FrameRate;
            SelectTag(NavigatorCorner, c.Zoom.NavigatorCorner);
            NavigatorWidth.Value = c.Zoom.NavigatorWidth;
            NavigatorOpacity.Value = c.Zoom.NavigatorOpacity;
            NavigatorFrameRate.Value = c.Zoom.NavigatorFrameRate;
            HudEnabled.IsChecked = c.Hud.Enabled;
            HudOptions.IsEnabled = c.Hud.Enabled;
            HudMessages.IsChecked = c.Hud.ShowMessages;
            SelectTag(HudPosition, c.Hud.Position);
            HudDraggedRow.Visibility = c.Hud.IsPlaced ? Visibility.Visible : Visibility.Collapsed;
            HudScale.Value = c.Hud.Scale;
            HudOpacity.Value = c.Hud.Opacity;
            HudDelay.Value = c.Hud.HideSeconds;
            BuildHudButtons(c.Hud);
            ShowAmbientValues(c);
            MaximumZoom.Value = c.Zoom.MaxZoom;
            WheelSpeed.Value = c.Zoom.WheelStep;
            CopiesMost.Maximum = CopiesSettings.MostUpperBound;
            CopiesGap.Maximum = CopiesSettings.GapUpperBound;
            CopiesMost.Value = c.Copies.Most;
            CopiesGap.Value = c.Copies.Gap;
            SelectTag(CopiesMaxSize, c.Copies.MaxSize.ToString(CultureInfo.InvariantCulture));
            CopiesRemember.IsChecked = c.Copies.Remember;
            ShowCopiesValues();
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

    private void Save(Action<RexConfig> mutate)
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
        c.Mirror.VideoCodec = SelectedTag(VideoCodec, "h264");
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
        c.Touchpad.Enabled = true;
        c.Touchpad.TwoFingerToAndroid = TwoFinger.IsChecked == true;
        c.Zoom.Enabled = HostZoom.IsChecked == true;
        c.Zoom.WheelZoom = c.Zoom.Enabled;
        c.Zoom.PinchZoom = c.Zoom.Enabled;
        c.Zoom.ShowNavigator = Navigator.IsChecked == true;
        c.Zoom.NavigatorPicture = NavigatorPicture.IsChecked == true;
        c.Zoom.NavigatorAlways = NavigatorAlways.IsChecked == true;
        c.Ambient.Enabled = AmbientEnabled.IsChecked == true;
        c.Ambient.Placement = SelectedTag(AmbientPlacement, "around");
        c.Ambient.Scaling = SelectedTag(AmbientScaling, "cover");
        c.Ambient.FlipHorizontal = AmbientFlip.IsChecked == true;
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

    /// <summary>The copies' sliders preview live (the gap moves as it is dragged) and are written after the drag.</summary>
    private void OnCopiesSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_host is null) return;
        ShowCopiesValues();
        if (_loading) return;
        var most = (int)Math.Round(CopiesMost.Value);
        var gap = Math.Round(CopiesGap.Value);
        _host.PreviewConfig(c =>
        {
            c.Copies.Most = most;
            c.Copies.Gap = gap;
        });
    }

    private void OnCopiesOption(object sender, RoutedEventArgs e) => Save(c =>
    {
        c.Copies.MaxSize = int.TryParse(SelectedTag(CopiesMaxSize, "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) ? size : 0;
        c.Copies.Remember = CopiesRemember.IsChecked == true;
    });

    private void ShowCopiesValues()
    {
        var most = (int)Math.Round(CopiesMost.Value);
        CopiesMostValue.Text = most == 1 ? "1 copy" : $"{most} copies";
        CopiesGapValue.Text = CopiesGap.Value < 0.5 ? "none" : $"{CopiesGap.Value:0} px";
    }

    /// <summary>
    /// Narrows the panel to the settings that mention what was typed, row by row, opening their
    /// groups and saying how many there are. A group whose own name matches shows whole.
    ///
    /// Eleven groups and a hundred-odd controls is more than anyone should scroll through hunting
    /// for one switch, and a whole group shown for one word in it hid nothing.
    /// </summary>
    private void OnFilter(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = SettingsFilter.Text.Trim();
        FilterHint.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var matches = 0;
        foreach (var group in Groups())
        {
            var whole = query.Length > 0 && Mentions(group.Header, query);
            var shown = group.Content is Panel body ? FilterRows(body, whole ? string.Empty : query) : 0;
            var hit = query.Length == 0 || whole || shown > 0;
            group.Visibility = hit ? Visibility.Visible : Visibility.Collapsed;
            if (query.Length > 0 && hit)
            {
                group.IsExpanded = true;
                matches += shown;
            }
        }

        FilterEmpty.Visibility = query.Length > 0 && matches == 0 ? Visibility.Visible : Visibility.Collapsed;
        FilterCount.Visibility = query.Length > 0 && matches > 0 ? Visibility.Visible : Visibility.Collapsed;
        FilterCount.Text = matches == 1 ? "1 setting matches" : $"{matches} settings match";
        if (query.Length == 0 && _host is not null && !_loading)
        {
            // Some rows show only in some states (a dragged HUD, a note about the audio source);
            // clearing the search gives them back to the state that decides.
            Refresh();
        }
    }

    /// <summary>
    /// Shows the rows of a group that mention the words, or every row for no words, and says how
    /// many are showing. A row is a setting row, a slider with its label line, a block marked as
    /// one row, or a button; a nested panel is filtered row by row; a plain line of explanation is
    /// put away while searching.
    /// </summary>
    private static int FilterRows(Panel body, string query)
    {
        var searching = query.Length > 0;
        var shown = 0;
        var children = body.Children.OfType<UIElement>().ToArray();
        for (var i = 0; i < children.Length; i++)
        {
            var child = children[i];
            if (child is Grid label && i + 1 < children.Length && children[i + 1] is Slider slider)
            {
                var pair = !searching || Mentions(label, query) || Mentions(slider, query);
                label.Visibility = pair ? Visibility.Visible : Visibility.Collapsed;
                slider.Visibility = label.Visibility;
                shown += pair ? 1 : 0;
                i++;
                continue;
            }

            if (child is StackPanel { Tag: not "row" } inner)
            {
                var count = FilterRows(inner, query);
                inner.Visibility = !searching || count > 0 ? Visibility.Visible : Visibility.Collapsed;
                shown += count;
                continue;
            }

            if (child is TextBlock)
            {
                child.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
                continue;
            }

            var hit = !searching || Mentions(child, query);
            child.Visibility = hit ? Visibility.Visible : Visibility.Collapsed;
            shown += hit ? 1 : 0;
        }

        return shown;
    }

    private IEnumerable<Expander> Groups() =>
        [GroupDisplay, GroupAudio, GroupSession, GroupZoom, GroupInput, GroupCopies, GroupHud, GroupLockScreen, GroupCaptures, GroupStartup, GroupAdvanced];

    /// <summary>Whether something says the words: its text, a label, a hint, an option or a button's words.</summary>
    private static bool Mentions(object? subject, string query)
    {
        bool Says(string? text) => text is not null && text.Contains(query, StringComparison.OrdinalIgnoreCase);

        switch (subject)
        {
            case string text:
                return Says(text);
            case null:
                return false;
            case DependencyObject root:
                foreach (var node in Descendants(root).Prepend(root))
                {
                    var said = node switch
                    {
                        TextBlock text => Says(text.Text),
                        HeaderedContentControl row => Says(row.Header as string),
                        ContentControl control => Says(control.Content as string),
                        _ => false,
                    };
                    if (said || (node is FrameworkElement element && Says(System.Windows.Automation.AutomationProperties.GetName(element))))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        // The content of a collapsed expander is never realised, so the logical tree is what has
        // the words in it whether the group is open or shut.
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var deeper in Descendants(child))
            {
                yield return deeper;
            }
        }
    }

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

        Save(c =>
        {
            var fresh = new RexConfig();
            c.Mirror = fresh.Mirror;
            c.Session = fresh.Session;
            c.Touchpad = fresh.Touchpad;
            c.Input = fresh.Input;
            c.Zoom = fresh.Zoom;
            c.Copies = fresh.Copies;
            c.Ambient = fresh.Ambient;
            c.Hud = fresh.Hud;
            c.PatternGuide = fresh.PatternGuide;
            c.Wireless = fresh.Wireless;
            c.Logging = fresh.Logging;
            c.App = fresh.App;
        });

        _window?.SetStatus("Every app setting is back to how it ships.");
    }

    private void OnHudChanged(object sender, RoutedEventArgs e) => Save(c =>
    {
        c.Hud.Enabled = HudEnabled.IsChecked == true;
        c.Hud.ShowMessages = HudMessages.IsChecked == true;
    });

    /// <summary>
    /// Choosing a position is how the bar gets pinned back: it overrides wherever it was dragged to,
    /// which the other HUD settings must leave alone.
    /// </summary>
    private void OnHudPosition(object sender, SelectionChangedEventArgs e) => Save(c =>
    {
        c.Hud.Position = SelectedTag(HudPosition, "top");
        c.Hud.X = null;
        c.Hud.Y = null;
    });

    private void OnHudPinBack(object sender, RoutedEventArgs e) => Save(c =>
    {
        c.Hud.X = null;
        c.Hud.Y = null;
    });

    private void OnHudSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_host is null) return;
        HudScaleValue.Text = $"{HudScale.Value * 100:0}%";
        HudOpacityValue.Text = $"{HudOpacity.Value * 100:0}%";
        HudDelayValue.Text = $"{HudDelay.Value:0} s";
        if (_loading) return;
        var scale = Math.Round(HudScale.Value, 2);
        var opacity = Math.Round(HudOpacity.Value, 2);
        var seconds = Math.Round(HudDelay.Value);
        _host.PreviewConfig(c =>
        {
            c.Hud.Scale = scale;
            c.Hud.Opacity = opacity;
            c.Hud.HideSeconds = seconds;
        });
    }

    private void OnResetHud(object sender, RoutedEventArgs e) => Save(c => c.Hud = new HudSettings());

    /// <summary>
    /// One chip per action, filled in when the HUD carries it, above a preview of the real bar.
    /// A set you pick from wants chips; a switch would say each action is a setting of its own.
    /// </summary>
    private void BuildHudButtons(HudSettings hud)
    {
        HudButtonCount.Text = hud.Buttons.Count == 0
            ? "Nothing chosen, so the bar stays empty. Click a chip to add a button."
            : "Click to add or remove. They appear in this order.";

        if (HudButtons.Children.Count == 0)
        {
            foreach (var action in MirrorActions.All)
            {
                var chip = new ToggleButton
                {
                    Content = action.Label,
                    Tag = action.Id,
                    ToolTip = action.Detail,
                    Style = (Style)FindResource("Chip"),
                };
                System.Windows.Automation.AutomationProperties.SetAutomationId(chip, "hud-button " + action.Id);
                System.Windows.Automation.AutomationProperties.SetName(chip, action.Label);
                chip.Checked += OnHudButton;
                chip.Unchecked += OnHudButton;
                HudButtons.Children.Add(chip);
            }
        }

        foreach (var chip in HudButtons.Children.OfType<ToggleButton>())
        {
            chip.IsChecked = hud.Buttons.Contains((string)chip.Tag!, StringComparer.Ordinal);
        }

        BuildHudPreview(hud);
    }

    /// <summary>Shows the bar exactly as fullscreen will draw it, so the choice is never abstract.</summary>
    private void BuildHudPreview(HudSettings hud)
    {
        HudPreview.Children.Clear();
        if (hud.Buttons.Count == 0)
        {
            HudPreview.Children.Add(new TextBlock
            {
                Text = "empty",
                Style = (Style)FindResource("MutedText"),
                FontSize = 11,
                Margin = new Thickness(2, 2, 0, 2),
            });
            return;
        }

        foreach (var action in hud.Buttons.Select(MirrorActions.Find).OfType<MirrorAction>())
        {
            var icon = ActionIcons.For(action.Id);
            var content = icon is not null && TryFindResource(icon) is System.Windows.Media.Geometry geometry
                ? new System.Windows.Shapes.Path
                {
                    Data = geometry,
                    Stroke = (System.Windows.Media.Brush)FindResource("Text"),
                    StrokeThickness = 1.6,
                    Width = 16,
                    Height = 16,
                    Stretch = System.Windows.Media.Stretch.Uniform,
                } as object
                : action.Label;

            HudPreview.Children.Add(new Border
            {
                Background = (System.Windows.Media.Brush)FindResource("Raised"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("Line"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 0, 4, 4),
                ToolTip = action.Label,
                Child = content is string text
                    ? new TextBlock { Text = text, FontSize = 11, Foreground = (System.Windows.Media.Brush)FindResource("Text") }
                    : (UIElement)content,
            });
        }
    }

    private void OnHudButton(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not ToggleButton { Tag: string id })
        {
            return;
        }

        Save(c =>
        {
            if (c.Hud.Buttons.Contains(id, StringComparer.Ordinal))
            {
                c.Hud.Buttons.Remove(id);
            }
            else
            {
                // Keep the catalogue's order so the bar never looks shuffled.
                c.Hud.Buttons = MirrorActions.Ids
                    .Where(x => x == id || c.Hud.Buttons.Contains(x, StringComparer.Ordinal))
                    .ToList();
            }
        });
    }

    private void OnChooseScreenshotFolder(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;
        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose where screenshots are saved",
            InitialDirectory = _host.Paths.ScreenshotFolder(_host.Config.App.ScreenshotDirectory),
        };
        if (picker.ShowDialog(_window!) == true)
            Save(c => c.App.ScreenshotDirectory = picker.FolderName);
    }

    private void OnDefaultScreenshotFolder(object sender, RoutedEventArgs e) =>
        Save(c => c.App.ScreenshotDirectory = new AppSettings().ScreenshotDirectory);
}
