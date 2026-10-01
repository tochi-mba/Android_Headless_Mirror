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
    private readonly Func<bool> _initialize;
    private readonly Func<POINTER_TOUCH_INFO[], bool> _inject;

    public TouchInjector() : this(
        () => NativeMethods.InitializeTouchInjection(MaxContacts, NativeMethods.TOUCH_FEEDBACK_NONE),
        contacts => NativeMethods.InjectTouchInput((uint)contacts.Length, contacts)) { }

    internal TouchInjector(Func<bool> initialize, Func<POINTER_TOUCH_INFO[], bool> inject)
    {
        _initialize = initialize;
        _inject = inject;
    }

    public bool IsAvailable
    {
        get
        {
            if (_initialized)
            {
                return true;
            }

            _initialized = _initialize();
            LastError = _initialized ? 0 : Marshal.GetLastWin32Error();
            return _initialized;
        }
    }

    public bool AnyDown => _down[0] || _down[1];

    /// <summary>Starts or moves one synthetic finger without disturbing the real pointer.</summary>
    public bool MoveOne((int X, int Y) point)
    {
        if (!IsAvailable || _down[1])
        {
            return false;
        }

        var ok = Send([(0, point, _down[0])]);
        if (ok)
        {
            _down[0] = true;
        }

        return ok;
    }

    public bool ReleaseOne((int X, int Y) point)
    {
        if (!_down[0] || _down[1])
        {
            return !_down[1];
        }

        var ok = Lift([(0, point)]);
        _down[0] = false;
        return ok;
    }

    /// <summary>Sends the current position of both contacts. Contacts are pressed on first use.</summary>
    public bool Move((int X, int Y) first, (int X, int Y) second)
    {
        if (!IsAvailable)
        {
            return false;
        }

        var ok = Send([(0, first, _down[0]), (1, second, _down[1])]);
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

        // Even if Windows rejects the UP packet, do not leave REX's logical state stuck down.
        // The error is returned to the bridge so it can be surfaced in diagnostics.
        var ok = Lift([(0, first), (1, second)]);
        _down[0] = _down[1] = false;
        return ok;
    }

    /// <summary>
    /// Presses or moves the contacts (each moves if it is down, and is pressed if not). A press
    /// Windows refuses may be refused because it still holds the contact from a release it refused
    /// earlier, so every contact is let go of and pressed again, once.
    /// </summary>
    private bool Send((uint Id, (int X, int Y) At, bool Down)[] contacts)
    {
        var frame = contacts.Select(c => Contact(c.Id, c.At.X, c.At.Y, c.Down ? NativeMethods.POINTER_FLAG_UPDATE : NativeMethods.POINTER_FLAG_DOWN)).ToArray();
        if (Inject(frame) || contacts.All(c => c.Down))
        {
            return LastError == 0;
        }

        var lifted = contacts.Select(c => (c.Id, c.At)).ToArray();
        Inject(Frame(lifted, NativeMethods.POINTER_FLAG_UP | NativeMethods.POINTER_FLAG_CANCELED));
        return Inject(Frame(lifted, NativeMethods.POINTER_FLAG_DOWN));
    }

    /// <summary>
    /// Lifts the contacts. When Windows refuses the release it may still hold them down, which would
    /// make it refuse the next press as well, so they are cancelled outright.
    /// </summary>
    private bool Lift((uint Id, (int X, int Y) At)[] contacts)
    {
        if (Inject(Frame(contacts, NativeMethods.POINTER_FLAG_UP)))
        {
            return true;
        }

        var refused = LastError;
        Inject(Frame(contacts, NativeMethods.POINTER_FLAG_UP | NativeMethods.POINTER_FLAG_CANCELED));
        LastError = refused;
        return false;
    }

    private static POINTER_TOUCH_INFO[] Frame((uint Id, (int X, int Y) At)[] contacts, uint state) =>
        contacts.Select(c => Contact(c.Id, c.At.X, c.At.Y, state)).ToArray();

    private bool Inject(POINTER_TOUCH_INFO[] frame)
    {
        var ok = _inject(frame);
        LastError = ok ? 0 : Marshal.GetLastWin32Error();
        return ok;
    }

    public int LastError { get; private set; }

    private static POINTER_TOUCH_INFO Contact(uint id, int x, int y, uint stateFlags)
    {
        var flags = stateFlags;
        if ((stateFlags & NativeMethods.POINTER_FLAG_UP) == 0)
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
            pressure = 512,
        };
    }
}
