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
            AudioDup.IsEnabled = c.Mirror.Audio;
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
            ScreenshotLocation.Text = _host.Paths.ScreenshotFolder(c.App.ScreenshotDirectory);
            RefreshRestartNotice();
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
        c.Mirror.RecordOnStart = Record.IsChecked == true;
        c.Session.TurnScreenOff = TurnScreenOff.IsChecked == true;
        c.Session.StayAwake = StayAwake.IsChecked == true;
        c.Session.PowerOffOnClose = PowerOffOnClose.IsChecked == true;
    });

    private void OnSessionChanged(object sender, RoutedEventArgs e) => Save(c =>
    {
        c.Session.RestartOnUnexpectedExit = RestartOnCrash.IsChecked == true;
        c.Wireless.Enabled = Wireless.IsChecked == true;
        c.Wireless.EnableTcpipWhenUsbAvailable = WirelessTcpip.IsChecked == true;
    });

    private void OnLiveChanged(object sender, RoutedEventArgs e) => Save(c =>
    {
        c.Touchpad.Enabled = true;
        c.Touchpad.TwoFingerToAndroid = TwoFinger.IsChecked == true;
        c.Zoom.Enabled = HostZoom.IsChecked == true;
        c.Zoom.WheelZoom = c.Zoom.Enabled;
        c.Zoom.PinchZoom = c.Zoom.Enabled;
        c.Zoom.ShowNavigator = Navigator.IsChecked == true;
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

    private void OnRestartNow(object sender, RoutedEventArgs e)
    {
        _host?.Session.RestartMirror();
        Refresh();
    }

    private void OnForgetLocks(object sender, RoutedEventArgs e)
    {
        var count = _host?.State.ResetLockScreen("ALL") ?? 0;
        Status.Text = count == 0 ? "No saved answers." : $"Forgot {count} phone(s). You'll be asked again on the next connection.";
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
                Status.Text = "There is no previous configuration to restore.";
                return;
            }

            _host.ReloadConfigFromDisk();
            Refresh();
            Status.Text = "Previous configuration restored.";
        }
        catch (InvalidOperationException ex)
        {
            Status.Text = ex.Message;
        }
    }

    /// <summary>Sliders preview live and are written a moment after the drag ends.</summary>
    private void OnAppearanceChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Sliders raise ValueChanged while the XAML is still loading (Minimum coerces Value); the
        // labels do not exist yet and Refresh() writes them once the host is attached.
        if (_host is null) return;
        NavigatorWidthValue.Text = $"{NavigatorWidth.Value:0} px";
        if (_loading) return;
        var maxZoom = MaximumZoom.Value;
        var wheelStep = Math.Round(WheelSpeed.Value, 2);
        var navigatorWidth = Math.Round(NavigatorWidth.Value);
        _host.PreviewConfig(c =>
        {
            c.Zoom.MaxZoom = maxZoom;
            c.Zoom.WheelStep = wheelStep;
            c.Zoom.NavigatorWidth = navigatorWidth;
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
        AmbientTintValue.Text = c.Ambient.TintStrength < 0.005 ? "off" : $"{c.Ambient.TintStrength * 100:0}% at {c.Ambient.TintHue:0}°";
        AmbientFrameRateValue.Text = $"{c.Ambient.FrameRate:0} fps";

        static string Offset(double value, string negative, string positive) =>
            Math.Abs(value) < 0.005 ? "centred" : $"{Math.Abs(value) * 100:0}% {(value < 0 ? negative : positive)}";
    }

    private void OnResetAmbient(object sender, RoutedEventArgs e) => Save(c => c.Ambient = new AmbientSettings());

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
            ? "Nothing chosen, so the bar stays empty. Tap a chip to add a button."
            : "Tap to add or remove. They appear in this order.";

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
            var icon = HudIcons.For(action.Id);
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

    public void RefreshRestartNotice()
    {
        RestartBar.Visibility = _host?.Session.NeedsRestart == true ? Visibility.Visible : Visibility.Collapsed;
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
