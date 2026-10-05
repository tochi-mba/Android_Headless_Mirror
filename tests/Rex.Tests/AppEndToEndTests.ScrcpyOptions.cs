using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// The scrcpy options that change how a mirror ends: a time limit stops it, as asked, instead of
/// starting it again; an encoder the phone does not have is named in words.
/// </summary>
public sealed partial class AppEndToEndTests
{
    [Fact]
    public async Task TimeLimit_StopsTheMirrorWithWordsAndDoesNotStartItAgain()
    {
        using var package = new TestPackage(withFakeTools: true, configure: c =>
        {
            c.Mirror.TimeLimitMinutes = 1;
            c.Session.RetrySeconds = 1;
        });
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        Assert.Contains("--time-limit=60", Launches(package)[0], StringComparison.Ordinal);

        var stopped = await app.WaitForPhaseAsync("stopped", TimeSpan.FromSeconds(90));
        Assert.Equal(MirrorTimeLimit.Stopped(1), stopped["message"]!.GetValue<string>());
        Assert.Contains("time-limit reached", package.ScrcpyLog());

        // A stop the person asked for: nothing starts it again until they press Start.
        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Single(Launches(package));
        await app.QuitAsync();
    }

    [Fact]
    public async Task AnEncoderThePhoneDoesNotHave_IsNamedInWords()
    {
        using var package = new TestPackage(withFakeTools: true, configure: c => c.Mirror.VideoEncoder = "c2.exynos.hevc.encoder");
        using var app = new AppProcess(package);
        var waiting = await app.WaitForStatusAsync(
            s => s["phase"]?.GetValue<string>() == "waiting" && s["message"]!.GetValue<string>().StartsWith("The phone has no video encoder", StringComparison.Ordinal),
            StartupTimeout, "the missing encoder to be named");
        Assert.Equal(
            "The phone has no video encoder called c2.exynos.hevc.encoder for H.264. Choose another in Settings, Picture, or let the phone choose.",
            waiting["message"]!.GetValue<string>());
        await app.QuitAsync();
    }
}
