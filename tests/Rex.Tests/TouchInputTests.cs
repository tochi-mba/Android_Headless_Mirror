using Rex.Core;
using Rex.Mirror.Mirror;
using Rex.Mirror.Native;

namespace Rex.Tests;

public sealed class TouchInputTests
{
    [Fact]
    public void AltGrIsAlwaysLeftForTextInput()
    {
        Assert.True(InputHooks.CanOfferHotkey(rightAltDown: false));
        Assert.False(InputHooks.CanOfferHotkey(rightAltDown: true));
    }

    [Fact]
    public void TwoFingers_OverOneOfTheAppsOwnLists_ScrollThatListRatherThanThePhone()
    {
        var config = new RexConfig();

        // The mirror is a child window, so without this the whole gesture reaches the phone and the
        // settings list sits still under the fingers scrolling it.
        Assert.Equal(TouchpadBridge.GestureKind.Panel, TouchpadBridge.Decide(overPanel: true, altDown: false, config, injectorAvailable: true));
        Assert.Equal(TouchpadBridge.GestureKind.Panel, TouchpadBridge.Decide(overPanel: true, altDown: true, config, injectorAvailable: false));

        // Everywhere else two fingers still mean what they did.
        Assert.Equal(TouchpadBridge.GestureKind.Android, TouchpadBridge.Decide(overPanel: false, altDown: false, config, injectorAvailable: true));
        Assert.Equal(TouchpadBridge.GestureKind.Host, TouchpadBridge.Decide(overPanel: false, altDown: true, config, injectorAvailable: true));

        // And a gesture with nowhere to go is left to Windows.
        Assert.Equal(TouchpadBridge.GestureKind.None, TouchpadBridge.Decide(overPanel: false, altDown: false, config, injectorAvailable: false));

        var noPinch = new RexConfig();
        noPinch.Zoom.PinchZoom = false;
        Assert.Equal(TouchpadBridge.GestureKind.None, TouchpadBridge.Decide(overPanel: false, altDown: true, noPinch, injectorAvailable: true));

        var noAndroid = new RexConfig();
        noAndroid.Touchpad.TwoFingerToAndroid = false;
        Assert.Equal(TouchpadBridge.GestureKind.None, TouchpadBridge.Decide(overPanel: false, altDown: false, noAndroid, injectorAvailable: true));
    }

    [Fact]
    public void ZoomedContactsCannotEscapeTheVisibleViewport()
    {
        var surface = new RECT { Left = -500, Top = -1000, Right = 1500, Bottom = 2000 };
        var viewport = new RECT { Left = 100, Top = 200, Right = 900, Bottom = 800 };
        var visible = TouchpadBridge.VisibleSurface(surface, viewport);
        Assert.Equal((101, 201), TouchpadBridge.MapContact(visible, (500, 500), (0, 0), (-5000, -5000), 1));
        Assert.Equal((898, 798), TouchpadBridge.MapContact(visible, (500, 500), (0, 0), (5000, 5000), 1));
    }

    [Fact]
    public void TranslationMovesBothContactsOnceWithoutChangingTheirSeparation()
    {
        var surface = new RECT { Left = 0, Top = 0, Right = 1000, Bottom = 1000 };
        var first = TouchpadBridge.MapContact(surface, (500, 500), (100, 100), (90, 120), 2);
        var second = TouchpadBridge.MapContact(surface, (500, 500), (100, 100), (130, 120), 2);
        Assert.Equal((480, 540), first);
        Assert.Equal((560, 540), second);
        Assert.Equal(520, (first.X + second.X) / 2);
        Assert.Equal(80, second.X - first.X);
    }

    [Fact]
    public void PinchStaysCenteredAndContactsStayInsideTheSurface()
    {
        var surface = new RECT { Left = 100, Top = 100, Right = 900, Bottom = 900 };
        Assert.Equal((400, 500), TouchpadBridge.MapContact(surface, (500, 500), (0, 0), (-100, 0), 1));
        Assert.Equal((600, 500), TouchpadBridge.MapContact(surface, (500, 500), (0, 0), (100, 0), 1));
        Assert.Equal((101, 898), TouchpadBridge.MapContact(surface, (500, 500), (0, 0), (-10000, 10000), 1));
    }

    [Fact]
    public void InjectionUsesValidPressureAndStableContactsThroughDownMoveAndUp()
    {
        var frames = new List<POINTER_TOUCH_INFO[]>();
        var injector = new TouchInjector(() => true, contacts => { frames.Add(contacts); return true; });
        Assert.True(injector.Move((100, 200), (300, 200)));
        Assert.True(injector.Move((90, 210), (310, 210)));
        Assert.True(injector.Release((90, 210), (310, 210)));
        Assert.False(injector.AnyDown);
        Assert.Equal(3, frames.Count);
        for (var i = 0; i < frames.Count; i++)
        {
            Assert.Equal(2, frames[i].Length);
            for (var finger = 0; finger < 2; finger++)
            {
                var contact = frames[i][finger];
                Assert.Equal((uint)finger, contact.pointerInfo.pointerId);
                Assert.InRange(contact.pressure, 0u, 1024u);
                var state = i switch { 0 => NativeMethods.POINTER_FLAG_DOWN, 1 => NativeMethods.POINTER_FLAG_UPDATE, _ => NativeMethods.POINTER_FLAG_UP };
                Assert.NotEqual(0u, contact.pointerInfo.pointerFlags & state);
                if (i == 2)
                {
                    Assert.Equal(frames[1][finger].pointerInfo.ptPixelLocation.X, contact.pointerInfo.ptPixelLocation.X);
                    Assert.Equal(frames[1][finger].pointerInfo.ptPixelLocation.Y, contact.pointerInfo.ptPixelLocation.Y);
                    Assert.Equal(0u, contact.pointerInfo.pointerFlags & NativeMethods.POINTER_FLAG_INCONTACT);
                }
            }
        }
    }

    [Fact]
    public void FailedReleaseResetsLocalStateAndNextGestureStartsWithDown()
    {
        var frames = new List<POINTER_TOUCH_INFO[]>();
        var injector = new TouchInjector(() => true, contacts => { frames.Add(contacts); return frames.Count != 2; });
        Assert.True(injector.Move((100, 200), (300, 200)));
        Assert.False(injector.Release((100, 200), (300, 200)));
        Assert.False(injector.AnyDown);
        Assert.True(injector.Move((100, 200), (300, 200)));
        Assert.All(frames[2], contact => Assert.NotEqual(0u, contact.pointerInfo.pointerFlags & NativeMethods.POINTER_FLAG_DOWN));
    }

    [Fact]
    public void OneFingerKeyboardGestureHasACompleteDownMoveUpSequence()
    {
        var frames = new List<POINTER_TOUCH_INFO[]>();
        var injector = new TouchInjector(() => true, contacts => { frames.Add(contacts); return true; });

        Assert.True(injector.MoveOne((100, 500)));
        Assert.True(injector.MoveOne((100, 300)));
        Assert.True(injector.ReleaseOne((100, 300)));
        Assert.False(injector.AnyDown);
        Assert.All(frames, frame => Assert.Single(frame));
        Assert.NotEqual(0u, frames[0][0].pointerInfo.pointerFlags & NativeMethods.POINTER_FLAG_DOWN);
        Assert.NotEqual(0u, frames[1][0].pointerInfo.pointerFlags & NativeMethods.POINTER_FLAG_UPDATE);
        Assert.NotEqual(0u, frames[2][0].pointerInfo.pointerFlags & NativeMethods.POINTER_FLAG_UP);
    }

    [Fact]
    public void KeyboardGesturesStayInsideThePictureAndOppositesReverseExactly()
    {
        var surface = new RECT { Left = 100, Top = 200, Right = 500, Bottom = 1000 };

        foreach (var action in MirrorActions.Gestures)
        {
            var strokes = KeyboardTouch.Strokes(action, surface);
            Assert.NotEmpty(strokes);
            Assert.All(strokes.SelectMany(stroke => stroke), point =>
            {
                Assert.InRange(point.X, surface.Left + 1, surface.Right - 1);
                Assert.InRange(point.Y, surface.Top + 1, surface.Bottom - 1);
            });
        }

        var up = Assert.Single(KeyboardTouch.Strokes("swipe-up", surface));
        var down = Assert.Single(KeyboardTouch.Strokes("swipe-down", surface));
        var left = Assert.Single(KeyboardTouch.Strokes("swipe-left", surface));
        var right = Assert.Single(KeyboardTouch.Strokes("swipe-right", surface));
        Assert.Equal(up.Reverse(), down);
        Assert.Equal(left.Reverse(), right);
        Assert.True(up[0].Y > up[^1].Y, "a swipe up starts low and ends high");
        Assert.True(left[0].X > left[^1].X, "a swipe left starts on the right");
        Assert.All(up, point => Assert.Equal(300, point.X));
        Assert.All(left, point => Assert.Equal(600, point.Y));

        Assert.Equal([[(300, 600)]], KeyboardTouch.Strokes("tap", surface));
        Assert.Equal([[(300, 600)], [(300, 600)]], KeyboardTouch.Strokes("like", surface));
        Assert.Empty(KeyboardTouch.Strokes("unknown", surface));
        Assert.Empty(KeyboardTouch.Strokes("swipe-up", new RECT()));
        Assert.Equal("Liked", KeyboardTouch.Outcome("like"));
    }

    [Fact]
    public void BrowseModeKeysAreTheOnesFeedsUseEverywhereElse()
    {
        Assert.Equal("swipe-up", KeyboardBrowse.ActionFor(NativeMethods.VK_DOWN));
        Assert.Equal("swipe-down", KeyboardBrowse.ActionFor(NativeMethods.VK_UP));
        Assert.Equal("swipe-left", KeyboardBrowse.ActionFor(NativeMethods.VK_RIGHT));
        Assert.Equal("swipe-right", KeyboardBrowse.ActionFor(NativeMethods.VK_LEFT));
        Assert.Equal("tap", KeyboardBrowse.ActionFor(NativeMethods.VK_RETURN));
        Assert.Equal("tap", KeyboardBrowse.ActionFor(NativeMethods.VK_SPACE));
        Assert.Equal("like", KeyboardBrowse.ActionFor('L'));
        Assert.Equal("mute", KeyboardBrowse.ActionFor('M'));
        Assert.Equal("back", KeyboardBrowse.ActionFor(NativeMethods.VK_BACK));

        // Letters keep typing, so a search box still works inside the mode.
        Assert.Null(KeyboardBrowse.ActionFor('A'));
        Assert.Null(KeyboardBrowse.ActionFor('1'));
        Assert.Null(KeyboardBrowse.ActionFor(NativeMethods.VK_ESCAPE));

        // Every key the registry tells people about is one the mode takes, and the other way round.
        var registry = Shortcuts.BrowseKeys.Select(key => key.Gesture).OrderBy(g => g, StringComparer.Ordinal);
        var mapped = new Dictionary<string, int>
        {
            ["Down"] = NativeMethods.VK_DOWN, ["Up"] = NativeMethods.VK_UP, ["Right"] = NativeMethods.VK_RIGHT, ["Left"] = NativeMethods.VK_LEFT,
            ["Enter"] = NativeMethods.VK_RETURN, ["L"] = 'L', ["M"] = 'M', ["Backspace"] = NativeMethods.VK_BACK,
        };
        Assert.Equal(mapped.Keys.OrderBy(g => g, StringComparer.Ordinal), registry);
        Assert.All(mapped.Values, key => Assert.NotNull(MirrorActions.Find(KeyboardBrowse.ActionFor(key)!)));

        Assert.True(KeyboardBrowse.Applies(browsing: true, ctrl: false, alt: false, focusInsideControl: false));
        Assert.False(KeyboardBrowse.Applies(browsing: false, ctrl: false, alt: false, focusInsideControl: false));
        Assert.False(KeyboardBrowse.Applies(browsing: true, ctrl: true, alt: false, focusInsideControl: false));
        Assert.False(KeyboardBrowse.Applies(browsing: true, ctrl: false, alt: true, focusInsideControl: false));
        Assert.False(KeyboardBrowse.Applies(browsing: true, ctrl: false, alt: false, focusInsideControl: true));
    }
}
