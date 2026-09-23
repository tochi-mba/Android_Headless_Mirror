using Rex.Core;
using Rex.Mirror.Mirror;
using Rex.Mirror.Native;

namespace Rex.Tests;

public sealed class TouchInputTests
{
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
}
