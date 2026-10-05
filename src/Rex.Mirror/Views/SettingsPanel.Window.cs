using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror.Views;

/// <summary>
/// The settings about how the app tells you things and keeps what it captures: what is new after
/// an update, words from the tray, screenshots, how zoom answers the wheel, and how far and fast
/// keyboard swipes go. All of them apply at once. The window's own are in its group.
/// </summary>
public partial class SettingsPanel
{
    private void RefreshWindow(RexConfig c)
    {
        ShowWhatsNew.IsChecked = c.App.ShowWhatsNew;
        NotifyConnections.IsChecked = c.App.NotifyConnections;
        NotifyMirrorStops.IsChecked = c.App.NotifyMirrorStops;
        ScreenshotFlash.IsChecked = c.App.ScreenshotFlash;
        OpenScreenshots.IsChecked = c.App.OpenScreenshots;
        ZoomKeyStep.Value = c.Zoom.KeyStep;
        ZoomKeyStep.IsEnabled = c.Zoom.Enabled;
        PatternDotSize.Value = c.PatternGuide.DotSize;
        PatternDotSize.IsEnabled = c.PatternGuide.Enabled;
        // Someone typing a name keeps what they have typed, and a refused one stays to be put right.
        if (!CaptureNames.IsKeyboardFocusWithin && CaptureNamesError.Visibility != Visibility.Visible)
        {
            CaptureNames.Text = c.App.CaptureNames;
        }

        ShowCaptureExample(c.App.CaptureNames);
        SelectTag(ScreenshotFormat, c.App.ScreenshotFormat);
        CopyScreenshots.IsChecked = c.App.CopyScreenshots;
        SelectTag(ZoomAnchor, c.Zoom.ZoomAtPointer ? "pointer" : "middle");
        InvertWheel.IsChecked = c.Zoom.InvertWheel;
        ResetOnRotate.IsChecked = c.Zoom.ResetOnRotate;
        SwipeLength.Value = c.Input.SwipeLength;
        SwipeMilliseconds.Value = c.Input.SwipeMilliseconds;
        ShowSwipeValues();
        ShowWindowValues();
    }

    private void OnWindowChanged(object sender, RoutedEventArgs e) => Save(c =>
    {
        c.App.ShowWhatsNew = ShowWhatsNew.IsChecked == true;
        c.App.NotifyConnections = NotifyConnections.IsChecked == true;
        c.App.NotifyMirrorStops = NotifyMirrorStops.IsChecked == true;
        c.App.ScreenshotFlash = ScreenshotFlash.IsChecked == true;
        c.App.OpenScreenshots = OpenScreenshots.IsChecked == true;
        c.App.ScreenshotFormat = SelectedTag(ScreenshotFormat, "png");
        c.App.CopyScreenshots = CopyScreenshots.IsChecked == true;
        c.Zoom.ZoomAtPointer = SelectedTag(ZoomAnchor, "pointer") == "pointer";
        c.Zoom.InvertWheel = InvertWheel.IsChecked == true;
        c.Zoom.ResetOnRotate = ResetOnRotate.IsChecked == true;
    });

    /// <summary>The zoom keys' step and the pattern guide's dots preview as they are dragged and are written after.</summary>
    private void OnWindowSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_host is null) return;
        ShowWindowValues();
        if (_loading) return;
        var step = Math.Round(ZoomKeyStep.Value, 2);
        var dots = Math.Round(PatternDotSize.Value, 2);
        _host.PreviewConfig(c =>
        {
            c.Zoom.KeyStep = step;
            c.PatternGuide.DotSize = dots;
        });
    }

    private void ShowWindowValues()
    {
        ZoomKeyStepValue.Text = $"{ZoomKeyStep.Value * 100:0}%";
        PatternDotSizeValue.Text = $"{PatternDotSize.Value * 100:0}%";
    }

    /// <summary>What the next screenshot would be called, under the box, so the tokens are plain.</summary>
    private void ShowCaptureExample(string template) =>
        CaptureNamesExample.Text = "Next: " + CaptureName.Format(template, DateTime.Now,
            _host?.Session.Identity?.DisplayName ?? "Galaxy S21 Ultra", _host?.Session.Identity?.Model ?? "SM-G998B", 1) +
            ScreenshotFile.Extension(_host?.Config.App.ScreenshotFormat ?? "png");

    private void OnCaptureNames(object sender, KeyboardFocusChangedEventArgs e) => CommitCaptureNames();

    /// <summary>As the app to open: typing commits on Enter or on leaving the box; a value set whole from outside the keyboard applies at once.</summary>
    private void OnCaptureNamesText(object sender, TextChangedEventArgs e)
    {
        if (!CaptureNames.IsKeyboardFocusWithin)
        {
            CommitCaptureNames();
        }
    }

    private void OnCaptureNamesKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitCaptureNames();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _host is not null)
        {
            CaptureNamesError.Visibility = Visibility.Collapsed;
            CaptureNames.Text = _host.Config.App.CaptureNames;
            e.Handled = true;
        }
    }

    private void CommitCaptureNames()
    {
        if (_host is null || _loading) return;
        var text = CaptureNames.Text.Trim();
        var why = CaptureName.WhyNot(text);
        CaptureNamesError.Text = why ?? string.Empty;
        CaptureNamesError.Visibility = why is null ? Visibility.Collapsed : Visibility.Visible;
        if (why is null && text != _host.Config.App.CaptureNames)
        {
            Save(c => c.App.CaptureNames = text);
        }
    }

    /// <summary>The swipe sliders take effect with the next swipe, so they preview live and are written after the drag.</summary>
    private void OnSwipeSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_host is null)
        {
            return;
        }

        ShowSwipeValues();
        if (_loading)
        {
            return;
        }

        var length = Math.Round(SwipeLength.Value, 2);
        var milliseconds = (int)Math.Round(SwipeMilliseconds.Value);
        _host.PreviewConfig(c =>
        {
            c.Input.SwipeLength = length;
            c.Input.SwipeMilliseconds = milliseconds;
        });
    }

    private void ShowSwipeValues()
    {
        SwipeLengthValue.Text = $"{SwipeLength.Value * 100:0}% of the usual";
        SwipeTimeValue.Text = $"{SwipeMilliseconds.Value:0} ms";
    }
}
