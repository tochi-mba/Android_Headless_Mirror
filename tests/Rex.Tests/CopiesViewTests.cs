using System.Windows.Input;
using Rex.Core;
using Rex.Mirror.Mirror;
using Rex.Mirror.Session;
using Rex.Mirror.Views;

namespace Rex.Tests;

/// <summary>The app-side rules behind copies and the Phone tab that need no window to check.</summary>
public sealed class CopiesViewTests
{
    [Fact]
    public void ACopyFollowsTheMainViewsZoomInProportion()
    {
        // The main view is zoomed 2x and panned; a copy half as wide and as tall shows the same part.
        var main = new ZoomView(2, -100, -300, 800, 1600);
        var copy = MirrorHost.FollowedView(main, 400, 800, 200, 400);
        Assert.Equal(new ZoomView(2, -50, -150, 400, 800), copy);

        // Views of the same size show exactly the same thing.
        Assert.Equal(main, MirrorHost.FollowedView(main, 400, 800, 400, 800));
    }

    [Theory]
    [InlineData("[server] ERROR: Could not create default video encoder for h264", "run out of video encoders")]
    [InlineData("[server] ERROR: Exception on thread Thread[video,5,main]", "run out of video encoders")]
    [InlineData("ERROR: Failed to initialize video", "run out of video encoders")]
    [InlineData("ERROR: Server connection failed", "could not connect")]
    [InlineData("java.lang.ClassNotFoundException: Didn't find class \"com.genymobile.scrcpy.Server\"", "could not connect")]
    [InlineData("ERROR: Device disconnected", "disconnected")]
    [InlineData("first line\nERROR: something new", "ERROR: something new")]
    [InlineData("", "closed without saying why")]
    public void ACopyThatFailsSaysWhyInWords(string stderr, string expected) =>
        Assert.Contains(expected, SessionController.CopyFailure(stderr.Split('\n')), StringComparison.Ordinal);

    [Fact]
    public void CopyTitles_AreUniqueSoEachWindowCanBeFound()
    {
        Assert.Equal("Android Headless Mirror [FAKE123] copy 1", SessionController.CopyTitle("FAKE123", 0));
        Assert.NotEqual(SessionController.CopyTitle("S", 0), SessionController.CopyTitle("S", 1));
    }

    [Theory]
    [InlineData(Key.Left, true)]
    [InlineData(Key.Right, true)]
    [InlineData(Key.Up, true)]
    [InlineData(Key.Down, true)]
    [InlineData(Key.PageUp, true)]
    [InlineData(Key.PageDown, true)]
    [InlineData(Key.Home, true)]
    [InlineData(Key.End, true)]
    [InlineData(Key.Tab, false)]
    [InlineData(Key.A, false)]
    public void EveryKeyThatMovesAPhoneSliderCommitsIt(Key key, bool commits) =>
        Assert.Equal(commits, PhonePanel.CommitsSlider(key));
}
