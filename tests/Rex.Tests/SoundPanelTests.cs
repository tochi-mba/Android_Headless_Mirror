using System.Windows;
using System.Windows.Automation;
using Rex.Core;
using Rex.Mirror.Views.Settings;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>The Sound on this PC group without a window: which rows can be changed, and what the values say.</summary>
public sealed class SoundPanelTests
{
    [Fact]
    public void TheSoundGroupFollowsItsParents()
    {
        Wpf.Run(() =>
        {
            var group = Wpf.Layout(new SoundGroup(), 360);
            var config = new RexConfig();
            group.Refresh(config);
            Assert.True(group.SoundOptions.IsEnabled);
            Assert.False(group.SoundLowerOptions.IsEnabled);
            Assert.Equal(Visibility.Collapsed, group.SoundOffNote.Visibility);
            Assert.True(group.SoundBalance.IsEnabled);
            Assert.Equal(string.Empty, AutomationProperties.GetHelpText(group.SoundBalance));

            config.Sound.LowerWhileTyping = true;
            group.Refresh(config);
            Assert.True(group.SoundLowerOptions.IsEnabled);

            // Phone sound off: every row stays in sight, cannot be changed, and the group says why.
            config.Mirror.Audio = false;
            group.Refresh(config);
            Assert.False(group.SoundOptions.IsEnabled);
            Assert.False(group.SoundLowerOptions.IsEnabled);
            Assert.Equal(Visibility.Visible, group.SoundOffNote.Visibility);
        });
    }

    [Fact]
    public void TheSoundRowsSayTheirValuesInWords()
    {
        Wpf.Run(() =>
        {
            var group = Wpf.Layout(new SoundGroup(), 360);
            var config = new RexConfig();
            config.Sound.Volume = 0.62;
            config.Sound.LowerTo = 1;
            config.Sound.FadeMs = 0;
            config.Sound.Balance = -0.5;
            config.Sound.LowerForMs = 1500;
            group.Refresh(config);

            Assert.Equal("62%", group.SoundLevelValue.Text);
            Assert.Equal("5%", group.SoundStepValue.Text);
            Assert.Equal("Not lowered at 100%", group.SoundLowerToValue.Text);
            Assert.Equal("1.5 s after the last key", group.SoundLowerForValue.Text);
            Assert.Equal("At once", group.SoundFadeValue.Text);
            Assert.Equal("50% left", group.SoundBalanceValue.Text);

            config.Sound.LowerTo = 0.3;
            config.Sound.FadeMs = 150;
            config.Sound.Balance = 0.25;
            group.Refresh(config);
            Assert.Equal("30% of your volume", group.SoundLowerToValue.Text);
            Assert.Equal("150 ms", group.SoundFadeValue.Text);
            Assert.Equal("25% right", group.SoundBalanceValue.Text);
            config.Sound.Balance = 0;
            group.Refresh(config);
            Assert.Equal("Centre", group.SoundBalanceValue.Text);
        });
    }

    [Fact]
    public void TheSoundPanelSaysTheLevelInWords()
    {
        Assert.Equal("62%", Rex.Mirror.Views.SoundPanel.Words(0.62, muted: false));
        Assert.Equal("Muted", Rex.Mirror.Views.SoundPanel.Words(0.62, muted: true));
        Assert.Equal("0%", Rex.Mirror.Views.SoundPanel.Words(0, muted: false));
    }
}
