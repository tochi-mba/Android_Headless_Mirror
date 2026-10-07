using System.Globalization;
using System.IO;
using System.Windows;
using Rex.Core;

namespace Rex.Mirror.Views;

/// <summary>
/// The rows for values config.json always had but the tab did not show: the sound's format and
/// buffer, three switches of the session, the touchpad, wheel and pinch zoom on their own, the
/// lock screen guide's last four, and where recordings go.
/// </summary>
public partial class SettingsPanel
{
    private void RefreshRemaining(RexConfig c)
    {
        SelectTag(AudioCodec, c.Mirror.AudioCodec);
        AudioBuffer.Value = Math.Min(c.Mirror.AudioBufferMs, AudioBuffer.Maximum);
        KeepActive.IsChecked = c.Session.KeepActive;
        WakeBeforeMirror.IsChecked = c.Session.WakeBeforeMirror;
        DismissKeyguard.IsChecked = c.Session.DismissKeyguard;
        TouchpadEnabled.IsChecked = c.Touchpad.Enabled;
        WheelZoom.IsChecked = c.Zoom.WheelZoom;
        PinchZoom.IsChecked = c.Zoom.PinchZoom;
        PatternAsk.IsChecked = c.PatternGuide.AskPerDevice;
        PatternCalibration.IsChecked = c.PatternGuide.CalibrationEnabled;
        PatternTrail.IsChecked = c.PatternGuide.ShowCursorTrail;
        PatternOpacity.Value = c.PatternGuide.Opacity;
        RecordLocation.Text = Path.Combine(_host!.Paths.Root, c.Mirror.RecordDirectory);

        var on = SettingsDependencies.Of(c);
        AudioCodec.IsEnabled = on.Audio;
        AudioBuffer.IsEnabled = on.Audio;
        TwoFinger.IsEnabled = on.Touchpad;
        WheelZoom.IsEnabled = on.Zoom;
        PinchZoom.IsEnabled = on.Zoom && on.Touchpad;
        foreach (var control in new UIElement[] { PatternAsk, PatternCalibration, PatternTrail, PatternOpacity })
        {
            control.IsEnabled = on.PatternOptions;
        }

        ShowRemainingValues();
        RefreshChanges(c);
    }

    private void ShowRemainingValues()
    {
        AudioBufferValue.Text = AudioBuffer.Value == 0 ? "none (lowest delay)" : $"{AudioBuffer.Value:0} ms";
        PatternOpacityValue.Text = $"{PatternOpacity.Value * 100:0}%";
    }

    private void OnRemainingChanged(object sender, RoutedEventArgs e) => Save(c =>
    {
        c.Mirror.AudioCodec = SelectedTag(AudioCodec, "opus");
        c.Session.KeepActive = KeepActive.IsChecked == true;
        c.Session.WakeBeforeMirror = WakeBeforeMirror.IsChecked == true;
        c.Session.DismissKeyguard = DismissKeyguard.IsChecked == true;
        c.Touchpad.Enabled = TouchpadEnabled.IsChecked == true;
        c.Zoom.WheelZoom = WheelZoom.IsChecked == true;
        c.Zoom.PinchZoom = PinchZoom.IsChecked == true;
        c.PatternGuide.AskPerDevice = PatternAsk.IsChecked == true;
        c.PatternGuide.CalibrationEnabled = PatternCalibration.IsChecked == true;
        c.PatternGuide.ShowCursorTrail = PatternTrail.IsChecked == true;
    });

    private void OnRemainingSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Sliders raise ValueChanged while the XAML is still loading; the labels follow once attached.
        if (_host is null) return;
        ShowRemainingValues();
        if (_loading) return;
        var buffer = (int)AudioBuffer.Value;
        var opacity = Math.Round(PatternOpacity.Value, 2);
        _host.PreviewConfig(c =>
        {
            c.Mirror.AudioBufferMs = buffer;
            c.PatternGuide.Opacity = opacity;
        });
    }

    /// <summary>A folder inside the app's own: recordings may only go there, so nothing is written elsewhere by accident.</summary>
    private void OnChooseRecordFolder(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;
        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Save recordings to",
            InitialDirectory = PickerFolder.Ready(Path.Combine(_host.Paths.Root, _host.Config.Mirror.RecordDirectory), _host.Paths.Root),
        };
        if (picker.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        var relative = Path.GetRelativePath(_host.Paths.Root, picker.FolderName).Replace('\\', '/');
        var inside = PathRules.IsSafeRelativePath(relative);
        RecordFolderProblem.Text = inside ? string.Empty : $"Choose a folder inside {_host.Paths.Root}.";
        RecordFolderProblem.Visibility = inside ? Visibility.Collapsed : Visibility.Visible;
        if (inside)
        {
            Save(c => c.Mirror.RecordDirectory = relative);
        }
    }

    private void OnDefaultRecordFolder(object sender, RoutedEventArgs e)
    {
        RecordFolderProblem.Visibility = Visibility.Collapsed;
        Save(c => c.Mirror.RecordDirectory = new MirrorSettings().RecordDirectory);
    }
}
