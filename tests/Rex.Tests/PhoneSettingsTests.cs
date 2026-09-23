using Rex.Core;

namespace Rex.Tests;

/// <summary>The catalogue is data: every entry must be complete, unique and safely editable.</summary>
public sealed class PhoneSettingsTests
{
    [Fact]
    public void EveryEntryIsCompleteAndUnique()
    {
        Assert.Equal(PhoneSettings.All.Count, PhoneSettings.Ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(PhoneSettings.All, setting =>
        {
            Assert.Matches("^[a-z0-9-]+$", setting.Id);
            Assert.Contains(setting.Group, PhoneSettings.Groups);
            Assert.False(string.IsNullOrWhiteSpace(setting.Label), setting.Id);
            Assert.EndsWith(".", setting.Description, StringComparison.Ordinal);
            Assert.Contains(setting.Namespace, AndroidSettings.Namespaces);
            Assert.True(AndroidSettings.IsValidKey(setting.Key) || setting.Key.Length == 0, setting.Id);
            Assert.NotEqual(AndroidSettings.RiskProtected, setting.Risk);

            switch (setting.Kind)
            {
                case PhoneSettingKind.Choice:
                    Assert.NotEmpty(setting.Choices);
                    Assert.Equal(setting.Choices.Count, setting.Choices.Select(c => c.Value).Distinct(StringComparer.Ordinal).Count());
                    break;
                case PhoneSettingKind.Slider:
                    Assert.True(setting.Maximum > setting.Minimum, setting.Id);
                    Assert.True(setting.Step > 0, setting.Id);
                    break;
                case PhoneSettingKind.Toggle:
                    Assert.NotEqual(setting.OnValue, setting.OffValue);
                    break;
            }
        });

        // A provider-backed setting must name a key, and the others must not pretend to have one.
        Assert.All(PhoneSettings.All.Where(s => s.Source == PhoneSettingSource.SettingsProvider),
            setting => Assert.NotEqual(string.Empty, setting.Key));
        Assert.All(PhoneSettings.Groups, group => Assert.Contains(PhoneSettings.All, s => s.Group == group));
    }

    [Fact]
    public void ValidationNormalisesWhatItAccepts()
    {
        var brightness = PhoneSettings.Find("brightness")!;
        Assert.Equal("200", PhoneSettings.Validate(brightness, " 200 ").Text);
        Assert.Equal("1", PhoneSettings.Validate(brightness, "1.4").Text);
        Assert.False(PhoneSettings.Validate(brightness, "0").Ok);
        Assert.False(PhoneSettings.Validate(brightness, "x").Ok);

        var timeout = PhoneSettings.Find("screen-timeout")!;
        Assert.True(PhoneSettings.Validate(timeout, "300000").Ok);
        Assert.False(PhoneSettings.Validate(timeout, "7").Ok);

        var touches = PhoneSettings.Find("show-touches")!;
        Assert.True(PhoneSettings.Validate(touches, "1").Ok);
        Assert.False(PhoneSettings.Validate(touches, "on").Ok);

        var warmth = PhoneSettings.Find("night-light-temperature")!;
        Assert.True(PhoneSettings.Validate(warmth, "3000").Ok);
        Assert.False(PhoneSettings.Validate(warmth, "9000").Ok);

        var size = PhoneSettings.Find("display-size")!;
        Assert.True(PhoneSettings.Validate(size, "1080x2400").Ok);
        Assert.True(PhoneSettings.Validate(size, "reset").Ok);
        Assert.False(PhoneSettings.Validate(size, "1080").Ok);

        var density = PhoneSettings.Find("display-density")!;
        Assert.True(PhoneSettings.Validate(density, "420").Ok);
        Assert.False(PhoneSettings.Validate(density, "10").Ok);

        var message = PhoneSettings.Find("lock-screen-owner-info")!;
        Assert.True(PhoneSettings.Validate(message, "If found, call 555").Ok);
        Assert.False(PhoneSettings.Validate(message, new string('x', 201)).Ok);
    }

    [Fact]
    public void ValuesReadBackInPlainWords()
    {
        Assert.Equal("On", PhoneSettings.Find("show-touches")!.Describe("1"));
        Assert.Equal("Off", PhoneSettings.Find("show-touches")!.Describe("0"));
        Assert.Equal("5 minutes", PhoneSettings.Find("screen-timeout")!.Describe("300000"));
        Assert.Equal("3000 K", PhoneSettings.Find("night-light-temperature")!.Describe("3000"));
        Assert.Equal("default", PhoneSettings.Find("brightness")!.Describe(""));
        // A value the phone holds that the catalogue does not list is shown as it is.
        Assert.Equal("99999", PhoneSettings.Find("screen-timeout")!.Describe("99999"));
    }

    [Fact]
    public void ProbesAreParsedIntoValues()
    {
        Assert.Equal("yes", PhoneSettings.ParseProbe("dark-mode", "Night mode: yes"));
        Assert.Equal("no", PhoneSettings.ParseProbe("dark-mode", "Night mode: no"));
        Assert.Equal("auto", PhoneSettings.ParseProbe("dark-mode", "Night mode: auto"));
        Assert.Equal(string.Empty, PhoneSettings.ParseProbe("dark-mode", "error"));
        Assert.Equal("1080x2400", PhoneSettings.ParseProbe("display-size", "Physical size: 1440x3200 Override size: 1080x2400 "));
        Assert.Equal("420", PhoneSettings.ParseProbe("display-density", "Physical density: 560 Override density: 420"));
    }

    [Fact]
    public void WriteCommandsMatchTheSource()
    {
        Assert.Equal(["settings", "put", "system", "screen_brightness", "200"],
            PhoneSettings.WriteCommand(PhoneSettings.Find("brightness")!, "200"));
        Assert.Equal(["svc", "data", "disable"], PhoneSettings.WriteCommand(PhoneSettings.Find("mobile-data")!, "0"));
        Assert.Equal(["cmd", "connectivity", "airplane-mode", "enable"], PhoneSettings.WriteCommand(PhoneSettings.Find("airplane-mode")!, "1"));
        Assert.Equal(["wm", "density", "420"], PhoneSettings.WriteCommand(PhoneSettings.Find("display-density")!, "420"));
        Assert.Contains("transition_animation_scale", PhoneSettings.WriteCommand(PhoneSettings.Find("animation-scale")!, "0")[^1]);
    }

    [Fact]
    public void RiskFollowsTheNamespaceUnlessTheEntrySaysOtherwise()
    {
        Assert.Equal(AndroidSettings.RiskNormal, PhoneSettings.Find("brightness")!.Risk);
        Assert.Equal(AndroidSettings.RiskAdvanced, PhoneSettings.Find("night-light")!.Risk);
        Assert.Equal(AndroidSettings.RiskSensitive, PhoneSettings.Find("navigation-mode")!.Risk);
        Assert.Equal(AndroidSettings.RiskSensitive, PhoneSettings.Find("airplane-mode")!.Risk);
    }
}
