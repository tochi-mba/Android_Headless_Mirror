using System.Text.RegularExpressions;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// The launch settings in the real app: each reaches the scrcpy session it belongs to, the main
/// one or every copy of the phone as well, and a recording is written in the chosen container.
/// </summary>
public sealed partial class AppEndToEndTests
{
    [Fact]
    public async Task LaunchSettings_ReachTheMainSessionAndTheCopiesTheyBelongTo()
    {
        using var package = new TestPackage(withFakeTools: true, configure: c =>
        {
            c.Mirror.VideoBufferMs = 100;
            c.Mirror.RenderDriver = "opengl";
            c.Mirror.AudioSource = "playback";
            c.Mirror.AudioDup = true;
            c.Mirror.RecordOnStart = true;
            c.Mirror.RecordFormat = "mkv";
            c.Session.ScreenOffTimeoutSeconds = 120;
            c.Session.KeepPcAwake = true;
            c.Session.StartApp = "com.example.player";
            c.Input.RightClick = "click";
            c.Input.ShiftClicks = false;
            c.Input.LegacyPaste = true;
            c.Input.Gamepad = "uhid";
        });
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);

        var main = Launches(package)[0];
        foreach (var expected in new[]
        {
            "--video-buffer=100", "--render-driver=opengl", "--audio-source=playback", "--audio-dup",
            "--screen-off-timeout=120", "--disable-screensaver", "--start-app=com.example.player",
            "--mouse-bind=+hsn", "--legacy-paste", "--gamepad=uhid",
        })
        {
            Assert.Contains(expected, main, StringComparison.Ordinal);
        }

        // scrcpy writes the container the file's extension names.
        Assert.Matches(new Regex(@"--record=\S+\.mkv(\s|$)"), main);

        await app.WaitForStatusAsync(s => Copies(s)["canAdd"]!.GetValue<bool>(), StartupTimeout, "room for a copy");
        await app.ActionAsync("copy-add");
        await app.WaitForStatusAsync(s => CopiesRunning(s) == 1, StartupTimeout, "the copy to open");
        var copy = Launches(package)[1];
        foreach (var same in new[] { "--video-buffer=100", "--render-driver=opengl", "--mouse-bind=+hsn", "--legacy-paste" })
        {
            Assert.Contains(same, copy, StringComparison.Ordinal);
        }

        foreach (var once in new[] { "--audio-source", "--audio-dup", "--screen-off-timeout", "--disable-screensaver", "--start-app", "--gamepad", "--record=" })
        {
            Assert.DoesNotContain(once, copy, StringComparison.Ordinal);
        }

        await app.QuitAsync();
    }
}
