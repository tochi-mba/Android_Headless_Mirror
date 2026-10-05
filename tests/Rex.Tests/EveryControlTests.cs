using Rex.Core;

namespace Rex.Tests;

/// <summary>What the last settings to get a control need: addresses typed in for phones on Wi-Fi, and what you changed and putting it back.</summary>
public sealed class EveryControlTests
{
    [Theory]
    [InlineData("192.168.1.20", "192.168.1.20:5555")]
    [InlineData(" 10.0.0.7:7000 ", "10.0.0.7:7000")]
    [InlineData("172.16.4.2", "172.16.4.2:5555")]
    [InlineData("172.31.255.1:1", "172.31.255.1:1")]
    [InlineData("169.254.3.9", "169.254.3.9:5555")]
    public void AnAddressOnThisNetworkIsTakenWithItsPort(string text, string address)
    {
        var (read, why) = WirelessAddress.Read(text, 5555);
        Assert.Equal(address, read);
        Assert.Null(why);
    }

    [Theory]
    [InlineData("", "Type the phone's address, like 192.168.1.20.")]
    [InlineData(null, "Type the phone's address, like 192.168.1.20.")]
    [InlineData("192.168.1.20:0", "The port is a number from 1 to 65535.")]
    [InlineData("192.168.1.20:70000", "The port is a number from 1 to 65535.")]
    [InlineData("192.168.1.20:port", "The port is a number from 1 to 65535.")]
    [InlineData("phone.local", "Use the phone's IPv4 address, like 192.168.1.20. Wireless debugging on the phone shows it.")]
    [InlineData("10.1", "Use the phone's IPv4 address, like 192.168.1.20. Wireless debugging on the phone shows it.")]
    [InlineData("8.8.8.8", "That address is not on a home or office network. Only addresses like 192.168.x.x, 10.x.x.x or 172.16-31.x.x are used.")]
    [InlineData("172.32.0.1", "That address is not on a home or office network. Only addresses like 192.168.x.x, 10.x.x.x or 172.16-31.x.x are used.")]
    [InlineData("192.169.1.1", "That address is not on a home or office network. Only addresses like 192.168.x.x, 10.x.x.x or 172.16-31.x.x are used.")]
    public void AnythingElseIsRefusedInWords(string? text, string why)
    {
        var (read, reason) = WirelessAddress.Read(text, 5555);
        Assert.Null(read);
        Assert.Equal(why, reason);
    }

    [Fact]
    public void WhatYouChangedIsWhatDiffersFromHowTheAppShips()
    {
        var config = new RexConfig();
        config.Mirror.MaxFps = 30;
        config.App.TopBarButtons = ["home"];
        var paths = new[] { "Mirror.MaxSize", "Mirror.MaxFps", "App.TopBarButtons", "Not.A.Setting", "Hud.X" };
        Assert.Equal(["Mirror.MaxFps", "App.TopBarButtons"], SettingsChanges.Of(config, paths));
        Assert.Empty(SettingsChanges.Of(new RexConfig(), paths));
    }

    [Fact]
    public void PuttingBackMovesOnlyWhatIsNamed()
    {
        var config = new RexConfig();
        config.Mirror.MaxFps = 30;
        config.Mirror.MaxSize = 1280;
        config.App.TopBarButtons = ["home"];
        var back = SettingsChanges.PutBack(config, ["Mirror.MaxFps", "App.TopBarButtons", "App.TopBarButtons", "Not.A.Setting"]);
        Assert.Equal(new RexConfig().Mirror.MaxFps, back.Mirror.MaxFps);
        Assert.Equal(new RexConfig().App.TopBarButtons, back.App.TopBarButtons);
        Assert.Equal(1280, back.Mirror.MaxSize);
        // The configuration it was given is left as it was.
        Assert.Equal(30, config.Mirror.MaxFps);
    }

    [Fact]
    public void EverySettingInTheTabCanBePutBack()
    {
        // Changing every setting the tab shows and putting them all back gives how the app ships.
        var shipped = new RexConfig();
        shipped.Normalize();
        var paths = SettingsCatalogue.Controls.Keys.ToArray();
        var changed = ConfigPaths.Apply(shipped, ConfigPaths.Values(shipped)
            .Where(pair => paths.Contains(pair.Key) && pair.Value.GetValueKind() is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
            .ToDictionary(pair => pair.Key, pair => (System.Text.Json.Nodes.JsonNode)System.Text.Json.Nodes.JsonValue.Create(!pair.Value.GetValue<bool>()))).Config;
        Assert.NotEmpty(SettingsChanges.Of(changed, paths));
        Assert.Empty(SettingsChanges.Of(SettingsChanges.PutBack(changed, paths), paths));
    }
}
