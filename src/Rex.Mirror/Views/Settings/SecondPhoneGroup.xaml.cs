using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Rex.Core;
using Rex.Mirror.Services;

namespace Rex.Mirror.Views.Settings;

/// <summary>A second, different phone beside the first: whether, when, where, whose sound, and how its session differs.</summary>
public partial class SecondPhoneGroup : UserControl, ISettingsGroup
{
    private SettingsPanel? _panel;
    private bool _loading;

    public SecondPhoneGroup() => InitializeComponent();

    Expander ISettingsGroup.Group => GroupSecondPhone;

    public void Attach(SettingsPanel panel, MainWindow window, AppHost host) => _panel = panel;

    public void Refresh(RexConfig config)
    {
        var phone = config.SecondPhone;
        _loading = true;
        try
        {
            SecondPhoneEnabled.IsChecked = phone.Enabled;
            SelectTag(SecondPhoneWhen, phone.WhenConnected);
            SecondPhoneRemember.IsChecked = phone.Remember;
            SelectTag(SecondPhoneSide, phone.Side);
            SelectTag(SecondPhoneSound, phone.Sound);
            SelectTag(SecondPhoneScreenOff, phone.ScreenOff);
            SelectTag(SecondPhoneMaxSize, phone.MaxSize.ToString(CultureInfo.InvariantCulture));
            SelectTag(SecondPhoneBitRate, phone.BitRate);
            SecondPhonePause.IsChecked = phone.PauseWhenHidden;
            SecondPhoneProfile.IsChecked = phone.FollowsProfile;
            SecondPhoneOptions.IsEnabled = phone.Enabled;
            // Without the phone's sound on this PC there is nothing to choose between.
            SecondPhoneSound.IsEnabled = config.Mirror.Audio;
            SecondPhoneSound.ToolTip = config.Mirror.Audio ? null : "Phone sound is off in Sound.";
            System.Windows.Automation.AutomationProperties.SetHelpText(SecondPhoneSound, config.Mirror.Audio ? string.Empty : "Phone sound is off in Sound.");
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnOption(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _panel?.Save(c =>
        {
            c.SecondPhone.Enabled = SecondPhoneEnabled.IsChecked == true;
            c.SecondPhone.WhenConnected = SelectedTag(SecondPhoneWhen, "ask");
            c.SecondPhone.Remember = SecondPhoneRemember.IsChecked == true;
            c.SecondPhone.Side = SelectedTag(SecondPhoneSide, "right");
            c.SecondPhone.Sound = SelectedTag(SecondPhoneSound, "main");
            c.SecondPhone.ScreenOff = SelectedTag(SecondPhoneScreenOff, "same");
            c.SecondPhone.MaxSize = int.TryParse(SelectedTag(SecondPhoneMaxSize, "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) ? size : 0;
            c.SecondPhone.BitRate = SelectedTag(SecondPhoneBitRate, string.Empty);
            c.SecondPhone.PauseWhenHidden = SecondPhonePause.IsChecked == true;
            c.SecondPhone.FollowsProfile = SecondPhoneProfile.IsChecked == true;
        });
    }

    private static string SelectedTag(ComboBox combo, string fallback) => (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? fallback;

    private static void SelectTag(ComboBox combo, string tag) =>
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag) ?? combo.Items[0];
}
