[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Serial,

    [switch]$TestOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$ConfigPath = Join-Path $Root "config.json"

if (-not (Test-Path $ConfigPath)) {
    if ($TestOnly) { return }
    exit 2
}

$Config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
if ($null -eq $Config.PSObject.Properties["MirrorChrome"] -or -not $Config.MirrorChrome.Enabled) {
    return
}

$ChromeConfig = $Config.MirrorChrome

function Ensure-MirrorChromeRuntimeConfig {
    param($Value)

    $defaults = [ordered]@{
        HostZoomModifier = "alt"
        WheelToHostZoom = $true
        TouchpadPinchToHostZoom = $true
        TouchpadPinchDominanceRatio = 1.35
        TouchpadScrollThreshold = 0.025
        AndroidPinchSensitivity = 0.55
        HostZoomPinchSensitivity = 0.55
        TouchpadScrollSensitivity = 0.04
        TouchpadScrollDeadzone = 2.0
        TouchpadScrollMaxDeltaPerSample = 18.0
        TouchpadSmoothing = 0.25
        HostPanSensitivity = 0.90
        ShowZoomMinimap = $true
    }

    if ($null -eq $Value.PSObject.Properties["WheelToHostZoom"]) {
        $legacy = $Value.PSObject.Properties["CtrlWheelZoom"]
        $Value | Add-Member -NotePropertyName WheelToHostZoom -NotePropertyValue $(
            if ($null -ne $legacy) { [bool]$legacy.Value } else { $true }
        )
    }

    if ($null -eq $Value.PSObject.Properties["TouchpadPinchToHostZoom"]) {
        $legacy = $Value.PSObject.Properties["CtrlTouchpadPinchToHostZoom"]
        $Value | Add-Member -NotePropertyName TouchpadPinchToHostZoom -NotePropertyValue $(
            if ($null -ne $legacy) { [bool]$legacy.Value } else { $true }
        )
    }

    foreach ($entry in $defaults.GetEnumerator()) {
        if ($null -eq $Value.PSObject.Properties[$entry.Key]) {
            $Value | Add-Member -NotePropertyName $entry.Key -NotePropertyValue $entry.Value
        }
    }

    # Ctrl and Shift both have scrcpy gesture semantics. REX reserves Alt
    # exclusively for Windows-side host magnification.
    $Value.HostZoomModifier = "alt"
}

Ensure-MirrorChromeRuntimeConfig $ChromeConfig
. (Join-Path $Root "MirrorInteraction.ps1")
. (Join-Path $Root "ScrcpyControl.ps1")

$WindowTitle = "{0} [{1}]" -f ([string]$Config.WindowTitle), $Serial

$nativeSource = @'
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

public static class AHMMirrorChromeNative
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MAGTRANSFORM
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
        public float[] v;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINTER_INFO
    {
        public uint pointerType;
        public uint pointerId;
        public uint frameId;
        public uint pointerFlags;
        public IntPtr sourceDevice;
        public IntPtr hwndTarget;
        public POINT ptPixelLocation;
        public POINT ptHimetricLocation;
        public POINT ptPixelLocationRaw;
        public POINT ptHimetricLocationRaw;
        public uint dwTime;
        public uint historyCount;
        public int inputData;
        public uint dwKeyStates;
        public ulong performanceCount;
        public uint buttonChangeType;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINTER_TOUCH_INFO
    {
        public POINTER_INFO pointerInfo;
        public uint touchFlags;
        public uint touchMask;
        public RECT rcContact;
        public RECT rcContactRaw;
        public uint orientation;
        public uint pressure;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOUCHPAD_SAMPLE
    {
        public uint PointerId;
        public uint PointerFlags;
        public int X;
        public int Y;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool RegisterTouchpadCapableWindowDelegate(IntPtr hwnd, bool enable);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool GetPointerTouchpadInfoDelegate(uint pointerId, out POINTER_TOUCH_INFO info);

    public const int WH_MOUSE_LL = 14;
    public const int WM_MOUSEWHEEL = 0x020A;
    public const int WM_NCHITTEST = 0x0084;
    public const int WM_POINTERUPDATE = 0x0245;
    public const int WM_POINTERDOWN = 0x0246;
    public const int WM_POINTERUP = 0x0247;
    public const int HTTRANSPARENT = -1;

    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;

    public const uint POINTER_FLAG_INCONTACT = 0x00000004;
    public const uint POINTER_FLAG_DOWN = 0x00010000;
    public const uint POINTER_FLAG_UPDATE = 0x00020000;
    public const uint POINTER_FLAG_UP = 0x00040000;

    public const uint INPUT_MOUSE = 0;
    public const uint INPUT_KEYBOARD = 1;
    public const uint MOUSEEVENTF_MOVE = 0x0001;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_WHEEL = 0x0800;
    public const uint MOUSEEVENTF_HWHEEL = 0x01000;
    public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    public const int SM_XVIRTUALSCREEN = 76;
    public const int SM_YVIRTUALSCREEN = 77;
    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;

    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;

    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TRANSPARENT = 0x00000020L;
    public const long WS_EX_TOOLWINDOW = 0x00000080L;
    public const long WS_EX_NOACTIVATE = 0x08000000L;

    public const uint WS_POPUP = 0x80000000;
    public const uint WS_VISIBLE = 0x10000000;
    public const uint WS_CHILD = 0x40000000;

    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_NOSENDCHANGING = 0x0400;
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);


    public const int MW_FILTERMODE_EXCLUDE = 0;

    private static IntPtr hook = IntPtr.Zero;
    private static LowLevelMouseProc hookProc;
    private static IntPtr wheelTarget = IntPtr.Zero;
    private static readonly ConcurrentQueue<int> wheelDeltas = new ConcurrentQueue<int>();
    private static bool magnificationInitialized = false;
    private static RegisterTouchpadCapableWindowDelegate registerTouchpadWindow;
    private static GetPointerTouchpadInfoDelegate getTouchpadInfo;
    private static bool touchpadApiResolved = false;
    private static POINT savedPinchCursor;
    private static bool pinchCursorSaved = false;
    private static bool syntheticPinchDown = false;

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int X, int Y);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, IntPtr lpProcName);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    public static void SetNoActivateToolWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        long style = IntPtr.Size == 8
            ? GetWindowLongPtr64(hwnd, GWL_EXSTYLE).ToInt64()
            : GetWindowLong32(hwnd, GWL_EXSTYLE);

        style |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;

        if (IntPtr.Size == 8)
        {
            SetWindowLongPtr64(hwnd, GWL_EXSTYLE, new IntPtr(style));
        }
        else
        {
            SetWindowLong32(hwnd, GWL_EXSTYLE, unchecked((int)style));
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags
    );

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam
    );

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("Magnification.dll", SetLastError = true)]
    public static extern bool MagInitialize();

    [DllImport("Magnification.dll", SetLastError = true)]
    public static extern bool MagUninitialize();

    [DllImport("Magnification.dll", SetLastError = true)]
    public static extern bool MagSetWindowSource(IntPtr hwnd, RECT rect);

    [DllImport("Magnification.dll", SetLastError = true)]
    public static extern bool MagSetWindowTransform(IntPtr hwnd, ref MAGTRANSFORM transform);

    [DllImport("Magnification.dll", SetLastError = true)]
    public static extern int MagSetWindowFilterList(IntPtr hwnd, int dwFilterMode, int count, IntPtr[] pHWND);


    private static bool ResolveTouchpadApi()
    {
        if (touchpadApiResolved)
        {
            return registerTouchpadWindow != null && getTouchpadInfo != null;
        }

        touchpadApiResolved = true;

        IntPtr user32 = GetModuleHandle("user32.dll");
        if (user32 == IntPtr.Zero)
        {
            return false;
        }

        IntPtr registerPtr = GetProcAddress(user32, new IntPtr(2689));
        IntPtr infoPtr = GetProcAddress(user32, new IntPtr(2691));
        if (registerPtr == IntPtr.Zero || infoPtr == IntPtr.Zero)
        {
            return false;
        }

        registerTouchpadWindow = (RegisterTouchpadCapableWindowDelegate)
            Marshal.GetDelegateForFunctionPointer(registerPtr, typeof(RegisterTouchpadCapableWindowDelegate));
        getTouchpadInfo = (GetPointerTouchpadInfoDelegate)
            Marshal.GetDelegateForFunctionPointer(infoPtr, typeof(GetPointerTouchpadInfoDelegate));

        return registerTouchpadWindow != null && getTouchpadInfo != null;
    }

    public static bool RegisterPrecisionTouchpadWindow(IntPtr hwnd, bool enable)
    {
        try
        {
            return ResolveTouchpadApi() && registerTouchpadWindow(hwnd, enable);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetTouchpadSample(uint pointerId, out TOUCHPAD_SAMPLE sample)
    {
        sample = new TOUCHPAD_SAMPLE();

        try
        {
            if (!ResolveTouchpadApi())
            {
                return false;
            }

            POINTER_TOUCH_INFO info;
            if (!getTouchpadInfo(pointerId, out info))
            {
                return false;
            }

            sample.PointerId = info.pointerInfo.pointerId;
            sample.PointerFlags = info.pointerInfo.pointerFlags;
            sample.X = info.pointerInfo.ptHimetricLocation.X;
            sample.Y = info.pointerInfo.ptHimetricLocation.Y;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static INPUT KeyboardInput(ushort key, bool up)
    {
        INPUT input = new INPUT();
        input.type = INPUT_KEYBOARD;
        input.U.ki.wVk = key;
        input.U.ki.dwFlags = up ? KEYEVENTF_KEYUP : 0;
        return input;
    }

    private static INPUT MouseInputAbsolute(int x, int y, uint extraFlags)
    {
        int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vw = Math.Max(2, GetSystemMetrics(SM_CXVIRTUALSCREEN));
        int vh = Math.Max(2, GetSystemMetrics(SM_CYVIRTUALSCREEN));

        int dx = (int)Math.Round(((double)(x - vx) * 65535.0) / (vw - 1));
        int dy = (int)Math.Round(((double)(y - vy) * 65535.0) / (vh - 1));

        INPUT input = new INPUT();
        input.type = INPUT_MOUSE;
        input.U.mi.dx = dx;
        input.U.mi.dy = dy;
        input.U.mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK | extraFlags;
        return input;
    }

    public static bool BeginScrcpyPinch(IntPtr target, int x, int y)
    {
        if (target == IntPtr.Zero || syntheticPinchDown)
        {
            return false;
        }

        SetForegroundWindow(target);

        if (GetCursorPos(out savedPinchCursor))
        {
            pinchCursorSaved = true;
        }
        else
        {
            pinchCursorSaved = false;
        }

        INPUT[] inputs = new INPUT[3];
        inputs[0] = KeyboardInput((ushort)VK_CONTROL, false);
        inputs[1] = MouseInputAbsolute(x, y, 0);
        inputs[2] = MouseInputAbsolute(x, y, MOUSEEVENTF_LEFTDOWN);

        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        syntheticPinchDown = sent == inputs.Length;
        return syntheticPinchDown;
    }

    public static bool UpdateScrcpyPinch(int x, int y)
    {
        if (!syntheticPinchDown)
        {
            return false;
        }

        INPUT[] inputs = new INPUT[1];
        inputs[0] = MouseInputAbsolute(x, y, 0);
        return SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT))) == 1;
    }

    public static void EndScrcpyPinch()
    {
        if (!syntheticPinchDown)
        {
            return;
        }

        POINT current;
        if (!GetCursorPos(out current))
        {
            current.X = 0;
            current.Y = 0;
        }

        INPUT[] inputs = new INPUT[2];
        inputs[0] = MouseInputAbsolute(current.X, current.Y, MOUSEEVENTF_LEFTUP);
        inputs[1] = KeyboardInput((ushort)VK_CONTROL, true);
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));

        syntheticPinchDown = false;

        if (pinchCursorSaved)
        {
            SetCursorPos(savedPinchCursor.X, savedPinchCursor.Y);
            pinchCursorSaved = false;
        }
    }

    public static bool IsSyntheticPinchActive()
    {
        return syntheticPinchDown;
    }

    public static IntPtr FindWindowByExactTitle(string title)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr hwnd, IntPtr lParam)
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            StringBuilder builder = new StringBuilder(512);
            GetWindowText(hwnd, builder, builder.Capacity);
            if (String.Equals(builder.ToString(), title, StringComparison.Ordinal))
            {
                found = hwnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    public static bool TryGetClientScreenRect(IntPtr hwnd, out RECT screenRect)
    {
        screenRect = new RECT();

        RECT client;
        if (!GetClientRect(hwnd, out client))
        {
            return false;
        }

        POINT topLeft = new POINT();
        topLeft.X = client.Left;
        topLeft.Y = client.Top;

        POINT bottomRight = new POINT();
        bottomRight.X = client.Right;
        bottomRight.Y = client.Bottom;

        if (!ClientToScreen(hwnd, ref topLeft) || !ClientToScreen(hwnd, ref bottomRight))
        {
            return false;
        }

        screenRect.Left = topLeft.X;
        screenRect.Top = topLeft.Y;
        screenRect.Right = bottomRight.X;
        screenRect.Bottom = bottomRight.Y;
        return true;
    }

    public static bool InitializeMagnification()
    {
        if (magnificationInitialized)
        {
            return true;
        }

        magnificationInitialized = MagInitialize();
        return magnificationInitialized;
    }

    public static void UninitializeMagnification()
    {
        if (magnificationInitialized)
        {
            MagUninitialize();
            magnificationInitialized = false;
        }
    }

    public static IntPtr CreateMagnifierHost()
    {
        if (!InitializeMagnification())
        {
            return IntPtr.Zero;
        }

        uint exStyle = (uint)(WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        IntPtr host = CreateWindowEx(
            exStyle,
            "Static",
            "",
            WS_POPUP,
            0,
            0,
            1,
            1,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero
        );

        return host;
    }

    public static IntPtr CreateMagnifierChild(IntPtr host)
    {
        if (host == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        uint exStyle = (uint)(WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
        return CreateWindowEx(
            exStyle,
            "Magnifier",
            "",
            WS_CHILD | WS_VISIBLE,
            0,
            0,
            1,
            1,
            host,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero
        );
    }

    public static bool SetMagnifierTransform(IntPtr magnifier, float zoom)
    {
        MAGTRANSFORM transform = new MAGTRANSFORM();
        transform.v = new float[9];
        transform.v[0] = zoom;
        transform.v[4] = zoom;
        transform.v[8] = 1.0f;
        return MagSetWindowTransform(magnifier, ref transform);
    }

    public static void ExcludeWindowsFromMagnifier(IntPtr magnifier, IntPtr[] windows)
    {
        if (magnifier == IntPtr.Zero || windows == null || windows.Length == 0)
        {
            return;
        }

        MagSetWindowFilterList(magnifier, MW_FILTERMODE_EXCLUDE, windows.Length, windows);
    }

    public static bool StartWheelHook(IntPtr target)
    {
        StopWheelHook();

        wheelTarget = target;
        hookProc = HookCallback;
        hook = SetWindowsHookEx(WH_MOUSE_LL, hookProc, GetModuleHandle(null), 0);
        return hook != IntPtr.Zero;
    }

    public static void UpdateWheelTarget(IntPtr target)
    {
        wheelTarget = target;
    }

    public static void StopWheelHook()
    {
        if (hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }

        hookProc = null;
        wheelTarget = IntPtr.Zero;

        int ignored;
        while (wheelDeltas.TryDequeue(out ignored)) {}
    }

    public static bool TryDequeueWheel(out int delta)
    {
        return wheelDeltas.TryDequeue(out delta);
    }

    public static bool SendWheelToTarget(IntPtr target, int delta, bool horizontal)
    {
        if (target == IntPtr.Zero || delta == 0)
        {
            return false;
        }

        SetForegroundWindow(target);

        INPUT input = new INPUT();
        input.type = INPUT_MOUSE;
        input.U.mi.mouseData = unchecked((uint)delta);
        input.U.mi.dwFlags = horizontal ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL;

        INPUT[] inputs = new INPUT[1];
        inputs[0] = input;
        return SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT))) == 1;
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (
            nCode >= 0 &&
            wParam.ToInt32() == WM_MOUSEWHEEL &&
            wheelTarget != IntPtr.Zero &&
            GetForegroundWindow() == wheelTarget &&
            (GetAsyncKeyState(VK_MENU) & 0x8000) != 0
        )
        {
            MSLLHOOKSTRUCT data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
            int delta = (short)((data.mouseData >> 16) & 0xffff);
            wheelDeltas.Enqueue(delta);
            return new IntPtr(1);
        }

        return CallNextHookEx(hook, nCode, wParam, lParam);
    }


}
'@

Add-Type -TypeDefinition $nativeSource -ErrorAction Stop

if ($TestOnly) {
    return
}

try {
    [AHMMirrorChromeNative]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
}
catch {}

Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

function Get-TouchpadGestureMetrics($Contacts) {
    # Keep pointer ordering stable across frames so a hashtable enumeration
    # change cannot flip the vector by PI and create a fake rotation.
    $items = @(
        $Contacts.GetEnumerator() |
            Sort-Object { [uint64]$_.Key } |
            Select-Object -First 2 |
            ForEach-Object { $_.Value }
    )
    if ($items.Count -lt 2) { return $null }

    $dx = [double]$items[1].X - [double]$items[0].X
    $dy = [double]$items[1].Y - [double]$items[0].Y
    $distance = [Math]::Sqrt(($dx * $dx) + ($dy * $dy))

    if ($distance -le 0.0001) { return $null }

    return [pscustomobject]@{
        Distance = $distance
        Angle = [Math]::Atan2($dy, $dx)
        CenterX = ([double]$items[0].X + [double]$items[1].X) / 2.0
        CenterY = ([double]$items[0].Y + [double]$items[1].Y) / 2.0
    }
}

function Normalize-Angle([double]$Radians) {
    while ($Radians -gt [Math]::PI) { $Radians -= (2.0 * [Math]::PI) }
    while ($Radians -lt -[Math]::PI) { $Radians += (2.0 * [Math]::PI) }
    return $Radians
}

function Get-SyntheticPinchPoint($ClientRect, [double]$BaseRadius, [double]$Scale, [double]$AngleDelta) {
    $width = [Math]::Max(1.0, [double]($ClientRect.Right - $ClientRect.Left))
    $height = [Math]::Max(1.0, [double]($ClientRect.Bottom - $ClientRect.Top))
    $centerX = [double]$ClientRect.Left + ($width / 2.0)
    $centerY = [double]$ClientRect.Top + ($height / 2.0)

    $maxRadius = [Math]::Max(12.0, [Math]::Min($width, $height) * 0.46)
    $radius = [Math]::Max(12.0, [Math]::Min($maxRadius, ($BaseRadius * $Scale)))

    return [pscustomobject]@{
        X = [int][Math]::Round($centerX + ($radius * [Math]::Cos($AngleDelta)))
        Y = [int][Math]::Round($centerY + ($radius * [Math]::Sin($AngleDelta)))
    }
}

function Get-ClampedHostZoom([double]$StartZoom, [double]$Scale, $Config) {
    $next = $StartZoom * $Scale
    return [Math]::Max(
        [double]$Config.MinZoom,
        [Math]::Min([double]$Config.MaxZoom, $next)
    )
}

$toolbar = New-Object System.Windows.Window
$toolbar.WindowStyle = [System.Windows.WindowStyle]::None
$toolbar.ResizeMode = [System.Windows.ResizeMode]::NoResize
$toolbar.AllowsTransparency = $true
$toolbar.Background = [System.Windows.Media.Brushes]::Transparent
$toolbar.ShowInTaskbar = $false
$toolbar.Topmost = $true
$toolbar.ShowActivated = $false
$toolbar.SizeToContent = [System.Windows.SizeToContent]::WidthAndHeight

$border = New-Object System.Windows.Controls.Border
$border.Background = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#E6080A09")
$border.BorderBrush = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#FF29302A")
$border.BorderThickness = New-Object System.Windows.Thickness(1)
$border.CornerRadius = New-Object System.Windows.CornerRadius(9)
$border.Padding = New-Object System.Windows.Thickness(7, 5, 7, 5)
$toolbar.Content = $border

$stack = New-Object System.Windows.Controls.StackPanel
$stack.Orientation = [System.Windows.Controls.Orientation]::Horizontal
$border.Child = $stack

function New-ToolbarButton([string]$Text) {
    $button = New-Object System.Windows.Controls.Button
    $button.Content = $Text
    $button.Padding = New-Object System.Windows.Thickness(10, 5, 10, 5)
    $button.Margin = New-Object System.Windows.Thickness(2, 0, 2, 0)
    $button.FontFamily = New-Object System.Windows.Media.FontFamily("Segoe UI")
    $button.FontSize = 11
    $button.Foreground = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#FFF2F5EE")
    $button.Background = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#FF181E19")
    $button.BorderBrush = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#FF29302A")
    return $button
}

$controlsButton = New-ToolbarButton "Controls"
if (
    $null -eq $Config.PSObject.Properties["ControlCenter"] -or
    -not [bool]$Config.ControlCenter.Enabled
) {
    $controlsButton.Visibility = [System.Windows.Visibility]::Collapsed
}
$stack.Children.Add($controlsButton) | Out-Null

$sleepButton = New-ToolbarButton "Sleep phone"
if (-not $ChromeConfig.SleepButton) {
    $sleepButton.Visibility = [System.Windows.Visibility]::Collapsed
}
$stack.Children.Add($sleepButton) | Out-Null

$zoomLabel = New-Object System.Windows.Controls.TextBlock
$zoomLabel.VerticalAlignment = [System.Windows.VerticalAlignment]::Center
$zoomLabel.Margin = New-Object System.Windows.Thickness(8, 0, 5, 0)
$zoomLabel.FontFamily = New-Object System.Windows.Media.FontFamily("Consolas")
$zoomLabel.FontSize = 10
$zoomLabel.Foreground = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#FF858D83")
$zoomLabel.Text = "100%"
$stack.Children.Add($zoomLabel) | Out-Null

$resetZoomButton = New-ToolbarButton "Reset zoom"
$resetZoomButton.Visibility = [System.Windows.Visibility]::Collapsed
$stack.Children.Add($resetZoomButton) | Out-Null

$toolbar.Show()
$toolbarHwnd = (New-Object System.Windows.Interop.WindowInteropHelper($toolbar)).Handle
$toolbar.Hide()

$navigatorWindow = New-Object System.Windows.Window
$navigatorWindow.WindowStyle = [System.Windows.WindowStyle]::None
$navigatorWindow.ResizeMode = [System.Windows.ResizeMode]::NoResize
$navigatorWindow.AllowsTransparency = $true
$navigatorWindow.Background = [System.Windows.Media.Brushes]::Transparent
$navigatorWindow.ShowInTaskbar = $false
$navigatorWindow.Topmost = $true
$navigatorWindow.ShowActivated = $false
$navigatorWindow.Focusable = $false
$navigatorWindow.Width = 176
$navigatorWindow.Height = 126

$navigatorBorder = New-Object System.Windows.Controls.Border
$navigatorBorder.Background = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#EE080A09")
$navigatorBorder.BorderBrush = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#FF4A544B")
$navigatorBorder.BorderThickness = New-Object System.Windows.Thickness(1)
$navigatorBorder.CornerRadius = New-Object System.Windows.CornerRadius(8)
$navigatorBorder.Padding = New-Object System.Windows.Thickness(8)
$navigatorWindow.Content = $navigatorBorder

$navigatorStack = New-Object System.Windows.Controls.StackPanel
$navigatorBorder.Child = $navigatorStack

$navigatorLabel = New-Object System.Windows.Controls.TextBlock
$navigatorLabel.Text = "ZOOM NAVIGATOR"
$navigatorLabel.FontFamily = New-Object System.Windows.Media.FontFamily("Consolas")
$navigatorLabel.FontSize = 9
$navigatorLabel.Foreground = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#FFD7FF3F")
$navigatorLabel.Margin = New-Object System.Windows.Thickness(1, 0, 0, 6)
$navigatorStack.Children.Add($navigatorLabel) | Out-Null

$navigatorCanvas = New-Object System.Windows.Controls.Canvas
$navigatorCanvas.Width = 158
$navigatorCanvas.Height = 88
$navigatorCanvas.Background = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#FF101511")
$navigatorCanvas.Cursor = [System.Windows.Input.Cursors]::Cross
$navigatorStack.Children.Add($navigatorCanvas) | Out-Null

$navigatorViewport = New-Object System.Windows.Shapes.Rectangle
$navigatorViewport.Stroke = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#FFD7FF3F")
$navigatorViewport.StrokeThickness = 2
$navigatorViewport.Fill = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#22D7FF3F")
$navigatorViewport.IsHitTestVisible = $false
$navigatorCanvas.Children.Add($navigatorViewport) | Out-Null

$navigatorWindow.Show()
$navigatorHwnd = (New-Object System.Windows.Interop.WindowInteropHelper($navigatorWindow)).Handle
[AHMMirrorChromeNative]::SetNoActivateToolWindow($navigatorHwnd)
$navigatorWindow.Hide()
$script:navigatorDragging = $false

$gestureWindow = New-Object System.Windows.Window
$gestureWindow.WindowStyle = [System.Windows.WindowStyle]::None
$gestureWindow.ResizeMode = [System.Windows.ResizeMode]::NoResize
$gestureWindow.AllowsTransparency = $true
$gestureWindow.Background = [System.Windows.Media.Brushes]::Transparent
$gestureWindow.Opacity = 0.01
$gestureWindow.ShowInTaskbar = $false
$gestureWindow.ShowActivated = $false
$gestureWindow.Topmost = $true
$gestureWindow.Focusable = $false
$gestureWindow.Show()

$gestureHwnd = (New-Object System.Windows.Interop.WindowInteropHelper($gestureWindow)).Handle
$gestureSource = [System.Windows.Interop.HwndSource]::FromHwnd($gestureHwnd)
$script:precisionTouchpadAvailable = $false
if ($ChromeConfig.NativeTouchpadGestures) {
    $script:precisionTouchpadAvailable = [AHMMirrorChromeNative]::RegisterPrecisionTouchpadWindow($gestureHwnd, $true)
}
$gestureWindow.Hide()

$script:targetHwnd = [IntPtr]::Zero
$script:targetSeen = $false
$script:missingSince = $null
$script:zoom = 1.0
$script:zoomAnchorX = 0.5
$script:zoomAnchorY = 0.5
$script:magnifierHost = [IntPtr]::Zero
$script:magnifierChild = [IntPtr]::Zero
$script:wheelHookStarted = $false
$script:lastRectKey = ""
$script:touchpadContacts = @{}
$script:touchpadGestureKind = "none"
$script:touchpadStartMetrics = $null
$script:touchpadLastMetrics = $null
$script:touchpadStartHostZoom = 1.0
$script:touchpadBaseRadius = 0.0
$script:touchpadGestureHandled = $false
$script:touchpadSmoothedScrollX = 0.0
$script:touchpadSmoothedScrollY = 0.0
$script:gestureHook = $null
$script:controlCenterProcess = $null
$script:lastCommandPoll = [DateTime]::MinValue
$script:controlCenterAutoOpened = $false

function Get-TargetClientRect {
    if ($script:targetHwnd -eq [IntPtr]::Zero) { return $null }

    $rect = New-Object AHMMirrorChromeNative+RECT
    if (-not [AHMMirrorChromeNative]::TryGetClientScreenRect($script:targetHwnd, [ref]$rect)) {
        return $null
    }

    return $rect
}

function Hide-ZoomNavigator {
    if ($navigatorWindow.IsVisible) {
        $navigatorWindow.Hide()
    }
}

function Set-ZoomAnchorFromNavigatorPoint($Point) {
    $width = [Math]::Max(1.0, [double]$navigatorCanvas.ActualWidth)
    $height = [Math]::Max(1.0, [double]$navigatorCanvas.ActualHeight)
    $script:zoomAnchorX = [Math]::Max(0.0, [Math]::Min(1.0, ([double]$Point.X / $width)))
    $script:zoomAnchorY = [Math]::Max(0.0, [Math]::Min(1.0, ([double]$Point.Y / $height)))
    Update-Magnifier
    Update-ZoomNavigator
}

function Update-ZoomNavigator {
    if (
        -not [bool]$ChromeConfig.ShowZoomMinimap -or
        -not (Test-HostZoomActive $script:zoom)
    ) {
        Hide-ZoomNavigator
        return
    }

    $rect = Get-TargetClientRect
    if ($null -eq $rect) {
        Hide-ZoomNavigator
        return
    }

    $sourceWidth = [Math]::Max(1.0, [double]($rect.Right - $rect.Left))
    $sourceHeight = [Math]::Max(1.0, [double]($rect.Bottom - $rect.Top))
    $geometry = Get-HostZoomSourceGeometry         0.0         0.0         $sourceWidth         $sourceHeight         $script:zoom         $script:zoomAnchorX         $script:zoomAnchorY

    if (-not $navigatorWindow.IsVisible) {
        $navigatorWindow.Show()
    }

    $navigatorWindow.UpdateLayout()
    $canvasWidth = [Math]::Max(1.0, [double]$navigatorCanvas.ActualWidth)
    $canvasHeight = [Math]::Max(1.0, [double]$navigatorCanvas.ActualHeight)

    [System.Windows.Controls.Canvas]::SetLeft(
        $navigatorViewport,
        [double]$geometry.NormalizedLeft * $canvasWidth
    )
    [System.Windows.Controls.Canvas]::SetTop(
        $navigatorViewport,
        [double]$geometry.NormalizedTop * $canvasHeight
    )
    $navigatorViewport.Width = [Math]::Max(
        4.0,
        [double]$geometry.NormalizedWidth * $canvasWidth
    )
    $navigatorViewport.Height = [Math]::Max(
        4.0,
        [double]$geometry.NormalizedHeight * $canvasHeight
    )

    $dpi = [System.Windows.Media.VisualTreeHelper]::GetDpi($navigatorWindow)
    $pixelWidth = [Math]::Max(1, [int][Math]::Ceiling($navigatorWindow.ActualWidth * $dpi.DpiScaleX))
    $pixelHeight = [Math]::Max(1, [int][Math]::Ceiling($navigatorWindow.ActualHeight * $dpi.DpiScaleY))
    $inset = [int][Math]::Round([double]$ChromeConfig.ToolbarInsetPixels)

    [AHMMirrorChromeNative]::SetWindowPos(
        $navigatorHwnd,
        [AHMMirrorChromeNative]::HWND_TOPMOST,
        $rect.Right - $pixelWidth - $inset,
        $rect.Bottom - $pixelHeight - $inset,
        $pixelWidth,
        $pixelHeight,
        [AHMMirrorChromeNative]::SWP_NOACTIVATE -bor
        [AHMMirrorChromeNative]::SWP_NOSENDCHANGING -bor
        [AHMMirrorChromeNative]::SWP_SHOWWINDOW
    ) | Out-Null
}

$navigatorCanvas.Add_PreviewMouseLeftButtonDown({
    param($sender, $eventArgs)
    if (-not (Test-HostZoomActive $script:zoom)) { return }

    $script:navigatorDragging = $true
    [System.Windows.Input.Mouse]::Capture($navigatorCanvas) | Out-Null
    Set-ZoomAnchorFromNavigatorPoint ($eventArgs.GetPosition($navigatorCanvas))
    $eventArgs.Handled = $true
})

$navigatorCanvas.Add_PreviewMouseMove({
    param($sender, $eventArgs)
    if (-not $script:navigatorDragging) { return }
    Set-ZoomAnchorFromNavigatorPoint ($eventArgs.GetPosition($navigatorCanvas))
    $eventArgs.Handled = $true
})

$navigatorCanvas.Add_PreviewMouseLeftButtonUp({
    param($sender, $eventArgs)
    if ($script:navigatorDragging) {
        $script:navigatorDragging = $false
        [System.Windows.Input.Mouse]::Capture($null) | Out-Null
        Set-ZoomAnchorFromNavigatorPoint ($eventArgs.GetPosition($navigatorCanvas))
        $eventArgs.Handled = $true
    }
})

function Ensure-Magnifier {
    if (-not $ChromeConfig.HostZoomEnabled) { return $false }

    if ($script:magnifierHost -eq [IntPtr]::Zero) {
        $script:magnifierHost = [AHMMirrorChromeNative]::CreateMagnifierHost()
    }

    if ($script:magnifierHost -eq [IntPtr]::Zero) { return $false }

    if ($script:magnifierChild -eq [IntPtr]::Zero) {
        $script:magnifierChild = [AHMMirrorChromeNative]::CreateMagnifierChild($script:magnifierHost)
    }

    if ($script:magnifierChild -eq [IntPtr]::Zero) { return $false }

    [AHMMirrorChromeNative]::ExcludeWindowsFromMagnifier(
        $script:magnifierChild,
        @($script:magnifierHost, $toolbarHwnd, $gestureHwnd, $navigatorHwnd)
    )

    return $true
}

function Hide-Magnifier {
    if ($script:magnifierHost -ne [IntPtr]::Zero) {
        [AHMMirrorChromeNative]::ShowWindow($script:magnifierHost, [AHMMirrorChromeNative]::SW_HIDE) | Out-Null
    }
}

function Update-ZoomUi {
    $zoomLabel.Text = ("{0:0}%" -f ($script:zoom * 100.0))

    if (Test-HostZoomActive $script:zoom) {
        $resetZoomButton.Visibility = [System.Windows.Visibility]::Visible
    }
    else {
        $resetZoomButton.Visibility = [System.Windows.Visibility]::Collapsed
    }

    Update-ZoomNavigator
    Write-MirrorChromeState
}

function Reset-HostZoom {
    $script:zoom = 1.0
    $script:zoomAnchorX = 0.5
    $script:zoomAnchorY = 0.5
    Hide-Magnifier
    Update-ZoomUi
}

function Update-Magnifier {
    if ($script:zoom -le 1.001) {
        Hide-Magnifier
        return
    }

    if (-not (Ensure-Magnifier)) { return }

    $rect = Get-TargetClientRect
    if ($null -eq $rect) { return }

    $width = [Math]::Max(1, $rect.Right - $rect.Left)
    $height = [Math]::Max(1, $rect.Bottom - $rect.Top)

    $sourceWidth = [Math]::Max(1, [int][Math]::Round($width / $script:zoom))
    $sourceHeight = [Math]::Max(1, [int][Math]::Round($height / $script:zoom))

    $anchorScreenX = $rect.Left + ($width * $script:zoomAnchorX)
    $anchorScreenY = $rect.Top + ($height * $script:zoomAnchorY)

    $sourceLeft = [int][Math]::Round($anchorScreenX - (($width * $script:zoomAnchorX) / $script:zoom))
    $sourceTop = [int][Math]::Round($anchorScreenY - (($height * $script:zoomAnchorY) / $script:zoom))

    $minLeft = $rect.Left
    $maxLeft = $rect.Right - $sourceWidth
    $minTop = $rect.Top
    $maxTop = $rect.Bottom - $sourceHeight

    $sourceLeft = [Math]::Max($minLeft, [Math]::Min($maxLeft, $sourceLeft))
    $sourceTop = [Math]::Max($minTop, [Math]::Min($maxTop, $sourceTop))

    $sourceRect = New-Object AHMMirrorChromeNative+RECT
    $sourceRect.Left = $sourceLeft
    $sourceRect.Top = $sourceTop
    $sourceRect.Right = $sourceLeft + $sourceWidth
    $sourceRect.Bottom = $sourceTop + $sourceHeight

    [AHMMirrorChromeNative]::SetWindowPos(
        $script:magnifierHost,
        [AHMMirrorChromeNative]::HWND_TOPMOST,
        $rect.Left,
        $rect.Top,
        $width,
        $height,
        [AHMMirrorChromeNative]::SWP_NOACTIVATE -bor
        [AHMMirrorChromeNative]::SWP_NOSENDCHANGING -bor
        [AHMMirrorChromeNative]::SWP_SHOWWINDOW
    ) | Out-Null

    [AHMMirrorChromeNative]::SetWindowPos(
        $script:magnifierChild,
        [IntPtr]::Zero,
        0,
        0,
        $width,
        $height,
        [AHMMirrorChromeNative]::SWP_NOACTIVATE
    ) | Out-Null

    [AHMMirrorChromeNative]::MagSetWindowSource($script:magnifierChild, $sourceRect) | Out-Null
    [AHMMirrorChromeNative]::SetMagnifierTransform($script:magnifierChild, [float]$script:zoom) | Out-Null
    [AHMMirrorChromeNative]::ShowWindow($script:magnifierHost, [AHMMirrorChromeNative]::SW_SHOWNOACTIVATE) | Out-Null
}

function Apply-ZoomDelta([int]$Delta) {
    if (-not $ChromeConfig.HostZoomEnabled) { return }
    if ($Delta -eq 0) { return }

    $rect = Get-TargetClientRect
    if ($null -eq $rect) { return }

    $cursor = New-Object AHMMirrorChromeNative+POINT
    if ([AHMMirrorChromeNative]::GetCursorPos([ref]$cursor)) {
        $width = [Math]::Max(1.0, [double]($rect.Right - $rect.Left))
        $height = [Math]::Max(1.0, [double]($rect.Bottom - $rect.Top))
        $script:zoomAnchorX = [Math]::Max(0.0, [Math]::Min(1.0, (($cursor.X - $rect.Left) / $width)))
        $script:zoomAnchorY = [Math]::Max(0.0, [Math]::Min(1.0, (($cursor.Y - $rect.Top) / $height)))
    }

    $direction = if ($Delta -gt 0) { 1.0 } else { -1.0 }
    $next = $script:zoom + ($direction * [double]$ChromeConfig.ZoomStep)
    $script:zoom = [Math]::Max(
        [double]$ChromeConfig.MinZoom,
        [Math]::Min([double]$ChromeConfig.MaxZoom, $next)
    )

    if ($script:zoom -lt 1.001) {
        $script:zoom = 1.0
    }

    Update-ZoomUi
    Update-Magnifier
}

function Test-HostZoomModifierDown {
    return (
        ([AHMMirrorChromeNative]::GetAsyncKeyState([AHMMirrorChromeNative]::VK_MENU) -band 0x8000) -ne 0
    )
}

function Reset-TouchpadGesture {
    if ($script:touchpadGestureKind -eq "device") {
        [AHMMirrorChromeNative]::EndScrcpyPinch()
    }

    $script:touchpadGestureKind = "none"
    $script:touchpadStartMetrics = $null
    $script:touchpadLastMetrics = $null
    $script:touchpadStartHostZoom = $script:zoom
    $script:touchpadBaseRadius = 0.0
    $script:touchpadGestureHandled = $false
    $script:touchpadSmoothedScrollX = 0.0
    $script:touchpadSmoothedScrollY = 0.0
}

function Update-TouchpadGesture {
    if ($script:touchpadContacts.Count -lt 2) {
        Reset-TouchpadGesture
        return $false
    }

    $metrics = Get-TouchpadGestureMetrics $script:touchpadContacts
    if ($null -eq $metrics) { return $false }

    if ($null -eq $script:touchpadStartMetrics) {
        $script:touchpadStartMetrics = $metrics
        $script:touchpadLastMetrics = $metrics
        $script:touchpadStartHostZoom = $script:zoom

        $rect = Get-TargetClientRect
        if ($null -ne $rect) {
            $shortSide = [Math]::Min(
                [double]($rect.Right - $rect.Left),
                [double]($rect.Bottom - $rect.Top)
            )
            $script:touchpadBaseRadius = [Math]::Max(
                20.0,
                $shortSide * [double]$ChromeConfig.TouchpadBaseRadiusRelativeToClient
            )
        }

        return $false
    }

    if ($script:touchpadStartMetrics.Distance -le 0.0001) { return $false }

    $scale = $metrics.Distance / $script:touchpadStartMetrics.Distance
    $angleDelta = Normalize-Angle ($metrics.Angle - $script:touchpadStartMetrics.Angle)
    $centerDeltaX = [double]$metrics.CenterX - [double]$script:touchpadStartMetrics.CenterX
    $centerDeltaY = [double]$metrics.CenterY - [double]$script:touchpadStartMetrics.CenterY
    $hostModifierDown = Test-HostZoomModifierDown

    if ($script:touchpadGestureKind -eq "none") {
        $intent = Get-InteractionGestureKind             ([double]$script:touchpadStartMetrics.Distance)             ([double]$metrics.Distance)             $centerDeltaX             $centerDeltaY             ([double]$ChromeConfig.TouchpadPinchThreshold)             ([double]$ChromeConfig.TouchpadPinchDominanceRatio)             ([double]$ChromeConfig.TouchpadScrollThreshold)

        if ($intent -eq "pinch") {
            if (
                $hostModifierDown -and
                $ChromeConfig.TouchpadPinchToHostZoom -and
                $ChromeConfig.HostZoomEnabled
            ) {
                $script:touchpadGestureKind = "host"
                $script:touchpadGestureHandled = $true

                $rect = Get-TargetClientRect
                $cursor = New-Object AHMMirrorChromeNative+POINT
                if ($null -ne $rect -and [AHMMirrorChromeNative]::GetCursorPos([ref]$cursor)) {
                    $width = [Math]::Max(1.0, [double]($rect.Right - $rect.Left))
                    $height = [Math]::Max(1.0, [double]($rect.Bottom - $rect.Top))
                    $script:zoomAnchorX = [Math]::Max(0.0, [Math]::Min(1.0, (($cursor.X - $rect.Left) / $width)))
                    $script:zoomAnchorY = [Math]::Max(0.0, [Math]::Min(1.0, (($cursor.Y - $rect.Top) / $height)))
                }
            }
            elseif ($ChromeConfig.TouchpadPinchToAndroid) {
                $rect = Get-TargetClientRect
                if ($null -ne $rect -and $script:touchpadBaseRadius -gt 0) {
                    $point = Get-SyntheticPinchPoint $rect $script:touchpadBaseRadius 1.0 0.0
                    if ([AHMMirrorChromeNative]::BeginScrcpyPinch($script:targetHwnd, $point.X, $point.Y)) {
                        $script:touchpadGestureKind = "device"
                        $script:touchpadGestureHandled = $true
                    }
                }
            }
        }
        elseif ($intent -eq "scroll") {
            $script:touchpadGestureKind = if (
                $hostModifierDown -and
                (Test-HostZoomActive $script:zoom)
            ) { "host-pan" } else { "scroll" }
            $script:touchpadGestureHandled = $true
        }
    }

    if ($script:touchpadGestureKind -eq "host") {
        $script:zoom = Get-HostZoomFromGesture             $script:touchpadStartHostZoom             $scale             ([double]$ChromeConfig.HostZoomPinchSensitivity)             ([double]$ChromeConfig.MinZoom)             ([double]$ChromeConfig.MaxZoom)
        if ($script:zoom -lt 1.001) { $script:zoom = 1.0 }
        Update-ZoomUi
        Update-Magnifier
        $script:touchpadLastMetrics = $metrics
        return $true
    }

    if ($script:touchpadGestureKind -eq "device") {
        $rect = Get-TargetClientRect
        if ($null -ne $rect) {
            $deviceScale = Get-AndroidPinchScale                 $scale                 ([double]$ChromeConfig.AndroidPinchSensitivity)
            $point = Get-SyntheticPinchPoint $rect $script:touchpadBaseRadius $deviceScale $angleDelta
            [AHMMirrorChromeNative]::UpdateScrcpyPinch($point.X, $point.Y) | Out-Null
        }
        $script:touchpadLastMetrics = $metrics
        return $true
    }

    if ($script:touchpadGestureKind -in @("scroll", "host-pan")) {
        if ($null -eq $script:touchpadLastMetrics) {
            $script:touchpadLastMetrics = $metrics
            return $true
        }

        $rawX = [double]$metrics.CenterX - [double]$script:touchpadLastMetrics.CenterX
        $rawY = [double]$metrics.CenterY - [double]$script:touchpadLastMetrics.CenterY
        $script:touchpadLastMetrics = $metrics

        $script:touchpadSmoothedScrollX = Get-SmoothedInteractionValue             $script:touchpadSmoothedScrollX             $rawX             ([double]$ChromeConfig.TouchpadSmoothing)
        $script:touchpadSmoothedScrollY = Get-SmoothedInteractionValue             $script:touchpadSmoothedScrollY             $rawY             ([double]$ChromeConfig.TouchpadSmoothing)

        if ($script:touchpadGestureKind -eq "host-pan") {
            $rect = Get-TargetClientRect
            if ($null -ne $rect) {
                $pan = Get-NormalizedPanAnchor                     $script:zoomAnchorX                     $script:zoomAnchorY                     $script:touchpadSmoothedScrollX                     $script:touchpadSmoothedScrollY                     ([double]($rect.Right - $rect.Left))                     ([double]($rect.Bottom - $rect.Top))                     $script:zoom                     ([double]$ChromeConfig.HostPanSensitivity)
                $script:zoomAnchorX = $pan.X
                $script:zoomAnchorY = $pan.Y
                Update-Magnifier
            }
            return $true
        }

        $vertical = Get-ScaledScrollDelta             (-$script:touchpadSmoothedScrollY)             ([double]$ChromeConfig.TouchpadScrollSensitivity)             ([double]$ChromeConfig.TouchpadScrollDeadzone)             ([double]$ChromeConfig.TouchpadScrollMaxDeltaPerSample)

        if ([Math]::Abs($vertical) -ge 1.0) {
            [AHMMirrorChromeNative]::SendWheelToTarget(
                $script:targetHwnd,
                [int][Math]::Round($vertical),
                $false
            ) | Out-Null
        }

        return $true
    }

    $script:touchpadLastMetrics = $metrics
    return $false
}

$script:gestureHook = [System.Windows.Interop.HwndSourceHook]{
    param($hwnd, $msg, $wParam, $lParam, [ref]$handled)

    if ($msg -eq [AHMMirrorChromeNative]::WM_NCHITTEST) {
        $handled.Value = $true
        return [IntPtr][AHMMirrorChromeNative]::HTTRANSPARENT
    }

    if (
        -not $script:precisionTouchpadAvailable -or
        $script:targetHwnd -eq [IntPtr]::Zero -or
        [AHMMirrorChromeNative]::GetForegroundWindow() -ne $script:targetHwnd
    ) {
        return [IntPtr]::Zero
    }

    if (
        $msg -eq [AHMMirrorChromeNative]::WM_POINTERDOWN -or
        $msg -eq [AHMMirrorChromeNative]::WM_POINTERUPDATE -or
        $msg -eq [AHMMirrorChromeNative]::WM_POINTERUP
    ) {
        $pointerId = [uint32]($wParam.ToInt64() -band 0xFFFF)
        $sample = New-Object AHMMirrorChromeNative+TOUCHPAD_SAMPLE

        if ([AHMMirrorChromeNative]::TryGetTouchpadSample($pointerId, [ref]$sample)) {
            $key = [string]$pointerId

            if ($msg -eq [AHMMirrorChromeNative]::WM_POINTERUP) {
                if ($script:touchpadContacts.ContainsKey($key)) {
                    $script:touchpadContacts.Remove($key)
                }

                $wasHandled = $script:touchpadGestureHandled
                if ($script:touchpadContacts.Count -lt 2) {
                    Reset-TouchpadGesture
                }

                if ($wasHandled) {
                    $handled.Value = $true
                }
            }
            else {
                $script:touchpadContacts[$key] = [pscustomobject]@{
                    X = [double]$sample.X
                    Y = [double]$sample.Y
                }

                if (Update-TouchpadGesture) {
                    $handled.Value = $true
                }
            }
        }
    }

    return [IntPtr]::Zero
}

if ($null -ne $gestureSource) {
    $gestureSource.AddHook($script:gestureHook)
}

function Get-RuntimeDirectory {
    $safeSerial = ($Serial -replace '[^A-Za-z0-9._-]', '_')
    return Join-Path (Join-Path $Root "runtime") $safeSerial
}

function Write-MirrorChromeState {
    try {
        $directory = Get-RuntimeDirectory
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
        $path = Join-Path $directory "mirror-chrome-state.json"
        $temp = $path + ".tmp"

        [pscustomobject]@{
            Version = 1
            Zoom = [Math]::Round([double]$script:zoom, 4)
            ZoomActive = [bool](Test-HostZoomActive $script:zoom)
            AnchorX = [Math]::Round([double]$script:zoomAnchorX, 4)
            AnchorY = [Math]::Round([double]$script:zoomAnchorY, 4)
            NavigatorVisible = [bool](
                [bool]$ChromeConfig.ShowZoomMinimap -and
                (Test-HostZoomActive $script:zoom)
            )
            UpdatedAt = [DateTime]::UtcNow.ToString("o")
        } | ConvertTo-Json -Compress | Set-Content -Path $temp -Encoding UTF8

        Move-Item -Force -Path $temp -Destination $path
    }
    catch {
        # Zoom state is advisory UI metadata; mirroring must continue if the
        # local runtime state cannot be written.
    }
}

function Open-ControlCenter {
    $controlCenterScript = Join-Path $Root "ControlCenter.ps1"
    if (-not (Test-Path $controlCenterScript)) { return }

    try {
        if ($null -ne $script:controlCenterProcess -and -not $script:controlCenterProcess.HasExited) {
            return
        }
    }
    catch {}

    $quote = [char]34
    $arguments =
        "-NoLogo -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " +
        $quote + $controlCenterScript + $quote +
        " -Serial " + $quote + $Serial + $quote

    try {
        $script:controlCenterProcess = Start-Process -FilePath "powershell.exe" -ArgumentList $arguments -PassThru -WindowStyle Hidden
    }
    catch {
        $script:controlCenterProcess = $null
    }
}

function Poll-MirrorChromeCommand {
    $directory = Get-RuntimeDirectory
    $path = Join-Path $directory "mirror-chrome.command"
    if (-not (Test-Path $path)) { return }

    try {
        $command = (Get-Content $path -Raw).Trim().ToLowerInvariant()
    }
    catch {
        return
    }
    finally {
        Remove-Item -Force $path -ErrorAction SilentlyContinue
    }

    switch ($command) {
        "zoom-in" { Apply-ZoomDelta 120 }
        "zoom-out" { Apply-ZoomDelta -120 }
        "reset-zoom" { Reset-HostZoom }
        "open-controls" { Open-ControlCenter }
    }
}

$controlsButton.Add_Click({
    Open-ControlCenter
})

$sleepButton.Add_Click({
    if ($script:targetHwnd -eq [IntPtr]::Zero) { return }

    # Use the same hardened delivery path as the Control Center. The scrcpy
    # launch contract pins MOD to left Alt.
    Invoke-ScrcpyNamedShortcut         -WindowTitle $WindowTitle         -Name "sleep" | Out-Null
})

$resetZoomButton.Add_Click({
    Reset-HostZoom
})

$timer = New-Object System.Windows.Threading.DispatcherTimer
$timer.Interval = [TimeSpan]::FromMilliseconds([Math]::Max(12, [int]$ChromeConfig.PollMilliseconds))

$timer.Add_Tick({
    $now = Get-Date

    if ($script:targetHwnd -eq [IntPtr]::Zero -or -not [AHMMirrorChromeNative]::IsWindow($script:targetHwnd)) {
        $script:targetHwnd = [AHMMirrorChromeNative]::FindWindowByExactTitle($WindowTitle)

        if ($script:targetHwnd -ne [IntPtr]::Zero) {
            $script:targetSeen = $true
            $script:missingSince = $null

            if (
                -not $script:controlCenterAutoOpened -and
                $null -ne $Config.PSObject.Properties["ControlCenter"] -and
                [bool]$Config.ControlCenter.Enabled -and
                [bool]$Config.ControlCenter.OpenOnLaunch
            ) {
                $script:controlCenterAutoOpened = $true
                Open-ControlCenter
            }

            if ($ChromeConfig.WheelToHostZoom -and -not $script:wheelHookStarted) {
                $script:wheelHookStarted = [AHMMirrorChromeNative]::StartWheelHook($script:targetHwnd)
            }
            elseif ($script:wheelHookStarted) {
                [AHMMirrorChromeNative]::UpdateWheelTarget($script:targetHwnd)
            }
        }
    }

    if ($script:targetHwnd -ne [IntPtr]::Zero -and [AHMMirrorChromeNative]::IsWindow($script:targetHwnd)) {
        $script:missingSince = $null

        if (($now - $script:lastCommandPoll).TotalMilliseconds -ge 100) {
            $script:lastCommandPoll = $now
            Poll-MirrorChromeCommand
        }

        $rect = Get-TargetClientRect
        if ($null -ne $rect) {
            $width = [Math]::Max(1, $rect.Right - $rect.Left)
            $height = [Math]::Max(1, $rect.Bottom - $rect.Top)

            if ($script:precisionTouchpadAvailable) {
                if (-not $gestureWindow.IsVisible) {
                    $gestureWindow.Show()
                }

                [AHMMirrorChromeNative]::SetWindowPos(
                    $gestureHwnd,
                    [AHMMirrorChromeNative]::HWND_TOPMOST,
                    $rect.Left,
                    $rect.Top,
                    $width,
                    $height,
                    [AHMMirrorChromeNative]::SWP_NOACTIVATE -bor
                    [AHMMirrorChromeNative]::SWP_NOSENDCHANGING -bor
                    [AHMMirrorChromeNative]::SWP_SHOWWINDOW
                ) | Out-Null
            }

            if (-not $toolbar.IsVisible) {
                $toolbar.Show()
            }

            $toolbar.UpdateLayout()
            $dpi = [System.Windows.Media.VisualTreeHelper]::GetDpi($toolbar)
            $toolbarWidth = [Math]::Max(1, [int][Math]::Ceiling($toolbar.ActualWidth * $dpi.DpiScaleX))
            $toolbarHeight = [Math]::Max(1, [int][Math]::Ceiling($toolbar.ActualHeight * $dpi.DpiScaleY))
            $inset = [int][Math]::Round([double]$ChromeConfig.ToolbarInsetPixels)

            [AHMMirrorChromeNative]::SetWindowPos(
                $toolbarHwnd,
                [AHMMirrorChromeNative]::HWND_TOPMOST,
                $rect.Right - $toolbarWidth - $inset,
                $rect.Top + $inset,
                $toolbarWidth,
                $toolbarHeight,
                [AHMMirrorChromeNative]::SWP_NOACTIVATE -bor
                [AHMMirrorChromeNative]::SWP_NOSENDCHANGING -bor
                [AHMMirrorChromeNative]::SWP_SHOWWINDOW
            ) | Out-Null

            $rectKey = "$($rect.Left),$($rect.Top),$width,$height"
            if ($rectKey -ne $script:lastRectKey) {
                $script:lastRectKey = $rectKey
            }

            if (Test-HostZoomActive $script:zoom) {
                Update-Magnifier
                Update-ZoomNavigator
            }
        }

        if ($script:wheelHookStarted) {
            $wheelDelta = 0
            while ([AHMMirrorChromeNative]::TryDequeueWheel([ref]$wheelDelta)) {
                Apply-ZoomDelta $wheelDelta
                $wheelDelta = 0
            }
        }
    }
    elseif ($script:targetSeen) {
        if ($null -eq $script:missingSince) {
            $script:missingSince = $now
        }
        elseif (($now - $script:missingSince).TotalMilliseconds -gt 3000) {
            $timer.Stop()
            $toolbar.Close()
            return
        }
    }
})

$toolbar.Add_Closed({
    $timer.Stop()
    Reset-TouchpadGesture

    if ($null -ne $gestureSource -and $null -ne $script:gestureHook) {
        $gestureSource.RemoveHook($script:gestureHook)
    }

    if ($script:precisionTouchpadAvailable) {
        [AHMMirrorChromeNative]::RegisterPrecisionTouchpadWindow($gestureHwnd, $false) | Out-Null
    }

    if ($gestureWindow.IsVisible) {
        $gestureWindow.Close()
    }

    if ($navigatorWindow.IsVisible) {
        $navigatorWindow.Close()
    }
    else {
        try { $navigatorWindow.Close() } catch {}
    }

    if ($script:wheelHookStarted) {
        [AHMMirrorChromeNative]::StopWheelHook()
        $script:wheelHookStarted = $false
    }

    if ($script:magnifierChild -ne [IntPtr]::Zero) {
        [AHMMirrorChromeNative]::DestroyWindow($script:magnifierChild) | Out-Null
        $script:magnifierChild = [IntPtr]::Zero
    }

    if ($script:magnifierHost -ne [IntPtr]::Zero) {
        [AHMMirrorChromeNative]::DestroyWindow($script:magnifierHost) | Out-Null
        $script:magnifierHost = [IntPtr]::Zero
    }

    [AHMMirrorChromeNative]::UninitializeMagnification()

    try {
        if ($null -ne $script:controlCenterProcess -and -not $script:controlCenterProcess.HasExited) {
            Stop-Process -Id $script:controlCenterProcess.Id -Force -ErrorAction SilentlyContinue
        }
    }
    catch {}
})

Update-ZoomUi
$timer.Start()
[System.Windows.Threading.Dispatcher]::Run()
