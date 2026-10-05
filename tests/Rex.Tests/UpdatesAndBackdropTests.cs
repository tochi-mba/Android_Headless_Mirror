using Rex.Core;

namespace Rex.Tests;

/// <summary>
/// The optional check for a newer version (when it is due, what GitHub's answer says, what is worth
/// offering), the black backdrop, and the fullscreen controls that stay up or show in the window.
/// </summary>
public sealed class UpdatesAndBackdropTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false, null, false)]
    [InlineData(true, null, true)]
    [InlineData(true, -23.0, false)]
    [InlineData(true, -24.0, true)]
    [InlineData(true, -48.0, true)]
    [InlineData(true, 5.0, true)]
    [InlineData(false, -48.0, false)]
    public void ACheckIsDueOnlyWhenOnAndAtMostOnceADay(bool enabled, double? hoursAgo, bool due) =>
        Assert.Equal(due, UpdateCheck.Due(enabled, hoursAgo is { } h ? Now.AddHours(h) : null, Now));

    [Theory]
    [InlineData("""{"tag_name":"v2.15.0","name":"x"}""", "2.15.0")]
    [InlineData("""{"tag_name":"2.16.1"}""", "2.16.1")]
    [InlineData("""{"tag_name":"V3.0"}""", "3.0")]
    [InlineData("""{"tag_name":"nightly"}""", null)]
    [InlineData("""{"tag_name":7}""", null)]
    [InlineData("""{"name":"v2.15.0"}""", null)]
    [InlineData("""["v2.15.0"]""", null)]
    [InlineData("not json", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void GitHubsAnswerIsReadForItsVersion(string? json, string? version) =>
        Assert.Equal(version is null ? null : Version.Parse(version), UpdateCheck.Latest(json));

    [Theory]
    [InlineData("2.15.0", "2.14.0", "", true)]
    [InlineData("2.14.0", "2.14.0", "", false)]
    [InlineData("2.13.0", "2.14.0", "", false)]
    [InlineData("2.15.0", "2.14.0", "2.15.0", false)]
    [InlineData("2.16.0", "2.14.0", "2.15.0", true)]
    [InlineData("2.15.0", "not a version", "", false)]
    [InlineData(null, "2.14.0", "", false)]
    public void OnlyANewerVersionThatWasNotSkippedIsOffered(string? latest, string current, string skipped, bool offered) =>
        Assert.Equal(offered, UpdateCheck.Offer(latest is null ? null : Version.Parse(latest), current, skipped));

    [Fact]
    public void ANewVersionIsDownloadedFromItsOwnReleasePage() =>
        Assert.Equal("https://github.com/tochi-mba/Android_Headless_Mirror/releases/tag/v2.15.0", UpdateCheck.ReleasePage(new Version(2, 15, 0)));

    [Fact]
    public void TheCheckIsOffUnlessTurnedOn()
    {
        Assert.False(new RexConfig().App.CheckForUpdates);
        Assert.False(UpdateCheck.Due(new RexConfig().App.CheckForUpdates, null, Now));
    }

    [Fact]
    public void ABlackBackdropReachesScrcpysOwnEdges()
    {
        var config = new RexConfig();
        Assert.Contains("--background-color=" + ScrcpyArguments.LetterboxColour, ScrcpyArguments.Build(config, "S", false, "T", null, null));
        config.Mirror.Backdrop = "black";
        Assert.Contains("--background-color=#000000", ScrcpyArguments.Build(config, "S", false, "T", null, null));
        Assert.Contains("--background-color=#000000", ScrcpyArguments.Build(config, "S", false, "T", null, null, copyIndex: 0));
        Assert.True(SettingsCatalogue.AppliesAtNextStart("Mirror.Backdrop"));
        Assert.Equal(ScrcpyArguments.LetterboxColour, ScrcpyArguments.LetterboxFor("ink"));
    }

    [Fact]
    public void TheNewValuesAreKeptSane()
    {
        var config = new RexConfig();
        config.Mirror.Backdrop = "white";
        config.Normalize();
        Assert.Equal("ink", config.Mirror.Backdrop);
        config.Mirror.Backdrop = "BLACK";
        config.Normalize();
        Assert.Equal("black", config.Mirror.Backdrop);

        var hud = new RexConfig().Hud;
        Assert.True(hud.AutoHide);
        Assert.False(hud.ShowInWindow);
        Assert.False(SettingsCatalogue.AppliesAtNextStart("Hud.AutoHide"));
        Assert.False(SettingsCatalogue.AppliesAtNextStart("App.CheckForUpdates"));
    }
}
