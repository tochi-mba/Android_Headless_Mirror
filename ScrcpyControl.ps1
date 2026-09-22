Set-StrictMode -Version Latest

$script:ScrcpyControlNativeLoaded = $false

function Initialize-ScrcpyControlNative {
    if ($script:ScrcpyControlNativeLoaded) { return }

    $source = @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class AHMScrcpyControlNative
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint type; public InputUnion U; }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; }

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
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_SYSKEYDOWN = 0x0104;
    public const uint WM_SYSKEYUP = 0x0105;

    public const ushort VK_LMENU = 0xA4;
    public const ushort VK_LSHIFT = 0xA0;
    public const ushort VK_LEFT = 0x25;
    public const ushort VK_UP = 0x26;
    public const ushort VK_RIGHT = 0x27;
    public const ushort VK_DOWN = 0x28;
    public const ushort VK_F11 = 0x7A;

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public static IntPtr FindWindowByExactTitle(string title)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr hwnd, IntPtr lParam)
        {
            if (!IsWindowVisible(hwnd)) return true;
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

    public static IntPtr FindWindowByTitlePrefix(string prefix)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr hwnd, IntPtr lParam)
        {
            if (!IsWindowVisible(hwnd)) return true;
            StringBuilder builder = new StringBuilder(512);
            GetWindowText(hwnd, builder, builder.Capacity);
            string title = builder.ToString();
            if (!String.IsNullOrEmpty(title) &&
                title.StartsWith(prefix, StringComparison.Ordinal))
            {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static INPUT Key(ushort vk, bool up)
    {
        INPUT input = new INPUT();
        input.type = INPUT_KEYBOARD;
        input.U.ki.wVk = vk;
        input.U.ki.dwFlags = up ? KEYEVENTF_KEYUP : 0;
        return input;
    }

    private static bool TryFocusTarget(IntPtr target)
    {
        if (target == IntPtr.Zero) return false;
        if (GetForegroundWindow() == target) return true;

        uint currentThread = GetCurrentThreadId();
        uint targetThread = GetWindowThreadProcessId(target, IntPtr.Zero);
        IntPtr foreground = GetForegroundWindow();
        uint foregroundThread = foreground == IntPtr.Zero
            ? 0
            : GetWindowThreadProcessId(foreground, IntPtr.Zero);

        bool attachedTarget = false;
        bool attachedForeground = false;

        try
        {
            if (targetThread != 0 && targetThread != currentThread)
            {
                attachedTarget = AttachThreadInput(currentThread, targetThread, true);
            }

            if (
                foregroundThread != 0 &&
                foregroundThread != currentThread &&
                foregroundThread != targetThread
            )
            {
                attachedForeground = AttachThreadInput(currentThread, foregroundThread, true);
            }

            BringWindowToTop(target);
            SetActiveWindow(target);
            SetFocus(target);
            SetForegroundWindow(target);
            return GetForegroundWindow() == target;
        }
        finally
        {
            if (attachedForeground)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }
            if (attachedTarget)
            {
                AttachThreadInput(currentThread, targetThread, false);
            }
        }
    }

    private static INPUT[] BuildShortcutInputs(
        bool alt,
        bool shift,
        ushort key,
        int repeat)
    {
        if (repeat < 1) repeat = 1;

        int count = (alt ? 2 : 0) + (shift ? 2 : 0) + (repeat * 2);
        INPUT[] inputs = new INPUT[count];
        int index = 0;

        // Keep MOD held across repeated key presses. scrcpy requires this for
        // shortcuts such as MOD+n+n (expand Quick Settings).
        if (alt) inputs[index++] = Key(VK_LMENU, false);
        if (shift) inputs[index++] = Key(VK_LSHIFT, false);

        for (int i = 0; i < repeat; i++)
        {
            inputs[index++] = Key(key, false);
            inputs[index++] = Key(key, true);
        }

        if (shift) inputs[index++] = Key(VK_LSHIFT, true);
        if (alt) inputs[index++] = Key(VK_LMENU, true);

        return inputs;
    }

    private static bool PostShortcut(
        IntPtr target,
        bool alt,
        bool shift,
        ushort key,
        int repeat)
    {
        if (target == IntPtr.Zero) return false;
        if (repeat < 1) repeat = 1;

        bool ok = true;

        if (alt)
        {
            ok &= PostMessage(target, WM_SYSKEYDOWN, new IntPtr(VK_LMENU), IntPtr.Zero);
        }
        if (shift)
        {
            ok &= PostMessage(target, WM_KEYDOWN, new IntPtr(VK_LSHIFT), IntPtr.Zero);
        }

        for (int i = 0; i < repeat; i++)
        {
            uint downMessage = alt ? WM_SYSKEYDOWN : WM_KEYDOWN;
            uint upMessage = alt ? WM_SYSKEYUP : WM_KEYUP;
            ok &= PostMessage(target, downMessage, new IntPtr(key), IntPtr.Zero);
            ok &= PostMessage(target, upMessage, new IntPtr(key), IntPtr.Zero);
        }

        if (shift)
        {
            ok &= PostMessage(target, WM_KEYUP, new IntPtr(VK_LSHIFT), IntPtr.Zero);
        }
        if (alt)
        {
            ok &= PostMessage(target, WM_SYSKEYUP, new IntPtr(VK_LMENU), IntPtr.Zero);
        }

        return ok;
    }

    public static bool SendShortcut(IntPtr target, bool alt, bool shift, ushort key, int repeat)
    {
        if (target == IntPtr.Zero) return false;

        TryFocusTarget(target);

        INPUT[] inputs = BuildShortcutInputs(alt, shift, key, repeat);
        uint sent = SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf(typeof(INPUT))
        );

        if (sent == inputs.Length)
        {
            return true;
        }

        // SendInput can be rejected by Windows focus/UIPI rules. A targeted
        // message fallback is preferable to reporting a false failure when the
        // scrcpy window is known.
        return PostShortcut(target, alt, shift, key, repeat);
    }
}
'@

    Add-Type -TypeDefinition $source -ErrorAction Stop
    $script:ScrcpyControlNativeLoaded = $true
}

function Convert-ToVirtualKey([string]$Character) {
    if ($Character.Length -ne 1) { throw "Expected one character." }
    return [int][char]$Character.ToUpperInvariant()
}

function Get-ScrcpyShortcutMap {
    Initialize-ScrcpyControlNative

    return @{
        fullscreen       = @{ Key=[AHMScrcpyControlNative]::VK_F11; Alt=$false; Shift=$false; Repeat=1 }
        fit              = @{ Key=(Convert-ToVirtualKey "w"); Alt=$true; Shift=$false; Repeat=1 }
        "pixel-perfect" = @{ Key=(Convert-ToVirtualKey "g"); Alt=$true; Shift=$false; Repeat=1 }
        "rotate-left" = @{ Key=[AHMScrcpyControlNative]::VK_LEFT; Alt=$true; Shift=$false; Repeat=1 }
        "rotate-right" = @{ Key=[AHMScrcpyControlNative]::VK_RIGHT; Alt=$true; Shift=$false; Repeat=1 }
        "flip-horizontal" = @{ Key=[AHMScrcpyControlNative]::VK_LEFT; Alt=$true; Shift=$true; Repeat=1 }
        "flip-vertical" = @{ Key=[AHMScrcpyControlNative]::VK_UP; Alt=$true; Shift=$true; Repeat=1 }
        pause            = @{ Key=(Convert-ToVirtualKey "z"); Alt=$true; Shift=$false; Repeat=1 }
        resume           = @{ Key=(Convert-ToVirtualKey "z"); Alt=$true; Shift=$true; Repeat=1 }
        "reset-capture" = @{ Key=(Convert-ToVirtualKey "r"); Alt=$true; Shift=$true; Repeat=1 }
        fps              = @{ Key=(Convert-ToVirtualKey "i"); Alt=$true; Shift=$false; Repeat=1 }
        home             = @{ Key=(Convert-ToVirtualKey "h"); Alt=$true; Shift=$false; Repeat=1 }
        back             = @{ Key=(Convert-ToVirtualKey "b"); Alt=$true; Shift=$false; Repeat=1 }
        apps             = @{ Key=(Convert-ToVirtualKey "s"); Alt=$true; Shift=$false; Repeat=1 }
        menu             = @{ Key=(Convert-ToVirtualKey "m"); Alt=$true; Shift=$false; Repeat=1 }
        power            = @{ Key=(Convert-ToVirtualKey "p"); Alt=$true; Shift=$false; Repeat=1 }
        sleep            = @{ Key=(Convert-ToVirtualKey "o"); Alt=$true; Shift=$false; Repeat=1 }
        wake             = @{ Key=(Convert-ToVirtualKey "o"); Alt=$true; Shift=$true; Repeat=1 }
        "rotate-device" = @{ Key=(Convert-ToVirtualKey "r"); Alt=$true; Shift=$false; Repeat=1 }
        notifications    = @{ Key=(Convert-ToVirtualKey "n"); Alt=$true; Shift=$false; Repeat=1 }
        "quick-settings" = @{ Key=(Convert-ToVirtualKey "n"); Alt=$true; Shift=$false; Repeat=2 }
        "collapse-panels" = @{ Key=(Convert-ToVirtualKey "n"); Alt=$true; Shift=$true; Repeat=1 }
        "volume-down" = @{ Key=[AHMScrcpyControlNative]::VK_DOWN; Alt=$true; Shift=$false; Repeat=1 }
        "volume-up" = @{ Key=[AHMScrcpyControlNative]::VK_UP; Alt=$true; Shift=$false; Repeat=1 }
        copy             = @{ Key=(Convert-ToVirtualKey "c"); Alt=$true; Shift=$false; Repeat=1 }
        cut              = @{ Key=(Convert-ToVirtualKey "x"); Alt=$true; Shift=$false; Repeat=1 }
        "paste-sync" = @{ Key=(Convert-ToVirtualKey "v"); Alt=$true; Shift=$false; Repeat=1 }
        "paste-inject" = @{ Key=(Convert-ToVirtualKey "v"); Alt=$true; Shift=$true; Repeat=1 }
        "keyboard-settings" = @{ Key=(Convert-ToVirtualKey "k"); Alt=$true; Shift=$false; Repeat=1 }
    }
}

function Get-ScrcpyWindowRect([string]$WindowTitle) {
    Initialize-ScrcpyControlNative
    $target = [AHMScrcpyControlNative]::FindWindowByExactTitle($WindowTitle)
    if ($target -eq [IntPtr]::Zero) { return $null }

    $rect = New-Object AHMScrcpyControlNative+RECT
    if (-not [AHMScrcpyControlNative]::GetWindowRect($target, [ref]$rect)) {
        return $null
    }

    return [pscustomobject]@{
        Left = $rect.Left
        Top = $rect.Top
        Right = $rect.Right
        Bottom = $rect.Bottom
        Width = $rect.Right - $rect.Left
        Height = $rect.Bottom - $rect.Top
    }
}

function Focus-ScrcpyWindow([string]$WindowTitlePrefix, [switch]$TestMode) {
    Initialize-ScrcpyControlNative

    if ($TestMode) {
        return [pscustomobject]@{ Ok=$true; Text="Test focus scrcpy window." }
    }

    $target = [AHMScrcpyControlNative]::FindWindowByTitlePrefix($WindowTitlePrefix)
    if ($target -eq [IntPtr]::Zero) {
        return [pscustomobject]@{ Ok=$false; Text="No matching scrcpy window is running." }
    }

    $ok = [AHMScrcpyControlNative]::SetForegroundWindow($target)
    return [pscustomobject]@{
        Ok = $ok
        Text = if ($ok) { "Mirror focused." } else { "Windows did not allow the mirror to be focused." }
    }
}

function Invoke-ScrcpyNamedShortcut {
    param(
        [Parameter(Mandatory = $true)][string]$WindowTitle,
        [Parameter(Mandatory = $true)][string]$Name,
        [switch]$TestMode
    )

    $map = Get-ScrcpyShortcutMap
    if (-not $map.ContainsKey($Name)) {
        return [pscustomobject]@{ Ok=$false; Text="Unknown scrcpy shortcut '$Name'." }
    }

    if ($TestMode) {
        return [pscustomobject]@{ Ok=$true; Text="Test scrcpy shortcut: $Name" }
    }

    $target = [AHMScrcpyControlNative]::FindWindowByExactTitle($WindowTitle)
    if ($target -eq [IntPtr]::Zero) {
        return [pscustomobject]@{ Ok=$false; Text="The matching scrcpy window is not running." }
    }

    $spec = $map[$Name]
    $ok = [AHMScrcpyControlNative]::SendShortcut(
        $target,
        [bool]$spec.Alt,
        [bool]$spec.Shift,
        [uint16]$spec.Key,
        [int]$spec.Repeat
    )

    return [pscustomobject]@{
        Ok = $ok
        Text = if ($ok) {
            $Name
        }
        else {
            "Could not deliver '$Name' to the scrcpy window. REX tried focused SendInput and a targeted window-message fallback."
        }
    }
}
