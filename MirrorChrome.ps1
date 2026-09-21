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

    public const ushort VK_LMENU = 0xA4;
    public const ushort VK_O = 0x4F;

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

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (
            nCode >= 0 &&
            wParam.ToInt32() == WM_MOUSEWHEEL &&
            wheelTarget != IntPtr.Zero &&
            GetForegroundWindow() == wheelTarget &&
            (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0
        )
        {
            MSLLHOOKSTRUCT data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
            int delta = (short)((data.mouseData >> 16) & 0xffff);
            wheelDeltas.Enqueue(delta);
            return new IntPtr(1);
        }

        return CallNextHookEx(hook, nCode, wParam, lParam);
    }

    public static bool SendScrcpyScreenOffShortcut(IntPtr target)
    {
        if (target == IntPtr.Zero)
        {
            return false;
        }

        SetForegroundWindow(target);

        INPUT[] inputs = new INPUT[4];

        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].U.ki.wVk = VK_LMENU;

        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].U.ki.wVk = VK_O;

        inputs[2].type = INPUT_KEYBOARD;
        inputs[2].U.ki.wVk = VK_O;
        inputs[2].U.ki.dwFlags = KEYEVENTF_KEYUP;

        inputs[3].type = INPUT_KEYBOARD;
        inputs[3].U.ki.wVk = VK_LMENU;
        inputs[3].U.ki.dwFlags = KEYEVENTF_KEYUP;

        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))) == inputs.Length;
    }
}
'@

Add-Type -TypeDefinition $nativeSource -ErrorAction Stop

if ($TestOnly) {
    return
}

Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

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

$sleepButton = New-ToolbarButton "Sleep phone"
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

function Get-TargetClientRect {
    if ($script:targetHwnd -eq [IntPtr]::Zero) { return $null }

    $rect = New-Object AHMMirrorChromeNative+RECT
    if (-not [AHMMirrorChromeNative]::TryGetClientScreenRect($script:targetHwnd, [ref]$rect)) {
        return $null
    }

    return $rect
}

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
        @($script:magnifierHost, $toolbarHwnd)
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

    if ($script:zoom -gt 1.001) {
        $resetZoomButton.Visibility = [System.Windows.Visibility]::Visible
    }
    else {
        $resetZoomButton.Visibility = [System.Windows.Visibility]::Collapsed
    }
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

$sleepButton.Add_Click({
    if ($script:targetHwnd -eq [IntPtr]::Zero) { return }

    # scrcpy's own MOD+O action turns the Android physical display off while
    # keeping video mirroring active. Default MOD includes Left Alt.
    [AHMMirrorChromeNative]::SendScrcpyScreenOffShortcut($script:targetHwnd) | Out-Null
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

            if ($ChromeConfig.CtrlWheelZoom -and -not $script:wheelHookStarted) {
                $script:wheelHookStarted = [AHMMirrorChromeNative]::StartWheelHook($script:targetHwnd)
            }
            elseif ($script:wheelHookStarted) {
                [AHMMirrorChromeNative]::UpdateWheelTarget($script:targetHwnd)
            }
        }
    }

    if ($script:targetHwnd -ne [IntPtr]::Zero -and [AHMMirrorChromeNative]::IsWindow($script:targetHwnd)) {
        $script:missingSince = $null

        $rect = Get-TargetClientRect
        if ($null -ne $rect) {
            $width = [Math]::Max(1, $rect.Right - $rect.Left)
            $height = [Math]::Max(1, $rect.Bottom - $rect.Top)

            if (-not $toolbar.IsVisible) {
                $toolbar.Show()
            }

            $toolbar.UpdateLayout()
            $inset = [double]$ChromeConfig.ToolbarInsetPixels
            $toolbar.Left = $rect.Right - $toolbar.ActualWidth - $inset
            $toolbar.Top = $rect.Top + $inset

            $rectKey = "$($rect.Left),$($rect.Top),$width,$height"
            if ($rectKey -ne $script:lastRectKey) {
                $script:lastRectKey = $rectKey
                Update-Magnifier
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
})

Update-ZoomUi
$timer.Start()
[System.Windows.Threading.Dispatcher]::Run()
