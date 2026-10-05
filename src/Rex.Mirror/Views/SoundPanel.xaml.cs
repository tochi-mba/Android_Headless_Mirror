using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Rex.Core;
using Rex.Mirror.Services;
using Rex.Mirror.Services.Sound;

namespace Rex.Mirror.Views;

/// <summary>
/// The sound panel: the volume of the phone's sound on this PC, mute, a level meter, and the reason
/// a rule is lowering or silencing it. When there is nothing to control it says why instead of
/// showing a slider that does nothing.
/// </summary>
public partial class SoundPanel : UserControl
{
    private readonly DispatcherTimer _meter;
    private MainWindow? _window;
    private AppHost? _host;
    private PhoneSound? _sound;
    private bool _showing;

    public SoundPanel()
    {
        InitializeComponent();
        _meter = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _meter.Tick += (_, _) => ShowMeter();
        // The meter is only read while it can be seen.
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && _host?.Config.Sound.ShowLevel == true)
            {
                _meter.Start();
            }
            else
            {
                _meter.Stop();
            }
        };
    }

    /// <summary>The sound the panel shows and sets from now on: that of the phone in use.</summary>
    public void Use(PhoneSound sound) => _sound = sound;

    public void Attach(MainWindow window, AppHost host, PhoneSound sound)
    {
        _window = window;
        _host = host;
        _sound = sound;
        Show();
    }

    /// <summary>Shows the sound as it is now.</summary>
    public void Show()
    {
        if (_sound is null || _host is null)
        {
            return;
        }

        _showing = true;
        try
        {
            var problem = _sound.Problem;
            SoundControls.Visibility = problem is null ? Visibility.Visible : Visibility.Collapsed;
            SoundUnavailable.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
            SoundProblem.Text = problem ?? string.Empty;
            SoundTurnOn.Visibility = _host.Config.Mirror.Audio ? Visibility.Collapsed : Visibility.Visible;
            SoundPhone.Text = _window?.TargetPhone is { } target ? _window.NameOf(target.Serial) : _host.Session.Identity?.DisplayName ?? string.Empty;
            SoundVolume.Value = Math.Round(_sound.Volume * 100);
            SoundVolumeValue.Text = Words(_sound.Volume, _sound.Muted);
            SoundMute.Content = _sound.Muted ? "Unmute" : "Mute";
            System.Windows.Automation.AutomationProperties.SetName(SoundMute, (_sound.Muted ? "Unmute" : "Mute") + " the phone's sound on this PC");
            SoundWhy.Text = _sound.Target.Why ?? string.Empty;
            SoundWhy.Visibility = _sound.Target.Why is null || _sound.Muted ? Visibility.Collapsed : Visibility.Visible;
            SoundMeterTrack.Visibility = _host.Config.Sound.ShowLevel ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _showing = false;
        }
    }

    /// <summary>The level in words: "62%", or "Muted".</summary>
    internal static string Words(double volume, bool muted) =>
        muted ? "Muted" : (volume * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary>Gives the slider the keyboard, as the panel opens.</summary>
    public void FocusFirst()
    {
        if (SoundControls.IsVisible)
        {
            SoundVolume.Focus();
        }
        else
        {
            SoundSettingsLink.Focus();
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _window?.CloseSoundPanel();
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private void OnVolume(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_showing || _sound is null)
        {
            return;
        }

        _sound.SetVolume(e.NewValue / 100);
    }

    private void OnMute(object sender, RoutedEventArgs e) => _sound?.SetMuted(!_sound.Muted);

    private void OnTurnOn(object sender, RoutedEventArgs e) => _host?.UpdateConfig(c => c.Mirror.Audio = true);

    private void OnSettings(object sender, RoutedEventArgs e) => _window?.OpenSettingsGroup("GroupSound");

    private void ShowMeter()
    {
        var peak = Math.Clamp(_sound?.Peak ?? 0, 0, 1);
        SoundMeter.Width = SoundMeterTrack.ActualWidth * peak;
    }
}
