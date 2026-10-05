using System.Windows.Automation;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>Two phones through real clicks: the phone menu's actions, the strip above the tabs, and the Second phone settings.</summary>
public sealed partial class AppUiTests
{
    /// <summary>A second fake phone, ready, of another make; both have a lock-screen answer.</summary>
    private static void WriteSecondPhone(TestPackage package)
    {
        package.WriteScenario(new
        {
            Devices = new object[]
            {
                new { Serial = "FAKE123", State = "device", Model = "SM-G998B" },
                new
                {
                    Serial = "FAKE456",
                    State = "device",
                    Model = "Pixel_7",
                    Properties = new Dictionary<string, string> { ["ro.product.marketname"] = "Pixel 7", ["ro.product.model"] = "Pixel 7", ["ro.product.manufacturer"] = "Google" },
                },
            },
            Properties = new Dictionary<string, string>
            {
                ["ro.product.manufacturer"] = "Samsung",
                ["ro.product.model"] = "SM-G998B",
                ["ro.product.marketname"] = "Galaxy S21 Ultra",
                ["ro.build.version.release"] = "15",
                ["ro.build.version.sdk"] = "35",
            },
        });
        var state = new StateStore(package.Paths.State);
        state.SetLockScreenMode("FAKE123", LockScreenModes.None);
        state.SetLockScreenMode("FAKE456", LockScreenModes.None);
    }

    private static void PickFromPhoneMenu(AppProcess app, string item)
    {
        app.Ui.Invoke("DeviceChip");
        var items = app.Ui.OpenMenuItems();
        var wanted = items.FirstOrDefault(i => i.Current.Name == item)
            ?? throw new InvalidOperationException($"No '{item}' in the phone menu: " + string.Join(" | ", items.Select(i => i.Current.Name)));
        ((InvokePattern)wanted.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
    }

    [Fact(Timeout = 150_000)]
    public async Task TwoPhones_ThePhoneMenuShowsUsesAndStopsThePhoneBeside()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true, configure: c => c.SecondPhone.WhenConnected = "never");
        WriteSecondPhone(package);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        Assert.False(app.Ui.Exists("PhoneStripName"));

        PickFromPhoneMenu(app, "Show Pixel 7 beside");
        await app.WaitForStatusAsync(s => s["phones"]!["beside"]?["state"]?.GetValue<string>() == "showing", Startup, "Pixel 7 beside");
        await app.WaitUntilAsync(() => app.Ui.Exists("PhoneStripName"), Soon, "the strip above the tabs");
        Assert.Equal("Galaxy S21 Ultra", app.Ui.Read("PhoneStripName", e => e.Name));
        Assert.Equal("Use Pixel 7", app.Ui.Read("PhoneStripSwitch", e => e.Name));
        await app.SaveScreenshotAsync("ui-two-phones.png");

        // The strip's button uses the other phone; the strip then names it.
        app.Ui.Invoke("PhoneStripSwitch");
        await app.WaitUntilAsync(() => app.Ui.Read("PhoneStripName", e => e.Name) == "Pixel 7", Soon, "the strip to follow the phone in use");
        Assert.Equal("Use Galaxy S21 Ultra", app.Ui.Read("PhoneStripSwitch", e => e.Name));

        PickFromPhoneMenu(app, "Stop showing Pixel 7 beside");
        await app.WaitForStatusAsync(s => s["phones"]!["beside"] is null, Soon, "the phone beside to go");
        await app.WaitUntilAsync(() => !app.Ui.Exists("PhoneStripName"), Soon, "the strip to go");
        Assert.Equal(string.Empty, new StateStore(package.Paths.State).Ui.SecondPhone);
        await app.QuitAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task Settings_SecondPhoneRowsSave()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        app.Ui.Select("TabSettings");
        app.Ui.ExpandGroup("GroupSecondPhone");
        SecondPhoneSettings Saved() => ConfigFile.Load(package.Paths.Config).SecondPhone;

        foreach (var (combo, item, check) in new (string, string, Func<SecondPhoneSettings, bool>)[]
                 {
                     ("SecondPhoneWhen", "Show it beside at once", s => s.WhenConnected == "always"),
                     ("SecondPhoneSide", "Left of the main phone", s => s.Side == "left"),
                     ("SecondPhoneSound", "The phone you are using", s => s.Sound == "active"),
                     ("SecondPhoneScreenOff", "On", s => s.ScreenOff == "on"),
                     ("SecondPhoneMaxSize", "1280 px", s => s.MaxSize == 1280),
                     ("SecondPhoneBitRate", "8 Mbps", s => s.BitRate == "8M"),
                 })
        {
            app.Ui.SelectComboItem(combo, item);
            await app.WaitUntilAsync(() => check(Saved()), Soon, combo + " to save");
        }

        foreach (var (id, on, check) in new (string, bool, Func<SecondPhoneSettings, bool>)[]
                 {
                     ("SecondPhoneRemember", false, s => !s.Remember),
                     ("SecondPhonePause", true, s => s.PauseWhenHidden),
                     ("SecondPhoneProfile", true, s => s.FollowsProfile),
                 })
        {
            app.Ui.Toggle(id, on);
            await app.WaitUntilAsync(() => check(Saved()), Soon, id + " to save");
        }

        // Off, every row below stays in sight but cannot be changed.
        app.Ui.Toggle("SecondPhoneEnabled", on: false);
        await app.WaitUntilAsync(() => !Saved().Enabled, Soon, "the second phone to be turned off");
        await app.WaitUntilAsync(() => !app.Ui.Read("SecondPhoneWhen", e => e.IsEnabled), Soon, "its rows to be disabled");
        await app.QuitAsync();
    }
}
