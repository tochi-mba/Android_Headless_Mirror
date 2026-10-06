using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// Your own keys through the Settings tab, with real keys pressed into the boxes: a key moved and
/// saved, one another action has refused in words, put back, and a browse key of one's own.
/// </summary>
public sealed partial class AppUiTests
{
    private const byte J = 0x4A, K = 0x4B, S = 0x53;

    [Fact(Timeout = 150_000)]
    public async Task Keys_RecordYourOwnPutBackAndRefuseATakenOne()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = await OpenSettingsAsync(package, "GroupKeys");
        await app.FocusAsync();
        RexConfig Saved() => ConfigFile.Load(package.Paths.Config);

        Assert.Equal("Ctrl+Alt+H", app.Ui.TextOf("key-home"));
        Assert.False(app.Ui.Exists("put-back-key-home"));
        app.Ui.Focus("key-home");
        await app.PressChordAsync(Ctrl, Alt, J);
        await app.WaitUntilAsync(() => Saved().Keys.Window.Any(b => b is { Action: "home", Key: "Ctrl+Alt+J" }), Soon, "home's new key to save");
        await app.WaitUntilAsync(() => app.Ui.Exists("put-back-key-home"), Soon, "Put back to offer itself");

        // A key another action has is refused under its box; the box keeps the key it had.
        app.Ui.Focus("key-back");
        await app.PressChordAsync(Ctrl, Alt, S);
        await app.WaitUntilAsync(() => app.Ui.Exists("why-key-back"), Soon, "the refusal");
        Assert.Equal("This key already does: Screenshot.", app.Ui.Read("why-key-back", e => e.Name));
        Assert.Equal("Ctrl+Alt+Backspace", app.Ui.TextOf("key-back"));
        Assert.DoesNotContain(Saved().Keys.Window, b => b.Action == "back");
        await app.SaveScreenshotAsync("ui-settings-keys.png");

        // In browse mode, a single key.
        app.Ui.Focus("browse-key-like");
        await app.PressChordAsync(K);
        await app.WaitUntilAsync(() => Saved().Keys.Browse.Any(b => b is { Action: "like", Key: "K" }), Soon, "like's browse key to save");

        app.Ui.Invoke("put-back-key-home");
        await app.WaitUntilAsync(() => Saved().Keys.Window.All(b => b.Action != "home"), Soon, "home's key to go back");
        Assert.Equal("Ctrl+Alt+H", app.Ui.TextOf("key-home"));

        app.Ui.Invoke("KeysPutAllBack");
        await app.WaitUntilAsync(() => Saved().Keys is { Window.Count: 0, Browse.Count: 0 }, Soon, "every key to go back");
        await app.QuitAsync();
    }
}
