using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror.Views.Settings;

/// <summary>
/// Sound on this PC: the volume a phone starts at, the step, remembering each phone, and the rules
/// that mute or lower the sound. Sliders preview at once; switches save at once.
/// </summary>
public partial class SoundGroup : UserControl, ISettingsGroup
{
    /// <summary>What the balance row says when the output cannot be balanced.</summary>
    public const string NotStereo = "This output is not stereo, so there is nothing to balance.";

    private SettingsPanel? _panel;
    private AppHost? _host;
    private MainWindow? _window;
    private bool _loading;

    public SoundGroup() => InitializeComponent();

    Expander ISettingsGroup.Group => GroupSound;

    public void Attach(SettingsPanel panel, MainWindow window, AppHost host)
    {
        _panel = panel;
        _host = host;
        _window = window;
    }

    public void Refresh(RexConfig config)
    {
        var sound = config.Sound;
        _loading = true;
        try
        {
            SoundLevel.Value = sound.Volume;
            SoundMuted.IsChecked = sound.Muted;
            SoundStep.Value = sound.Step;
            SoundPerPhone.IsChecked = sound.RememberPerPhone;
            SoundStartMuted.IsChecked = sound.StartMuted;
            SoundMuteHidden.IsChecked = sound.MuteWhenHidden;
            SoundMuteBehind.IsChecked = sound.MuteWhenBehind;
            SoundMuteLocked.IsChecked = sound.MuteWhenLocked;
            SoundLowerTyping.IsChecked = sound.LowerWhileTyping;
            SoundLowerTo.Value = sound.LowerTo;
            SoundLowerFor.Value = sound.LowerForMs;
            SoundFade.Value = sound.FadeMs;
            SoundBalance.Value = sound.Balance;
            SoundFollowMixer.IsChecked = sound.FollowMixer;
            SoundShowLevel.IsChecked = sound.ShowLevel;
            SoundWheel.IsChecked = sound.WheelOnButton;

            var on = SettingsDependencies.Of(config);
            SoundOptions.IsEnabled = on.Sound;
            SoundOffNote.Visibility = on.Sound ? Visibility.Collapsed : Visibility.Visible;
            SoundLowerOptions.IsEnabled = on.SoundLowering;
            // Balance needs two channels; an output that is known to have another number says so.
            var channels = _window?.PhoneSound.Channels ?? 0;
            SoundBalance.IsEnabled = channels is 0 or 2;
            AutomationProperties.SetHelpText(SoundBalance, SoundBalance.IsEnabled ? string.Empty : NotStereo);
            SoundBalance.ToolTip = SoundBalance.IsEnabled ? null : NotStereo;
            ShowValues();
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnOption(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Save(c =>
        {
            c.Sound.Muted = SoundMuted.IsChecked == true;
            c.Sound.RememberPerPhone = SoundPerPhone.IsChecked == true;
            c.Sound.StartMuted = SoundStartMuted.IsChecked == true;
            c.Sound.MuteWhenHidden = SoundMuteHidden.IsChecked == true;
            c.Sound.MuteWhenBehind = SoundMuteBehind.IsChecked == true;
            c.Sound.MuteWhenLocked = SoundMuteLocked.IsChecked == true;
            c.Sound.LowerWhileTyping = SoundLowerTyping.IsChecked == true;
            c.Sound.FollowMixer = SoundFollowMixer.IsChecked == true;
            c.Sound.ShowLevel = SoundShowLevel.IsChecked == true;
            c.Sound.WheelOnButton = SoundWheel.IsChecked == true;
        });
    }

    private void OnSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Sliders fire while their XAML is loading; the value labels are created later.
        if (_host is null) return;
        ShowValues();
        if (_loading) return;
        var (volume, step, lowerTo, lowerFor, fade, balance) =
            (Math.Round(SoundLevel.Value, 2), Math.Round(SoundStep.Value, 2), Math.Round(SoundLowerTo.Value, 2), (int)SoundLowerFor.Value, (int)SoundFade.Value, Math.Round(SoundBalance.Value, 2));
        _host.PreviewConfig(c =>
        {
            c.Sound.Volume = volume;
            c.Sound.Step = step;
            c.Sound.LowerTo = lowerTo;
            c.Sound.LowerForMs = lowerFor;
            c.Sound.FadeMs = fade;
            c.Sound.Balance = balance;
        });
    }

    private void Save(Action<RexConfig> mutate)
    {
        _panel?.Save(mutate);
    }

    private void ShowValues()
    {
        SoundLevelValue.Text = Percent(SoundLevel.Value);
        SoundStepValue.Text = Percent(SoundStep.Value);
        SoundLowerToValue.Text = SoundLowerTo.Value >= 1 ? "Not lowered at 100%" : Percent(SoundLowerTo.Value) + " of your volume";
        SoundLowerForValue.Text = (SoundLowerFor.Value / 1000).ToString("0.0", CultureInfo.InvariantCulture) + " s after the last key";
        SoundFadeValue.Text = SoundFade.Value < 1 ? "At once" : SoundFade.Value.ToString("0", CultureInfo.InvariantCulture) + " ms";
        SoundBalanceValue.Text = SoundBalance.Value switch
        {
            < 0 => Percent(-SoundBalance.Value) + " left",
            > 0 => Percent(SoundBalance.Value) + " right",
            _ => "Centre",
        };
    }

    /// <summary>A fraction in words: 0.62 is "62%".</summary>
    private static string Percent(double fraction) => (fraction * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
}
