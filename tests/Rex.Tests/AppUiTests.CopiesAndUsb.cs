using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>The controls for copies of the phone and for USB recovery, driven through UI Automation.</summary>
public sealed partial class AppUiTests
{
    [Fact(Timeout = 75_000)]
    public async Task Controls_CopyButtonsAddAndRemoveACopyAndSayWhereTheyStand()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        await app.WaitUntilAsync(() => app.Ui.Read("CopyAdd", e => e.IsEnabled), Soon, "Add a copy to be offered");
        Assert.False(app.Ui.Read("CopyRemove", e => e.IsEnabled));

        app.Ui.Invoke("CopyAdd");
        await app.WaitForStatusAsync(s => s["copies"]!["running"]!.GetValue<int>() == 1, Startup, "the copy to open");
        await app.WaitUntilAsync(() => app.Ui.Read("CopiesStatus", e => e.Name).StartsWith("1 copy open", StringComparison.Ordinal), Soon, "the panel to say so");
        await app.WaitUntilAsync(() => app.Ui.Read("CopyRemove", e => e.IsEnabled), Soon, "Remove a copy to be offered");
        await app.SaveScreenshotAsync("ui-controls-copies.png");

        app.Ui.Invoke("CopyRemove");
        await app.WaitForStatusAsync(s => s["copies"]!["running"]!.GetValue<int>() == 0, Startup, "the copy to close");
        await app.WaitUntilAsync(() => !app.Ui.Read("CopyRemove", e => e.IsEnabled), Soon, "nothing left to remove");
        await app.QuitAsync();
    }

    [Fact(Timeout = 75_000)]
    public async Task Settings_CopiesAndUsbRepairControlsSave()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        app.Ui.Select("TabSettings");

        app.Ui.ExpandGroup("GroupCopies");
        app.Ui.SetValue("CopiesMost", 2);
        app.Ui.SetValue("CopiesGap", 24);
        app.Ui.SelectComboItem("CopiesMaxSize", "1024 px");
        app.Ui.Toggle("CopiesRemember", on: false);
        await app.WaitUntilAsync(() =>
        {
            var copies = ConfigFile.Load(package.Paths.Config).Copies;
            return copies.Most == 2 && Math.Abs(copies.Gap - 24) < 0.01 && copies.MaxSize == 1024 && !copies.Remember;
        }, Soon, "the copy settings saved");
        await app.SaveScreenshotAsync("ui-settings-copies.png");

        app.Ui.ExpandGroup("GroupAdvanced");
        Assert.Contains("administrator approval", app.Ui.Read("AutoRepairUsbState", e => e.Name), StringComparison.Ordinal);
        app.Ui.Toggle("AutoRepairUsb", on: false);
        await app.WaitUntilAsync(() => !ConfigFile.Load(package.Paths.Config).App.AutoRepairUsb, Soon, "USB auto-repair to be turned off");
        await app.WaitUntilAsync(() => app.Ui.Read("AutoRepairUsbState", e => e.Name).StartsWith("Off", StringComparison.Ordinal), Soon, "the row to say so");
        app.Ui.Toggle("AutoRepairUsb", on: true);
        await app.WaitUntilAsync(() => ConfigFile.Load(package.Paths.Config).App.AutoRepairUsb, Soon, "USB auto-repair back on");
        await app.SaveScreenshotAsync("ui-settings-usb.png");
        await app.QuitAsync();
    }
}
