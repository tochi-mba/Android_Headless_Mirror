using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Rex.Core;

namespace Rex.Mirror.Views;

/// <summary>
/// More of scrcpy in the tab: what the phone sends as the picture (its encoder, part of its screen,
/// how it is captured as it turns and tilted, how this PC scales it, taps shown on the phone), the
/// sound's playback buffer and whether the mirror needs sound, and when the mirror stops or stops
/// being started again. Each applies the next time the mirror starts, like the rows above them.
/// </summary>
public partial class SettingsPanel
{
    /// <summary>What the last read of the phone's encoders said, for the line under the list.</summary>
    private string _encoderReadNote = string.Empty;

    private void RefreshScrcpy(RexConfig c)
    {
        FillEncoders(c);
        // Someone typing a crop keeps what they have typed, and a refused one stays to be put right.
        if (!Crop.IsKeyboardFocusWithin && CropError.Visibility != Visibility.Visible)
        {
            Crop.Text = c.Mirror.Crop;
        }

        SelectTag(CaptureOrientation, c.Mirror.CaptureOrientation);
        SelectTag(StartOrientation, c.Mirror.StartOrientation);
        Angle.Value = c.Mirror.Angle;
        SmoothScaling.IsChecked = c.Mirror.SmoothScaling;
        ShowTouches.IsChecked = c.Mirror.ShowTouches;
        AudioOutputBuffer.Value = Math.Min(c.Mirror.AudioOutputBufferMs, AudioOutputBuffer.Maximum);
        RequireAudio.IsChecked = c.Mirror.RequireAudio;
        RestartLimit.Value = c.Session.RestartLimit;
        SelectTag(TimeLimit, c.Mirror.TimeLimitMinutes.ToString(CultureInfo.InvariantCulture));

        var on = SettingsDependencies.Of(c);
        AudioOutputBuffer.IsEnabled = on.Audio;
        RequireAudio.IsEnabled = on.Audio;
        RestartLimit.IsEnabled = on.Restarts;
        ShowScrcpyValues();
    }

    private void ShowScrcpyValues()
    {
        AngleValue.Text = Angle.Value < 0.5 ? "straight" : $"{Angle.Value:0}°";
        AudioOutputBufferValue.Text = AudioOutputBuffer.Value < 0.5 ? "the shortest" : $"{AudioOutputBuffer.Value:0} ms";
        var times = (int)Math.Round(RestartLimit.Value);
        RestartLimitValue.Text = times == 1 ? "once" : $"{times} times";
    }

    /// <summary>
    /// The phone's encoders for the chosen codec, once they have been read, and the one chosen
    /// before even when it has not been: the list never hides what config.json says.
    /// </summary>
    private void FillEncoders(RexConfig c)
    {
        var device = _host!.Session.ActiveDevice;
        var known = device is null ? null : _host.Session.EncodersOf(device.Serial);
        var choices = EncoderList.For(known ?? [], c.Mirror.VideoCodec).Select(e => (e.Name, e.Label)).ToList();
        if (c.Mirror.VideoEncoder.Length > 0 && choices.All(choice => choice.Name != c.Mirror.VideoEncoder))
        {
            choices.Add((c.Mirror.VideoEncoder, c.Mirror.VideoEncoder));
        }

        var shown = VideoEncoder.Items.OfType<ComboBoxItem>().Skip(1).Select(item => (item.Tag as string ?? string.Empty, item.Content as string ?? string.Empty));
        if (!shown.SequenceEqual(choices))
        {
            while (VideoEncoder.Items.Count > 1)
            {
                VideoEncoder.Items.RemoveAt(1);
            }

            foreach (var (name, label) in choices)
            {
                VideoEncoder.Items.Add(new ComboBoxItem { Content = label, Tag = name });
            }
        }

        SelectTag(VideoEncoder, c.Mirror.VideoEncoder);
        var reading = _host.Session.ReadingEncoders;
        VideoEncoderRead.IsEnabled = device is not null && !reading;
        VideoEncoderRead.ToolTip = device is null ? "Connect a phone to read its encoders." : null;
        VideoEncoderNote.Text = device is null ? "Connect a phone to read its encoders."
            : reading ? "Reading the phone's encoders…"
            : _encoderReadNote.Length > 0 ? _encoderReadNote
            : known is null ? "Read them to choose one."
            : EncoderWords(choices.Count(choice => known.Any(e => e.Name == choice.Name)), c.Mirror.VideoCodec);
        VideoEncoderNote.Visibility = VideoEncoderNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private string EncoderWords(int count, string codec)
    {
        var name = ScrcpyArguments.CodecName(codec);
        var phone = _host!.Session.Identity?.DisplayName ?? "this phone";
        return count switch
        {
            0 => $"{phone} has no encoder for {name}; choose another video codec above.",
            1 => $"1 encoder for {name} on {phone}.",
            _ => $"{count} encoders for {name} on {phone}.",
        };
    }

    private async void OnReadEncoders(object sender, RoutedEventArgs e)
    {
        if (_host?.Session.ActiveDevice is not { } device) return;
        _encoderReadNote = string.Empty;
        var reading = _host.Session.ReadEncodersAsync(device.Serial);
        Refresh();
        var read = await reading.ConfigureAwait(true);
        _encoderReadNote = read.Ok ? string.Empty : "Could not read them: " + read.Error;
        Refresh();
    }

    private void OnPictureChanged(object sender, RoutedEventArgs e) => Save(c =>
    {
        c.Mirror.VideoEncoder = SelectedTag(VideoEncoder, string.Empty);
        c.Mirror.CaptureOrientation = SelectedTag(CaptureOrientation, string.Empty);
        c.Mirror.StartOrientation = SelectedTag(StartOrientation, "0");
        c.Mirror.SmoothScaling = SmoothScaling.IsChecked == true;
        c.Mirror.ShowTouches = ShowTouches.IsChecked == true;
        c.Mirror.RequireAudio = RequireAudio.IsChecked == true;
        c.Mirror.TimeLimitMinutes = int.Parse(SelectedTag(TimeLimit, "0"), CultureInfo.InvariantCulture);
    });

    private void OnPictureSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Sliders raise ValueChanged while the XAML is still loading; the labels follow once attached.
        if (_host is null) return;
        ShowScrcpyValues();
        if (_loading) return;
        var angle = (int)Math.Round(Angle.Value);
        var buffer = (int)Math.Round(AudioOutputBuffer.Value);
        var restarts = (int)Math.Round(RestartLimit.Value);
        _host.PreviewConfig(c =>
        {
            c.Mirror.Angle = angle;
            c.Mirror.AudioOutputBufferMs = buffer;
            c.Session.RestartLimit = restarts;
        });
    }

    private void OnCrop(object sender, KeyboardFocusChangedEventArgs e) => CommitCrop();

    /// <summary>As the app to open: typing commits on Enter or on leaving the box; a value set whole from outside the keyboard applies at once.</summary>
    private void OnCropText(object sender, TextChangedEventArgs e)
    {
        if (!Crop.IsKeyboardFocusWithin)
        {
            CommitCrop();
        }
    }

    private void OnCropKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitCrop();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _host is not null)
        {
            CropError.Visibility = Visibility.Collapsed;
            Crop.Text = _host.Config.Mirror.Crop;
            e.Handled = true;
        }
    }

    /// <summary>A crop is saved only when scrcpy could use it; otherwise the line under the box says how to write it.</summary>
    private void CommitCrop()
    {
        if (_host is null || _loading) return;
        var text = Crop.Text.Trim();
        var why = MirrorSettings.WhyNotCrop(text);
        CropError.Text = why ?? string.Empty;
        CropError.Visibility = why is null ? Visibility.Collapsed : Visibility.Visible;
        if (why is null && text != _host.Config.Mirror.Crop)
        {
            Save(c => c.Mirror.Crop = text);
        }
    }
}
