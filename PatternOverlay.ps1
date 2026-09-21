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

function Get-KeyguardStateFromText([string]$WindowText, [string]$TrustText) {
    $combined = (($WindowText + [Environment]::NewLine + $TrustText))

    if ($combined -match '(?im)\b(mShowingLockscreen|mKeyguardShowing|isStatusBarKeyguard|keyguardShowing|deviceLocked)\s*[=:]\s*(true|1)\b') {
        return "locked"
    }

    if ($combined -match '(?im)\b(mShowingLockscreen|mKeyguardShowing|isStatusBarKeyguard|keyguardShowing|deviceLocked)\s*[=:]\s*(false|0)\b') {
        return "unlocked"
    }

    return "unknown"
}

function Get-FittedContentRect(
    [double]$ClientWidth,
    [double]$ClientHeight,
    [double]$DeviceWidth,
    [double]$DeviceHeight
) {
    if ($ClientWidth -le 0 -or $ClientHeight -le 0) {
        return [pscustomobject]@{ X = 0.0; Y = 0.0; Width = 0.0; Height = 0.0 }
    }

    if ($DeviceWidth -le 0 -or $DeviceHeight -le 0) {
        return [pscustomobject]@{
            X = 0.0
            Y = 0.0
            Width = $ClientWidth
            Height = $ClientHeight
        }
    }

    $clientRatio = $ClientWidth / $ClientHeight
    $portraitRatio = $DeviceWidth / $DeviceHeight
    $landscapeRatio = $DeviceHeight / $DeviceWidth

    $portraitError = [Math]::Abs([Math]::Log($clientRatio / $portraitRatio))
    $landscapeError = [Math]::Abs([Math]::Log($clientRatio / $landscapeRatio))

    if ($landscapeError -lt $portraitError) {
        $sourceWidth = $DeviceHeight
        $sourceHeight = $DeviceWidth
    }
    else {
        $sourceWidth = $DeviceWidth
        $sourceHeight = $DeviceHeight
    }

    $scale = [Math]::Min($ClientWidth / $sourceWidth, $ClientHeight / $sourceHeight)
    $width = $sourceWidth * $scale
    $height = $sourceHeight * $scale

    return [pscustomobject]@{
        X = ($ClientWidth - $width) / 2.0
        Y = ($ClientHeight - $height) / 2.0
        Width = $width
        Height = $height
    }
}

function Get-PatternGridPoints($ContentRect, $OverlayConfig) {
    $span = [double]$ContentRect.Width * [double]$OverlayConfig.GridSizeRelativeToWidth
    $span = [Math]::Min($span, [double]$ContentRect.Height * 0.62)

    $centerX = [double]$ContentRect.X + ([double]$ContentRect.Width * [double]$OverlayConfig.GridCenterX)
    $centerY = [double]$ContentRect.Y + ([double]$ContentRect.Height * [double]$OverlayConfig.GridCenterY)

    $half = $span / 2.0
    $xs = @($centerX - $half, $centerX, $centerX + $half)
    $ys = @($centerY - $half, $centerY, $centerY + $half)

    $points = @()
    foreach ($y in $ys) {
        foreach ($x in $xs) {
            $points += [pscustomobject]@{ X = [double]$x; Y = [double]$y }
        }
    }

    return $points
}

function Get-HotkeySpec([string]$Text) {
    $parts = @($Text -split '\+' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $required = @()
    $key = $null

    foreach ($part in $parts) {
        switch -Regex ($part) {
            '^(?i:ctrl|control)$' { $required += 0x11; continue }
            '^(?i:alt)$'          { $required += 0x12; continue }
            '^(?i:shift)$'        { $required += 0x10; continue }
            '^(?i:win|windows)$'  { $required += 0x5B; continue }
            '^[A-Za-z0-9]$'       { $key = [int][char]$part.ToUpperInvariant(); continue }
            '^F([1-9]|1[0-2])$' {
                $number = [int]$Matches[1]
                $key = 0x70 + ($number - 1)
                continue
            }
            default { return $null }
        }
    }

    if ($null -eq $key) { return $null }

    return [pscustomobject]@{
        Modifiers = @($required)
        Key = [int]$key
    }
}

if ($TestOnly) {
    return
}

if (-not (Test-Path $ConfigPath)) {
    exit 2
}

$Config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
if ($null -eq $Config.PSObject.Properties["PatternOverlay"] -or -not $Config.PatternOverlay.Enabled) {
    exit 0
}

$OverlayConfig = $Config.PatternOverlay

$adb = Get-ChildItem (Join-Path $Root "tools") -Filter "adb.exe" -Recurse -File -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty FullName
if ([string]::IsNullOrWhiteSpace([string]$adb)) {
    exit 3
}

$WindowTitle = "{0} [{1}]" -f ([string]$Config.WindowTitle), $Serial

Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

$nativeSource = @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class AHMOverlayNative
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

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

    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TRANSPARENT = 0x00000020L;
    public const long WS_EX_TOOLWINDOW = 0x00000080L;
    public const long WS_EX_NOACTIVATE = 0x08000000L;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_NOSENDCHANGING = 0x0400;
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

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
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

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

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex)
            : GetWindowLongPtr32(hWnd, nIndex);
    }

    public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, value)
            : SetWindowLongPtr32(hWnd, nIndex, value);
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
}
'@

Add-Type -TypeDefinition $nativeSource -ErrorAction Stop

try {
    [AHMOverlayNative]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
}
catch {}

function Get-DeviceDisplaySize {
    try {
        $output = (& $adb -s $Serial shell wm size 2>$null | Out-String)
        $matches = [regex]::Matches($output, '(?im)(Override|Physical) size:\s*(\d+)x(\d+)')
        if ($matches.Count -gt 0) {
            $chosen = $matches[$matches.Count - 1]
            return [pscustomobject]@{
                Width = [double]$chosen.Groups[2].Value
                Height = [double]$chosen.Groups[3].Value
            }
        }
    }
    catch {}

    return [pscustomobject]@{ Width = 0.0; Height = 0.0 }
}

function Get-KeyguardState {
    $windowText = ""
    $trustText = ""

    try {
        $windowText = (& $adb -s $Serial shell dumpsys window 2>$null | Out-String)
    }
    catch {}

    $state = Get-KeyguardStateFromText $windowText ""
    if ($state -ne "unknown") {
        return $state
    }

    try {
        $trustText = (& $adb -s $Serial shell dumpsys trust 2>$null | Out-String)
    }
    catch {}

    return Get-KeyguardStateFromText $windowText $trustText
}

function Test-KeyDown([int]$VirtualKey) {
    return (([AHMOverlayNative]::GetAsyncKeyState($VirtualKey) -band 0x8000) -ne 0)
}

function Test-HotkeyDown($Spec) {
    if ($null -eq $Spec) { return $false }

    foreach ($modifier in @($Spec.Modifiers)) {
        if (-not (Test-KeyDown ([int]$modifier))) {
            return $false
        }
    }

    return Test-KeyDown ([int]$Spec.Key)
}

$window = New-Object System.Windows.Window
$window.WindowStyle = [System.Windows.WindowStyle]::None
$window.ResizeMode = [System.Windows.ResizeMode]::NoResize
$window.AllowsTransparency = $true
$window.Background = [System.Windows.Media.Brushes]::Transparent
$window.ShowInTaskbar = $false
$window.ShowActivated = $false
$window.Topmost = $true
$window.Focusable = $false

$canvas = New-Object System.Windows.Controls.Canvas
$canvas.Background = [System.Windows.Media.Brushes]::Transparent
$canvas.IsHitTestVisible = $false
$window.Content = $canvas

$trail = New-Object System.Windows.Shapes.Polyline
$trail.Stroke = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#FF774D")
$trail.StrokeThickness = 3.0
$trail.Opacity = [Math]::Min(1.0, [Math]::Max(0.2, [double]$OverlayConfig.Opacity))
$trail.StrokeStartLineCap = [System.Windows.Media.PenLineCap]::Round
$trail.StrokeEndLineCap = [System.Windows.Media.PenLineCap]::Round
$trail.StrokeLineJoin = [System.Windows.Media.PenLineJoin]::Round

$window.Show()
$overlayHwnd = (New-Object System.Windows.Interop.WindowInteropHelper($window)).Handle
$style = [AHMOverlayNative]::GetWindowLongPtr($overlayHwnd, [AHMOverlayNative]::GWL_EXSTYLE).ToInt64()
$style = $style -bor [AHMOverlayNative]::WS_EX_TRANSPARENT -bor [AHMOverlayNative]::WS_EX_TOOLWINDOW -bor [AHMOverlayNative]::WS_EX_NOACTIVATE
[AHMOverlayNative]::SetWindowLongPtr($overlayHwnd, [AHMOverlayNative]::GWL_EXSTYLE, [IntPtr]$style) | Out-Null
$window.Hide()

$hotkey = Get-HotkeySpec ([string]$OverlayConfig.ManualToggleHotkey)
$displaySize = Get-DeviceDisplaySize
$targetHwnd = [IntPtr]::Zero
$targetSeen = $false
$missingSince = $null
$lastWindowPoll = [DateTime]::MinValue
$lastKeyguardPoll = [DateTime]::MinValue
$keyguardState = "unknown"
$manualOverride = $null
$manualOverrideUntil = [DateTime]::MinValue
$hotkeyWasDown = $false
$leftWasDown = $false
$trailClearAt = [DateTime]::MinValue
$lastBoundsKey = ""

function Update-Grid {
    $canvas.Children.Clear()
    $canvas.Children.Add($trail) | Out-Null

    $contentRect = Get-FittedContentRect $canvas.ActualWidth $canvas.ActualHeight $displaySize.Width $displaySize.Height
    $points = @(Get-PatternGridPoints $contentRect $OverlayConfig)
    $radius = [Math]::Max(5.0, [double]$contentRect.Width * [double]$OverlayConfig.DotRadiusRelativeToWidth)

    foreach ($point in $points) {
        $dot = New-Object System.Windows.Shapes.Ellipse
        $dot.Width = $radius * 2.0
        $dot.Height = $radius * 2.0
        $dot.Fill = [System.Windows.Media.Brushes]::Transparent
        $dot.Stroke = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#D7FF3F")
        $dot.StrokeThickness = [Math]::Max(2.0, $radius * 0.22)
        $dot.Opacity = [Math]::Min(1.0, [Math]::Max(0.25, [double]$OverlayConfig.Opacity))
        $dot.IsHitTestVisible = $false

        [System.Windows.Controls.Canvas]::SetLeft($dot, $point.X - $radius)
        [System.Windows.Controls.Canvas]::SetTop($dot, $point.Y - $radius)
        $canvas.Children.Add($dot) | Out-Null
    }

    $label = New-Object System.Windows.Controls.TextBlock
    $label.Text = "PATTERN GUIDE  •  " + [string]$OverlayConfig.ManualToggleHotkey + " TO TOGGLE"
    $label.Foreground = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#D7FF3F")
    $label.FontFamily = New-Object System.Windows.Media.FontFamily("Segoe UI")
    $label.FontSize = 10.0
    $label.FontWeight = [System.Windows.FontWeights]::SemiBold
    $label.Opacity = 0.75
    $label.IsHitTestVisible = $false
    [System.Windows.Controls.Canvas]::SetLeft($label, 12.0)
    [System.Windows.Controls.Canvas]::SetTop($label, 10.0)
    $canvas.Children.Add($label) | Out-Null
}

$timer = New-Object System.Windows.Threading.DispatcherTimer
$timer.Interval = [TimeSpan]::FromMilliseconds([Math]::Max(12, [int]$OverlayConfig.FrameMilliseconds))

$timer.Add_Tick({
    $now = Get-Date

    if (($now - $lastWindowPoll).TotalMilliseconds -ge [int]$OverlayConfig.WindowPollMilliseconds) {
        $lastWindowPoll = $now

        if ($targetHwnd -eq [IntPtr]::Zero -or -not [AHMOverlayNative]::IsWindow($targetHwnd)) {
            $targetHwnd = [AHMOverlayNative]::FindWindowByExactTitle($WindowTitle)
        }

        if ($targetHwnd -ne [IntPtr]::Zero -and [AHMOverlayNative]::IsWindow($targetHwnd)) {
            $targetSeen = $true
            $missingSince = $null

            $rect = New-Object AHMOverlayNative+RECT
            if ([AHMOverlayNative]::TryGetClientScreenRect($targetHwnd, [ref]$rect)) {
                $width = [Math]::Max(1, $rect.Right - $rect.Left)
                $height = [Math]::Max(1, $rect.Bottom - $rect.Top)

                [AHMOverlayNative]::SetWindowPos(
                    $overlayHwnd,
                    [AHMOverlayNative]::HWND_TOPMOST,
                    $rect.Left,
                    $rect.Top,
                    $width,
                    $height,
                    [AHMOverlayNative]::SWP_NOACTIVATE -bor [AHMOverlayNative]::SWP_NOSENDCHANGING
                ) | Out-Null

                $boundsKey = "$($rect.Left),$($rect.Top),$width,$height,$($canvas.ActualWidth),$($canvas.ActualHeight)"
                if ($boundsKey -ne $lastBoundsKey -and $canvas.ActualWidth -gt 0 -and $canvas.ActualHeight -gt 0) {
                    $lastBoundsKey = $boundsKey
                    Update-Grid
                }
            }
        }
        elseif ($targetSeen) {
            if ($null -eq $missingSince) {
                $missingSince = $now
            }
            elseif (($now - $missingSince).TotalMilliseconds -gt 3000) {
                $timer.Stop()
                $window.Close()
                return
            }
        }
    }

    if (($now - $lastKeyguardPoll).TotalMilliseconds -ge [int]$OverlayConfig.KeyguardPollMilliseconds) {
        $lastKeyguardPoll = $now
        if ($OverlayConfig.AutoShowOnKeyguard) {
            $keyguardState = Get-KeyguardState
            if ($keyguardState -eq "unlocked") {
                $manualOverride = $null
            }
        }
    }

    $foreground = (
        $targetHwnd -ne [IntPtr]::Zero -and
        [AHMOverlayNative]::GetForegroundWindow() -eq $targetHwnd
    )

    $hotkeyDown = $foreground -and (Test-HotkeyDown $hotkey)
    if ($hotkeyDown -and -not $hotkeyWasDown) {
        $currentlyVisible = $window.IsVisible
        $manualOverride = -not $currentlyVisible
        $manualOverrideUntil = $now.AddSeconds([Math]::Max(5, [int]$OverlayConfig.ManualShowSeconds))
    }
    $hotkeyWasDown = $hotkeyDown

    if ($null -ne $manualOverride -and $now -gt $manualOverrideUntil) {
        $manualOverride = $null
    }

    $autoVisible = ($OverlayConfig.AutoShowOnKeyguard -and $keyguardState -eq "locked")
    if ($null -ne $manualOverride) {
        $shouldShow = [bool]$manualOverride
    }
    else {
        $shouldShow = [bool]$autoVisible
    }

    $shouldShow = $shouldShow -and $foreground -and $targetHwnd -ne [IntPtr]::Zero

    if ($shouldShow) {
        if (-not $window.IsVisible) {
            $window.Show()
        }

        [AHMOverlayNative]::SetWindowPos(
            $overlayHwnd,
            [AHMOverlayNative]::HWND_TOPMOST,
            0,
            0,
            0,
            0,
            0x0001 -bor 0x0002 -bor [AHMOverlayNative]::SWP_NOACTIVATE
        ) | Out-Null
    }
    elseif ($window.IsVisible) {
        $window.Hide()
        $trail.Points.Clear()
    }

    if ($window.IsVisible -and $OverlayConfig.ShowCursorTrail) {
        $leftDown = Test-KeyDown 0x01

        if ($leftDown) {
            $cursor = New-Object AHMOverlayNative+POINT
            if ([AHMOverlayNative]::GetCursorPos([ref]$cursor)) {
                $rect = New-Object AHMOverlayNative+RECT
                if ([AHMOverlayNative]::TryGetClientScreenRect($targetHwnd, [ref]$rect)) {
                    $physicalWidth = [Math]::Max(1.0, [double]($rect.Right - $rect.Left))
                    $physicalHeight = [Math]::Max(1.0, [double]($rect.Bottom - $rect.Top))
                    $scaleX = $canvas.ActualWidth / $physicalWidth
                    $scaleY = $canvas.ActualHeight / $physicalHeight

                    $x = ([double]$cursor.X - [double]$rect.Left) * $scaleX
                    $y = ([double]$cursor.Y - [double]$rect.Top) * $scaleY

                    if ($x -ge 0 -and $y -ge 0 -and $x -le $canvas.ActualWidth -and $y -le $canvas.ActualHeight) {
                        $trail.Points.Add((New-Object System.Windows.Point($x, $y)))
                    }
                }
            }
        }
        elseif ($leftWasDown) {
            $trailClearAt = $now.AddMilliseconds([Math]::Max(0, [int]$OverlayConfig.TrailHoldMilliseconds))
        }
        elseif ($trail.Points.Count -gt 0 -and $now -ge $trailClearAt) {
            $trail.Points.Clear()
        }

        $leftWasDown = $leftDown
    }
})

$window.Add_Closed({
    $timer.Stop()
})

$timer.Start()
[System.Windows.Threading.Dispatcher]::Run()
