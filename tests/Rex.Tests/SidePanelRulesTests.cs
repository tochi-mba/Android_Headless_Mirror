using Rex.Core;
using Rex.Mirror.Views;

namespace Rex.Tests;

/// <summary>
/// The small rules behind the Phone and Info tabs, without a window: when a row offers Reset,
/// which risk labels a raw key shows, when the wheel belongs to the panel rather than the key
/// list, and how the tab shortcuts are described.
/// </summary>
public sealed class SidePanelRulesTests
{
    [Fact]
    public void AnOrdinaryKeyShowsNoRiskLabelSoTheOthersStandOut()
    {
        Assert.Equal(string.Empty, new AndroidSettingRow("system", "screen_brightness", "128", AndroidSettings.RiskNormal).RiskTag);
        Assert.Equal(AndroidSettings.RiskAdvanced, new AndroidSettingRow("global", "x", "1", AndroidSettings.RiskAdvanced).RiskTag);
        Assert.Equal(AndroidSettings.RiskProtected, new AndroidSettingRow("global", "adb_enabled", "1", AndroidSettings.RiskProtected).RiskTag);
    }

    [Theory]
    // At the top, turning up is for the panel; at the bottom, turning down is.
    [InlineData(120, 0, 500, true)]
    [InlineData(-120, 500, 500, true)]
    // Anywhere the list can still move that way, it takes the wheel.
    [InlineData(-120, 0, 500, false)]
    [InlineData(120, 200, 500, false)]
    [InlineData(-120, 200, 500, false)]
    // A list with nothing to scroll never holds the wheel.
    [InlineData(-120, 0, 0, true)]
    public void TheKeyListHandsTheWheelBackAtEitherEnd(int delta, double offset, double scrollable, bool passes) =>
        Assert.Equal(passes, AdvancedSettingsView.WheelPassesThrough(delta, offset, scrollable));

    [Fact]
    public void OnlyAChangedSettingThatCanGoBackOffersReset()
    {
        var resettable = PhoneSettings.All.First(s => s.CanReset);
        Assert.True(PhonePanel.ShowsReset(new PhoneSettingValue(resettable, "1")));
        Assert.False(PhonePanel.ShowsReset(new PhoneSettingValue(resettable, string.Empty)));
        if (PhoneSettings.All.FirstOrDefault(s => !s.CanReset) is { } fixedSetting)
        {
            Assert.False(PhonePanel.ShowsReset(new PhoneSettingValue(fixedSetting, "1")));
        }
    }

    [Fact]
    public void TabShortcutsNameTheTabsAsTheyAreLabelled()
    {
        foreach (var (id, tab) in new[] { ("tab-controls", "Controls"), ("tab-apps", "Apps"), ("tab-phone", "Phone"), ("tab-settings", "Settings"), ("tab-info", "Info") })
        {
            Assert.Equal($"The {tab} tab", Shortcuts.All.Single(s => s.Id == id).Description);
        }
    }
}
