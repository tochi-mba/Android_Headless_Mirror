using System.Runtime.InteropServices;
using Rex.Mirror.Native;

namespace Rex.Mirror.Mirror;

/// <summary>
/// Synthesizes real touch contacts (Windows touch injection) at screen coordinates. scrcpy
/// turns them into genuine multi-touch on the phone, so pinch, rotate and pan behave exactly
/// like fingers on the glass.
/// </summary>
public sealed class TouchInjector
{
    private const int MaxContacts = 2;
    private readonly bool[] _down = new bool[MaxContacts];
    private bool _initialized;

    public bool IsAvailable
    {
        get
        {
            if (_initialized)
            {
                return true;
            }

            _initialized = NativeMethods.InitializeTouchInjection(10, NativeMethods.TOUCH_FEEDBACK_NONE);
            return _initialized;
        }
    }

    public bool AnyDown => _down[0] || _down[1];

    /// <summary>Sends the current position of both contacts. Contacts are pressed on first use.</summary>
    public bool Move((int X, int Y) first, (int X, int Y) second)
    {
        if (!IsAvailable)
        {
            return false;
        }

        var contacts = new[]
        {
            Contact(0, first.X, first.Y, _down[0] ? NativeMethods.POINTER_FLAG_UPDATE : NativeMethods.POINTER_FLAG_DOWN),
            Contact(1, second.X, second.Y, _down[1] ? NativeMethods.POINTER_FLAG_UPDATE : NativeMethods.POINTER_FLAG_DOWN),
        };

        var ok = NativeMethods.InjectTouchInput((uint)contacts.Length, contacts);
        if (ok)
        {
            _down[0] = _down[1] = true;
        }

        return ok;
    }

    public bool Release((int X, int Y) first, (int X, int Y) second)
    {
        if (!AnyDown)
        {
            return true;
        }

        var contacts = new[]
        {
            Contact(0, first.X, first.Y, NativeMethods.POINTER_FLAG_UP),
            Contact(1, second.X, second.Y, NativeMethods.POINTER_FLAG_UP),
        };
        var ok = NativeMethods.InjectTouchInput((uint)contacts.Length, contacts);

        // Even if Windows rejects the UP packet, do not leave REX's logical state stuck down.
        // The error is returned to the bridge so it can be surfaced in diagnostics.
        _down[0] = _down[1] = false;
        return ok;
    }

    public int LastError => Marshal.GetLastWin32Error();

    private static POINTER_TOUCH_INFO Contact(uint id, int x, int y, uint stateFlags)
    {
        var flags = stateFlags;
        if (stateFlags != NativeMethods.POINTER_FLAG_UP)
        {
            flags |= NativeMethods.POINTER_FLAG_INRANGE | NativeMethods.POINTER_FLAG_INCONTACT;
        }

        return new POINTER_TOUCH_INFO
        {
            pointerInfo = new POINTER_INFO
            {
                pointerType = NativeMethods.PT_TOUCH,
                pointerId = id,
                pointerFlags = flags,
                ptPixelLocation = new POINT { X = x, Y = y },
            },
            touchFlags = 0,
            touchMask = NativeMethods.TOUCH_MASK_CONTACTAREA | NativeMethods.TOUCH_MASK_ORIENTATION | NativeMethods.TOUCH_MASK_PRESSURE,
            rcContact = new RECT { Left = x - 2, Top = y - 2, Right = x + 2, Bottom = y + 2 },
            orientation = 90,
            pressure = 32000,
        };
    }
}
