using System.Text.Json.Nodes;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// "USB device not recognised" in the real app: the notice, the two fixes it offers, and the
/// automatic repair with its limits. Windows is played by the test package: the failed device
/// comes from a file, and Task Scheduler and the administrator prompt are a log the app writes.
/// </summary>
public sealed partial class AppEndToEndTests
{
    private static readonly TimeSpan UsbNoticeTimeout = TimeSpan.FromSeconds(25);

    private static object FailedUsbDevice => new
    {
        instanceId = @"USB\VID_0000&PID_0002\5&1F007347&0&5",
        description = "Unknown USB Device (Device Descriptor Request Failed)",
        hardwareIds = new[] { @"USB\DEVICE_DESCRIPTOR_FAILURE" },
        problemCode = 43,
    };

    private static string UsbNotice(JsonObject status) => status["usb"]!["notice"]!.GetValue<string>();

    [Fact]
    public async Task UsbNotRecognised_IsOfferedThenRepairedAutomaticallyOnceAndClearsWhenThePhoneComesBack()
    {
        using var package = new TestPackage(withFakeTools: true);
        package.WriteScenario(new { Devices = Array.Empty<object>() });
        package.WriteUsbProblems(FailedUsbDevice);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("waiting", StartupTimeout);

        // Nothing is set up yet, so nothing happens by itself: both fixes are offered.
        var offered = await app.WaitForStatusAsync(s => UsbNotice(s) == "offer", UsbNoticeTimeout, "the USB notice");
        Assert.Equal("not-installed", offered["usb"]!["autoRepair"]!.GetValue<string>());
        Assert.Equal(@"USB\VID_0000&PID_0002\5&1F007347&0&5", offered["usb"]!["problems"]![0]!.GetValue<string>());
        Assert.True(app.Ui.Exists("NoticeUsbRepair"));
        Assert.True(app.Ui.Exists("NoticeUsbAuto"));
        await app.SaveScreenshotAsync("usb-not-recognised.png");
        Assert.DoesNotContain(package.UsbRepairCalls(), line => line.StartsWith("run ", StringComparison.Ordinal));

        // "Fix automatically from now on": one prompt sets the task up, and the app starts it.
        app.Ui.Invoke("NoticeUsbAuto");
        var fixing = await app.WaitForStatusAsync(
            s => UsbNotice(s) == "fixing" && s["usb"]!["automatic"]!.GetValue<bool>(), UsbNoticeTimeout, "the automatic repair");
        Assert.Equal("installed", fixing["usb"]!["autoRepair"]!.GetValue<string>());
        await app.WaitUntilAsync(() => package.UsbRepairCalls().Any(line => line == @"run \REX\USB auto-repair"), UsbNoticeTimeout, "the task to be started");
        var calls = package.UsbRepairCalls();
        Assert.Equal("elevate usb enable-auto-repair", calls[0]);
        Assert.StartsWith(@"register \REX\USB auto-repair ", calls[1], StringComparison.Ordinal);
        await app.SaveScreenshotAsync("usb-fixing.png");

        // The device is still failing, but a repair is at most once every 90 seconds.
        var checks = (await app.SendAsync(new IpcRequest("status"))).Data!["usb"]!["checks"]!.GetValue<int>();
        await app.WaitForStatusAsync(s => s["usb"]!["checks"]!.GetValue<int>() >= checks + 3, UsbNoticeTimeout, "three more looks");
        Assert.Single(package.UsbRepairCalls(), line => line.StartsWith("run ", StringComparison.Ordinal));

        // The phone comes back: the notice goes, whatever Windows reported a moment ago.
        package.WriteUsbProblems();
        File.Delete(package.FakeAdbScenario);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        var done = await app.WaitForStatusAsync(s => UsbNotice(s) == "none", UsbNoticeTimeout, "the notice to clear");
        Assert.Equal(0, done["usb"]!["attempts"]!.GetValue<int>());
        await app.QuitAsync();
    }

    [Fact]
    public async Task UsbNotRecognised_TheFixAsksForApprovalAndHideSilencesTheNotice()
    {
        using var package = new TestPackage(withFakeTools: true, configure: c => c.App.AutoRepairUsb = false);
        package.WriteScenario(new { Devices = Array.Empty<object>() });
        package.WriteUsbProblems(FailedUsbDevice);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("waiting", StartupTimeout);
        await app.WaitForStatusAsync(s => UsbNotice(s) == "offer", UsbNoticeTimeout, "the USB notice");

        // "Fix USB" is the prompted repair; with the switch off, nothing is started by itself.
        app.Ui.Invoke("NoticeUsbRepair");
        await app.WaitUntilAsync(() => package.UsbRepairCalls().Contains("elevate usb repair"), UsbNoticeTimeout, "the prompted repair");
        var afterRepair = await app.WaitForStatusAsync(s => s["usb"]!["attempts"]!.GetValue<int>() == 1, UsbNoticeTimeout, "the attempt to count");
        Assert.False(afterRepair["usb"]!["automatic"]!.GetValue<bool>());
        Assert.DoesNotContain(package.UsbRepairCalls(), line => line.StartsWith("run ", StringComparison.Ordinal));

        // Hide: nothing more is said about this spell, though Windows still reports the device.
        app.Ui.Invoke("NoticeUsbHide");
        var hidden = await app.WaitForStatusAsync(s => UsbNotice(s) == "none", UsbNoticeTimeout, "the notice to hide");
        Assert.Single(hidden["usb"]!["problems"]!.AsArray());
        await app.QuitAsync();
    }
}
