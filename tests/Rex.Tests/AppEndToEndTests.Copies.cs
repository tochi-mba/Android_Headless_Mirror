using System.Text.Json.Nodes;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// Copies of the phone in the real app: each is a scrcpy session of its own beside the main one,
/// takes input itself, follows the main view's zoom, waits out of sight while the phone is on its
/// side, comes back after a restart, and is given up with a reason when the phone cannot hold it.
/// </summary>
public sealed partial class AppEndToEndTests
{
    private static JsonNode Copies(JsonObject status) => status["copies"]!;

    private static int CopiesRunning(JsonObject status) => Copies(status)["running"]!.GetValue<int>();

    private static string[] Launches(TestPackage package) =>
        package.ScrcpyLog().Where(line => line.StartsWith("args ", StringComparison.Ordinal)).ToArray();

    private static (int X, int Y) Centre(JsonNode view) =>
        (view["x"]!.GetValue<int>() + view["width"]!.GetValue<int>() / 2, view["y"]!.GetValue<int>() + view["height"]!.GetValue<int>() / 2);

    [Fact]
    public async Task Copies_OpenBesideThePhoneTakeInputFollowZoomAndClose()
    {
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        var before = await app.WaitForStatusAsync(s => Copies(s)["canAdd"]!.GetValue<bool>(), StartupTimeout, "room for a copy");
        Assert.Equal(0, Copies(before)["wanted"]!.GetValue<int>());

        await app.ActionAsync("copy-add");
        var status = await app.WaitForStatusAsync(s => CopiesRunning(s) == 1, StartupTimeout, "the copy to open");

        // A session of its own, on its own port, that leaves the phone's power and audio alone.
        var launches = Launches(package);
        Assert.Equal(2, launches.Length);
        Assert.Contains("--port=27183", launches[0], StringComparison.Ordinal);
        Assert.DoesNotContain("--no-cleanup", launches[0], StringComparison.Ordinal);
        foreach (var expected in new[] { "--port=27184", "--no-cleanup", "--no-power-on", "--no-audio", "copy 1" })
        {
            Assert.Contains(expected, launches[1], StringComparison.Ordinal);
        }

        Assert.DoesNotContain("--turn-screen-off", launches[1], StringComparison.Ordinal);

        // Side by side, the same size, both showing.
        var views = Copies(status)["views"]!.AsArray();
        Assert.Equal(2, views.Count);
        Assert.All(views, view => Assert.True(view!["shown"]!.GetValue<bool>()));
        Assert.True(views[1]!["x"]!.GetValue<int>() >= views[0]!["x"]!.GetValue<int>() + views[0]!["width"]!.GetValue<int>());
        Assert.InRange(views[1]!["width"]!.GetValue<int>(), views[0]!["width"]!.GetValue<int>() - 2, views[0]!["width"]!.GetValue<int>() + 2);
        Assert.InRange(views[1]!["height"]!.GetValue<int>(), views[0]!["height"]!.GetValue<int>() - 2, views[0]!["height"]!.GetValue<int>() + 2);
        await app.SaveScreenshotAsync("copies-two.png");

        // A click on the copy reaches the copy's own session.
        var copyCentre = Centre(views[1]!);
        await app.ClickAtAsync(copyCentre.X, copyCentre.Y);
        await app.WaitUntilAsync(() => package.ScrcpyLog().Any(line => line == "click at=27184"), TimeSpan.FromSeconds(10), "the click to reach the copy");

        // Alt + wheel over the copy zooms every view the same way.
        await app.AltWheelAtAsync(copyCentre.X, copyCentre.Y, 3);
        var zoomed = await app.WaitForStatusAsync(
            s => Copies(s)["views"]!.AsArray() is [var main, var copy] &&
                main!["zoom"]!.GetValue<double>() > 1 && main["zoom"]!.GetValue<double>() == copy!["zoom"]!.GetValue<double>(),
            TimeSpan.FromSeconds(10),
            "both views to zoom together");
        Assert.True(zoomed["zoom"]!.GetValue<double>() > 1);
        await app.SaveScreenshotAsync("copies-zoomed.png");

        // Removing the copy ends its session and nothing else.
        var copyProcess = Copies(zoomed)["processes"]![0]!.GetValue<int>();
        await app.ActionAsync("copy-remove");
        var removed = await app.WaitForStatusAsync(s => CopiesRunning(s) == 0 && Copies(s)["views"]!.AsArray().Count == 1, StartupTimeout, "the copy to close");
        await AppProcess.WaitForProcessExitAsync(copyProcess, TimeSpan.FromSeconds(10));
        Assert.True(removed["mirroring"]!.GetValue<bool>());
        Assert.False(Copies(removed)["canRemove"]!.GetValue<bool>());
        await app.QuitAsync();
        Assert.Equal(0, new StateStore(package.Paths.State).Ui.Copies);
    }

    [Fact]
    public async Task Copies_WaitOutOfSightWhileThePhoneIsOnItsSideAndComeBackAfterARestart()
    {
        using var package = new TestPackage(withFakeTools: true);
        using (var first = new AppProcess(package))
        {
            await first.WaitForPhaseAsync("mirroring", StartupTimeout);
            await first.WaitForStatusAsync(s => Copies(s)["canAdd"]!.GetValue<bool>(), StartupTimeout, "room for a copy");
            await first.ActionAsync("copy-add");
            await first.WaitForStatusAsync(s => CopiesRunning(s) == 1, StartupTimeout, "the copy to open");
            await first.QuitAsync();
        }

        // Remembered: the next run opens it again, after the main picture.
        Assert.Equal(1, new StateStore(package.Paths.State).Ui.Copies);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await app.WaitForStatusAsync(s => CopiesRunning(s) == 1, StartupTimeout, "the remembered copy");

        // On its side the phone fills the width alone; the copy keeps running, out of sight.
        await app.ActionAsync("rotation-landscape");
        var sideways = await app.WaitForStatusAsync(
            s => Copies(s)["shown"]!.GetValue<int>() == 1 && s["surface"]!["width"]!.GetValue<double>() > s["surface"]!["height"]!.GetValue<double>(),
            StartupTimeout,
            "the copy to step aside");
        Assert.Equal(1, CopiesRunning(sideways));
        Assert.False(Copies(sideways)["views"]![1]!["shown"]!.GetValue<bool>());
        Assert.Contains("out of sight", Copies(sideways)["summary"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.False(Copies(sideways)["canAdd"]!.GetValue<bool>());
        await app.SaveScreenshotAsync("copies-landscape.png");

        await app.ActionAsync("rotation-portrait");
        await app.WaitForStatusAsync(
            s => Copies(s)["shown"]!.GetValue<int>() == 2 && Copies(s)["views"]![1]!["shown"]!.GetValue<bool>(),
            StartupTimeout,
            "the copy to come back");
        await app.QuitAsync();
    }

    [Fact]
    public async Task Copies_PauseAndTurnWithTheMainViewAndOpenShowingItTheSameWay()
    {
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await app.WaitForStatusAsync(s => Copies(s)["canAdd"]!.GetValue<bool>(), StartupTimeout, "room for a copy");

        // Turned before the copy opens: the copy starts turned the same way.
        await app.ActionAsync("rotate-right");
        await app.WaitForStatusAsync(s => s["view"]!["orientation"]!.GetValue<string>() == "90", TimeSpan.FromSeconds(5), "the turn to be followed");
        await app.ActionAsync("copy-add");
        await app.WaitForStatusAsync(s => CopiesRunning(s) == 1, StartupTimeout, "the copy to open");
        Assert.EndsWith("--display-orientation=90", Launches(package)[1], StringComparison.Ordinal);

        // What changes the picture on this PC reaches the copy's own session as well.
        bool CopyGot(int virtualKey) => package.ScrcpyLog().Any(line =>
            line.StartsWith($"key vk={virtualKey} ", StringComparison.Ordinal) && line.EndsWith("at=27184", StringComparison.Ordinal));
        await app.ActionAsync("rotate-left");
        await app.WaitUntilAsync(() => CopyGot(0x25), TimeSpan.FromSeconds(10), "the turn to reach the copy");
        await app.ActionAsync("pause");
        await app.WaitUntilAsync(() => CopyGot('Z'), TimeSpan.FromSeconds(10), "the pause to reach the copy");
        var paused = await app.SendAsync(new IpcRequest("status"));
        Assert.True(paused.Data!["view"]!["paused"]!.GetValue<bool>());
        Assert.Equal("0", paused.Data["view"]!["orientation"]!.GetValue<string>());

        // What acts on the phone happens once, through the main session.
        await app.ActionAsync("sleep");
        await app.WaitUntilAsync(
            () => package.ScrcpyLog().Any(line => line.StartsWith("key vk=79 ", StringComparison.Ordinal) && line.EndsWith("at=27183", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(10),
            "the main session to turn the screen off");
        Assert.False(CopyGot('O'));

        // A copy opened while the picture is paused starts paused, and upright again.
        var pausesBefore = package.ScrcpyLog().Count(line => line.StartsWith("key vk=90 ", StringComparison.Ordinal) && line.EndsWith("at=27184", StringComparison.Ordinal));
        await app.ActionAsync("copy-remove");
        await app.WaitForStatusAsync(s => CopiesRunning(s) == 0, StartupTimeout, "the copy to close");
        await app.ActionAsync("copy-add");
        await app.WaitForStatusAsync(s => CopiesRunning(s) == 1, StartupTimeout, "the copy to open again");
        Assert.EndsWith("--display-orientation=0", Launches(package)[^1], StringComparison.Ordinal);
        await app.WaitUntilAsync(
            () => package.ScrcpyLog().Count(line => line.StartsWith("key vk=90 ", StringComparison.Ordinal) && line.EndsWith("at=27184", StringComparison.Ordinal)) > pausesBefore,
            TimeSpan.FromSeconds(10),
            "the new copy to be paused");

        await app.ActionAsync("resume");
        await app.WaitForStatusAsync(s => !s["view"]!["paused"]!.GetValue<bool>(), TimeSpan.FromSeconds(5), "the picture to play again");
        await app.QuitAsync();
    }

    [Fact]
    public async Task Copies_ThatThePhoneCannotHoldAreGivenUpWithTheReason()
    {
        using var package = new TestPackage(withFakeTools: true);
        File.WriteAllText(package.FailCopiesMarker, string.Empty);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await app.WaitForStatusAsync(s => Copies(s)["canAdd"]!.GetValue<bool>(), StartupTimeout, "room for a copy");

        await app.ActionAsync("copy-add");

        // Tried twice, then given up; the main picture is never disturbed.
        var gaveUp = await app.WaitForStatusAsync(
            s => Copies(s)["wanted"]!.GetValue<int>() == 0 && CopiesRunning(s) == 0 && !Copies(s)["starting"]!.GetValue<bool>(),
            TimeSpan.FromSeconds(60),
            "the copy to be given up");
        Assert.True(gaveUp["mirroring"]!.GetValue<bool>());
        Assert.Equal(2, Launches(package).Count(line => line.Contains("--no-cleanup", StringComparison.Ordinal)));
        await app.QuitAsync();
        Assert.Contains("run out of video encoders", File.ReadAllText(package.Paths.LogFile), StringComparison.Ordinal);
    }
}
