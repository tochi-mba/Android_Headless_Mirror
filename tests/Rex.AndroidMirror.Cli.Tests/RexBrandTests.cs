using Rex.AndroidMirror.Cli;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Rex.AndroidMirror.Cli.Tests;

public sealed class RexBrandTests
{
    [Fact]
    public void Header_RendersRexIdentityAndSubtitle()
    {
        var console = new TestConsole();
        console.Profile.Width = 120;

        RexBrand.Header(console, "FIRST RUN");

        Assert.Contains("REX", console.Output);
        Assert.Contains("TECHNOLOGIES", console.Output);
        Assert.Contains("FIRST RUN", console.Output);
    }

    [Fact]
    public void Success_EscapesUserControlledMarkup()
    {
        var console = new TestConsole();

        RexBrand.Success(console, "saved [red]literal[/]");

        Assert.Contains("saved [red]literal[/]", console.Output);
    }

    [Fact]
    public void Warn_EscapesUserControlledMarkup()
    {
        var console = new TestConsole();

        RexBrand.Warn(console, "device [blue]USB[/]");

        Assert.Contains("device [blue]USB[/]", console.Output);
    }

    [Fact]
    public void Error_EscapesUserControlledMarkup()
    {
        var console = new TestConsole();

        RexBrand.Error(console, "boom [/]");

        Assert.Contains("boom [/]", console.Output);
    }

    [Theory]
    [InlineData("adb_enabled = 1 [protected]")]
    [InlineData("Wireless.ManualHosts = []")]
    [InlineData("value [red]is data[/], not markup")]
    public void Menu_EscapesDynamicChoiceMarkup(string choice)
    {
        var console = new TestConsole();
        console.Profile.Capabilities.Interactive = true;
        console.Profile.Width = 120;
        console.Input.PushKey(ConsoleKey.Enter);

        var selected = console.Prompt(RexBrand.Menu("Choose setting", new[] { choice }));

        Assert.Equal(choice, selected);
        Assert.Contains(choice, console.Output);
    }

    [Fact]
    public void DeviceLabel_ContainsIdentityTransportAndState()
    {
        var usb = new RexDevice("ABC", "device", false, "Samsung", "S24", "Samsung S24");
        var wifi = new RexDevice("192.168.1.9:5555", "device", true, "Google", "Pixel", "Google Pixel");

        Assert.Contains("USB", RexBrand.DeviceLabel(usb));
        Assert.Contains("wireless", RexBrand.DeviceLabel(wifi));
        Assert.Contains("ABC", RexBrand.DeviceLabel(usb));
        Assert.Contains("device", RexBrand.DeviceLabel(usb));
    }

    [Theory]
    [InlineData(true, "running")]
    [InlineData(false, "stopped")]
    public void State_RendersCorrectBranch(bool value, string expected)
    {
        var rendered = RexBrand.State(value, "running", "stopped");
        Assert.Contains(expected, rendered);
    }
}
