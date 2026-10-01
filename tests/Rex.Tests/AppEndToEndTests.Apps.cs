using System.Text.RegularExpressions;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// The phone's apps across processes: the list read once the phone is mirrored and never while
/// another server starts, the command line, closing what was opened here, and favourites on the
/// fullscreen controls.
/// </summary>
public sealed partial class AppEndToEndTests
{
    private static async Task WaitForAppsAsync(AppProcess app) =>
        await app.WaitForStatusAsync(s => s["apps"]!["count"]!.GetValue<int>() == 14, StartupTimeout, "the phone's apps");

    [Fact(Timeout = 180_000)]
    public async Task Apps_ListIsReadOnceThePhoneIsMirroredAndNeverDuringACopyStart()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        var state = new StateStore(package.Paths.State);
        state.SetUi(state.Ui with { Copies = 1 });
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await WaitForAppsAsync(app);
        await app.WaitForStatusAsync(s => CopiesRunning(s) == 1, StartupTimeout, "the remembered copy");

        // Every server start, in the order the fake scrcpys logged it: from a session's arguments
        // to its window showing. Reading the list never falls inside one of those.
        var log = File.ReadAllLines(package.FakeScrcpyLog);
        var listing = log.Select((line, at) => (line, at)).Where(l => l.line.StartsWith("list-apps", StringComparison.Ordinal)).Select(l => l.at).ToArray();
        Assert.Equal(2, listing.Length);
        Assert.Contains("--no-cleanup", log[listing[0]], StringComparison.Ordinal);
        foreach (var (line, at) in log.Select((line, at) => (line, at)).Where(l => l.line.StartsWith("args ", StringComparison.Ordinal)))
        {
            var port = Regex.Match(line, "--port=(\\d+)").Groups[1].Value;
            var shown = Array.FindIndex(log, at, l => l == $"shown at={port}");
            Assert.True(shown > at, $"The session on port {port} never showed.");
            Assert.DoesNotContain(listing, l => l > at && l < shown);
        }

        // Once per connection: a restart of the mirror does not read it again.
        Assert.True((await app.SendAsync(new IpcRequest("session-restart"))).Ok);
        await app.WaitForStatusAsync(s => s["mirroring"]!.GetValue<bool>() && !s["restartRequired"]!.GetValue<bool>(), StartupTimeout, "the mirror again");
        Assert.Single(File.ReadAllLines(package.FakeScrcpyLog), l => l.StartsWith("list-apps start", StringComparison.Ordinal));
        await app.QuitAsync();
    }

    [Fact(Timeout = 180_000)]
    public async Task Apps_TheCommandLineListsAndOpens()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await WaitForAppsAsync(app);

        var listed = await app.RunCliAsync("app", "list", "spot");
        Assert.Contains("com.spotify.music", listed, StringComparison.Ordinal);
        Assert.DoesNotContain("com.example.one", listed, StringComparison.Ordinal);

        Assert.Contains("Opened Spotify.", await app.RunCliAsync("app", "open", "Spotify"), StringComparison.Ordinal);
        await app.WaitUntilAsync(() => package.AdbCalls().Any(c => c.Contains("am start -n com.spotify.music/.MainActivity", StringComparison.Ordinal)), SoundTimeout, "Spotify to open");

        Assert.Contains("More than one app is called \"Notes\"", await app.RunCliAsync("app", "open", "Notes"), StringComparison.Ordinal);
        Assert.Contains("Opened Example One.", await app.RunCliAsync("app", "open", "com.example.one", "--fresh"), StringComparison.Ordinal);
        await app.WaitUntilAsync(() => package.AdbCalls().Any(c => c.Contains("am start -S -n com.example.one/.MainActivity", StringComparison.Ordinal)), SoundTimeout, "a fresh start");

        // Starring through the running app shows at once.
        Assert.Contains("is a favourite", await app.RunCliAsync("app", "favourite", "com.example.two", "on"), StringComparison.Ordinal);
        await app.WaitForStatusAsync(s => s["apps"]!["favourites"]!.AsArray().Any(f => f!.GetValue<string>() == "com.example.two"), SoundTimeout, "the favourite");
        await app.QuitAsync();
    }

    [Fact(Timeout = 180_000)]
    public async Task Apps_ClosedWhenTheMirrorStopsWhenAsked()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true, configure: c => c.Apps.CloseWhenMirrorStops = true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await WaitForAppsAsync(app);

        Assert.True((await app.SendAsync(new IpcRequest("open-app", new Dictionary<string, string> { ["app"] = "Example One" }))).Ok);
        // A restart is not a stop: nothing is closed.
        Assert.True((await app.SendAsync(new IpcRequest("session-restart"))).Ok);
        await app.WaitForStatusAsync(s => s["mirroring"]!.GetValue<bool>(), StartupTimeout, "the mirror again");
        Assert.DoesNotContain(package.AdbCalls(), c => c.Contains("am force-stop", StringComparison.Ordinal));

        Assert.True((await app.SendAsync(new IpcRequest("session-stop"))).Ok);
        await app.WaitUntilAsync(() => package.AdbCalls().Any(c => c.Contains("am force-stop com.example.one", StringComparison.Ordinal)), SoundTimeout, "the app opened here to close");
        Assert.Single(package.AdbCalls(), c => c.Contains("am force-stop", StringComparison.Ordinal));
        await app.QuitAsync();
    }

    [Fact(Timeout = 180_000)]
    public async Task Apps_FavouritesShowOnTheFullscreenControlsWhenAsked()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true, configure: c => c.Apps.FavouritesInHud = true);
        new StateStore(package.Paths.State).SetFavourite("FAKE123", "com.example.one", true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await WaitForAppsAsync(app);

        await app.ActionAsync("fullscreen");
        await app.WaitForStatusAsync(s => s["fullscreen"]!.GetValue<bool>(), StartupTimeout, "fullscreen");
        app.MovePointerToCorner();
        app.Ui.Invoke("hud-app-com.example.one");
        await app.WaitUntilAsync(() => package.AdbCalls().Any(c => c.Contains("am start -n com.example.one/.MainActivity", StringComparison.Ordinal)), SoundTimeout, "the favourite to open");
        await app.QuitAsync();
    }
}
