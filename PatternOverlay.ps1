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
    $xs = @(($centerX - $half), $centerX, ($centerX + $half))
    $ys = @(($centerY - $half), $centerY, ($centerY + $half))

    $points = @()
    foreach ($y in $ys) {
        foreach ($x in $xs) {
            $points += [pscustomobject]@{ X = [double]$x; Y = [double]$y }
        }
    }

    return $points
}


function ConvertFrom-AndroidBounds([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }

    if ($Text -notmatch '^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$') {
        return $null
    }

    $left = [double]$Matches[1]
    $top = [double]$Matches[2]
    $right = [double]$Matches[3]
    $bottom = [double]$Matches[4]

    if ($right -le $left -or $bottom -le $top) { return $null }

    return [pscustomobject]@{
        Left = $left
        Top = $top
        Right = $right
        Bottom = $bottom
        Width = $right - $left
        Height = $bottom - $top
        CenterX = ($left + $right) / 2.0
        CenterY = ($top + $bottom) / 2.0
    }
}

function New-PatternGeometry(
    [string]$Source,
    [double]$ScreenWidth,
    [double]$ScreenHeight,
    [double]$Left,
    [double]$Top,
    [double]$Right,
    [double]$Bottom,
    [bool]$ExactDots
) {
    if ($ScreenWidth -le 0 -or $ScreenHeight -le 0) { return $null }
    if ($Right -le $Left -or $Bottom -le $Top) { return $null }

    return [pscustomobject]@{
        Source = $Source
        ScreenWidth = $ScreenWidth
        ScreenHeight = $ScreenHeight
        ExactDots = $ExactDots
        GridBoundsNormalized = [pscustomobject]@{
            Left = $Left / $ScreenWidth
            Top = $Top / $ScreenHeight
            Right = $Right / $ScreenWidth
            Bottom = $Bottom / $ScreenHeight
        }
    }
}

function Get-PatternGeometryFromUiXml([string]$XmlText) {
    if ([string]::IsNullOrWhiteSpace($XmlText)) { return $null }

    try {
        [xml]$doc = $XmlText
    }
    catch {
        return $null
    }

    $nodes = @($doc.SelectNodes("//node"))
    if ($nodes.Count -eq 0) { return $null }

    $nodeInfo = @()
    $screenRight = 0.0
    $screenBottom = 0.0

    foreach ($node in $nodes) {
        $bounds = ConvertFrom-AndroidBounds ([string]$node.bounds)
        if ($null -eq $bounds) { continue }

        $screenRight = [Math]::Max($screenRight, $bounds.Right)
        $screenBottom = [Math]::Max($screenBottom, $bounds.Bottom)

        $className = [string]$node.class
        $resourceId = [string]$node.'resource-id'
        $contentDescription = [string]$node.'content-desc'
        $text = [string]$node.text
        $searchText = "$className $resourceId $contentDescription $text"

        $score = 0
        if ($className -match '(?i)(^|\.)LockPatternView$') { $score += 120 }
        elseif ($className -match '(?i)Pattern') { $score += 35 }

        if ($resourceId -match '(?i)(lock.?pattern|pattern.?lock|lockPatternView)') { $score += 100 }
        elseif ($resourceId -match '(?i)pattern') { $score += 45 }

        if ($contentDescription -match '(?i)pattern\s*(area|lock|grid)?') { $score += 55 }
        if ($text -match '(?i)pattern\s*(area|lock|grid)?') { $score += 20 }

        $ratio = $bounds.Width / $bounds.Height
        if ($ratio -ge 0.72 -and $ratio -le 1.38) { $score += 20 }
        if ($bounds.Width -ge 180 -and $bounds.Height -ge 180) { $score += 10 }

        $nodeInfo += [pscustomobject]@{
            Node = $node
            Bounds = $bounds
            ClassName = $className
            ResourceId = $resourceId
            ContentDescription = $contentDescription
            Text = $text
            SearchText = $searchText
            Score = $score
        }
    }

    if ($screenRight -le 0 -or $screenBottom -le 0) { return $null }

    $patternView = @(
        $nodeInfo |
            Where-Object { $_.Score -ge 50 } |
            Sort-Object Score -Descending, @{ Expression = { $_.Bounds.Width * $_.Bounds.Height }; Descending = $true }
    ) | Select-Object -First 1

    if ($null -eq $patternView) { return $null }

    # AOSP exposes nine virtual accessibility nodes while a pattern is in progress.
    # OEMs may expose them differently, so only trust child nodes with explicit
    # pattern/cell semantics. If nine are present, their centers are more precise
    # than deriving centers from the parent view bounds.
    $dotCandidates = @()
    foreach ($child in @($patternView.Node.SelectNodes(".//node"))) {
        $bounds = ConvertFrom-AndroidBounds ([string]$child.bounds)
        if ($null -eq $bounds) { continue }

        if (
            $bounds.Left -lt $patternView.Bounds.Left -or
            $bounds.Top -lt $patternView.Bounds.Top -or
            $bounds.Right -gt $patternView.Bounds.Right -or
            $bounds.Bottom -gt $patternView.Bounds.Bottom
        ) {
            continue
        }

        $childText = (
            ([string]$child.class) + " " +
            ([string]$child.'resource-id') + " " +
            ([string]$child.'content-desc') + " " +
            ([string]$child.text)
        )

        if ($childText -notmatch '(?i)(pattern.*cell|cell.*pattern|pattern\s*cell)') {
            continue
        }

        if (
            $bounds.Width -gt ($patternView.Bounds.Width * 0.45) -or
            $bounds.Height -gt ($patternView.Bounds.Height * 0.45)
        ) {
            continue
        }

        $dotCandidates += $bounds
    }

    if ($dotCandidates.Count -ge 9) {
        $ordered = @(
            $dotCandidates |
                Sort-Object CenterY, CenterX |
                Select-Object -First 9
        )

        if ($ordered.Count -eq 9) {
            $left = ($ordered | Measure-Object CenterX -Minimum).Minimum
            $right = ($ordered | Measure-Object CenterX -Maximum).Maximum
            $top = ($ordered | Measure-Object CenterY -Minimum).Minimum
            $bottom = ($ordered | Measure-Object CenterY -Maximum).Maximum

            $geometry = New-PatternGeometry "ui-dots" $screenRight $screenBottom $left $top $right $bottom $true
            if ($null -ne $geometry) { return $geometry }
        }
    }

    # For AOSP LockPatternView, the three cell centers are each centered in one
    # third of the usable pattern view. UIAutomator gives us the runtime view
    # bounds, so derive the outer dot-center rectangle from 1/6 and 5/6.
    $view = $patternView.Bounds
    $leftCenter = $view.Left + ($view.Width / 6.0)
    $rightCenter = $view.Left + ($view.Width * 5.0 / 6.0)
    $topCenter = $view.Top + ($view.Height / 6.0)
    $bottomCenter = $view.Top + ($view.Height * 5.0 / 6.0)

    return New-PatternGeometry "ui-view" $screenRight $screenBottom $leftCenter $topCenter $rightCenter $bottomCenter $false
}

function Get-PatternPointsFromGeometry(
    $Geometry,
    [double]$ClientWidth,
    [double]$ClientHeight,
    [double]$FallbackDeviceWidth,
    [double]$FallbackDeviceHeight,
    $OverlayConfig
) {
    if ($null -eq $Geometry -or $null -eq $Geometry.GridBoundsNormalized) {
        $contentRect = Get-FittedContentRect $ClientWidth $ClientHeight $FallbackDeviceWidth $FallbackDeviceHeight
        $points = @(Get-PatternGridPoints $contentRect $OverlayConfig)
        return [pscustomobject]@{
            Source = "estimated"
            ContentRect = $contentRect
            Points = $points
        }
    }

    $screenWidth = [double]$Geometry.ScreenWidth
    $screenHeight = [double]$Geometry.ScreenHeight
    if ($screenWidth -le 0) { $screenWidth = $FallbackDeviceWidth }
    if ($screenHeight -le 0) { $screenHeight = $FallbackDeviceHeight }

    $contentRect = Get-FittedContentRect $ClientWidth $ClientHeight $screenWidth $screenHeight
    $bounds = $Geometry.GridBoundsNormalized

    $left = $contentRect.X + ([double]$bounds.Left * $contentRect.Width)
    $right = $contentRect.X + ([double]$bounds.Right * $contentRect.Width)
    $top = $contentRect.Y + ([double]$bounds.Top * $contentRect.Height)
    $bottom = $contentRect.Y + ([double]$bounds.Bottom * $contentRect.Height)

    $xs = @($left, (($left + $right) / 2.0), $right)
    $ys = @($top, (($top + $bottom) / 2.0), $bottom)

    $points = @()
    foreach ($y in $ys) {
        foreach ($x in $xs) {
            $points += [pscustomobject]@{ X = [double]$x; Y = [double]$y }
        }
    }

    return [pscustomobject]@{
        Source = [string]$Geometry.Source
        ContentRect = $contentRect
        Points = $points
    }
}

function Get-CalibrationPath([string]$SerialValue) {
    $directoryName = [string]$OverlayConfig.CalibrationDirectory
    if ([string]::IsNullOrWhiteSpace($directoryName)) {
        $directoryName = "pattern-calibration"
    }

    $directory = Join-Path $Root $directoryName
    $safeSerial = ($SerialValue -replace '[^A-Za-z0-9._-]', '_')
    if ([string]::IsNullOrWhiteSpace($safeSerial)) { $safeSerial = "device" }

    return Join-Path $directory ($safeSerial + ".json")
}

function Convert-CalibrationRecordToGeometry($Record) {
    if ($null -eq $Record) { return $null }

    foreach ($name in @("Left", "Top", "Right", "Bottom")) {
        if ($null -eq $Record.PSObject.Properties[$name]) { return $null }
    }

    $left = [double]$Record.Left
    $top = [double]$Record.Top
    $right = [double]$Record.Right
    $bottom = [double]$Record.Bottom

    if (
        $left -lt 0 -or $top -lt 0 -or
        $right -gt 1 -or $bottom -gt 1 -or
        $right -le $left -or $bottom -le $top
    ) {
        return $null
    }

    return [pscustomobject]@{
        Source = "calibration"
        ScreenWidth = 1.0
        ScreenHeight = 1.0
        ExactDots = $false
        GridBoundsNormalized = [pscustomobject]@{
            Left = $left
            Top = $top
            Right = $right
            Bottom = $bottom
        }
    }
}

function Load-PatternCalibration([string]$SerialValue) {
    if (-not $OverlayConfig.CalibrationEnabled) { return $null }

    $path = Get-CalibrationPath $SerialValue
    if (-not (Test-Path $path)) { return $null }

    try {
        $record = Get-Content $path -Raw | ConvertFrom-Json
        return Convert-CalibrationRecordToGeometry $record
    }
    catch {
        return $null
    }
}

function Save-PatternCalibration([string]$SerialValue, $BoundsNormalized) {
    if (-not $OverlayConfig.CalibrationEnabled) { return $false }

    $path = Get-CalibrationPath $SerialValue
    $directory = Split-Path -Parent $path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null

    $record = [pscustomobject]@{
        Version = 1
        Serial = $SerialValue
        Left = [Math]::Round([double]$BoundsNormalized.Left, 8)
        Top = [Math]::Round([double]$BoundsNormalized.Top, 8)
        Right = [Math]::Round([double]$BoundsNormalized.Right, 8)
        Bottom = [Math]::Round([double]$BoundsNormalized.Bottom, 8)
        UpdatedUtc = [DateTime]::UtcNow.ToString("o")
    }

    $record | ConvertTo-Json -Depth 4 | Set-Content -Path $path -Encoding UTF8
    return $true
}

function Remove-PatternCalibration([string]$SerialValue) {
    $path = Get-CalibrationPath $SerialValue
    if (Test-Path $path) {
        Remove-Item -Force $path -ErrorAction SilentlyContinue
    }
}

function Get-EffectivePatternGeometry {
    # Exact virtual-dot bounds are the highest-confidence source.
    if ($script:discoveredGeometry -and [string]$script:discoveredGeometry.Source -eq "ui-dots") {
        return $script:discoveredGeometry
    }

    # A user calibration is an explicit correction, so it can override parent-view
    # geometry when an OEM exposes a padded/non-standard pattern container.
    if ($script:calibrationGeometry) {
        return $script:calibrationGeometry
    }

    if ($script:discoveredGeometry) {
        return $script:discoveredGeometry
    }

    return $null
}

function Get-GridBoundsNormalizedFromPoints($Points, $ContentRect) {
    if ($null -eq $Points -or @($Points).Count -ne 9) { return $null }
    if ($ContentRect.Width -le 0 -or $ContentRect.Height -le 0) { return $null }

    $left = (@($Points) | Measure-Object X -Minimum).Minimum
    $right = (@($Points) | Measure-Object X -Maximum).Maximum
    $top = (@($Points) | Measure-Object Y -Minimum).Minimum
    $bottom = (@($Points) | Measure-Object Y -Maximum).Maximum

    return [pscustomobject]@{
        Left = [Math]::Max(0.0, [Math]::Min(1.0, (($left - $ContentRect.X) / $ContentRect.Width)))
        Top = [Math]::Max(0.0, [Math]::Min(1.0, (($top - $ContentRect.Y) / $ContentRect.Height)))
        Right = [Math]::Max(0.0, [Math]::Min(1.0, (($right - $ContentRect.X) / $ContentRect.Width)))
        Bottom = [Math]::Max(0.0, [Math]::Min(1.0, (($bottom - $ContentRect.Y) / $ContentRect.Height)))
    }
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

Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

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


function Get-UiHierarchyXml {
    if (-not $OverlayConfig.AutoDiscoverGeometry) { return $null }

    $remote = "/data/local/tmp/ahm-pattern-" + $PID + ".xml"

    try {
        $previousErrorActionPreference = $ErrorActionPreference
        $ErrorActionPreference = "Continue"

        & $adb -s $Serial shell uiautomator dump --compressed $remote 2>$null | Out-Null
        $xmlText = (& $adb -s $Serial exec-out cat $remote 2>$null | Out-String).Trim()

        if ($xmlText -match '<hierarchy[\s>]' -and $xmlText -match '</hierarchy>') {
            return $xmlText
        }
    }
    catch {}
    finally {
        try {
            & $adb -s $Serial shell rm -f $remote 2>$null | Out-Null
        }
        catch {}

        if ($null -ne $previousErrorActionPreference) {
            $ErrorActionPreference = $previousErrorActionPreference
        }
    }

    return $null
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
$script:targetHwnd = [IntPtr]::Zero
$script:targetSeen = $false
$script:missingSince = $null
$script:lastWindowPoll = [DateTime]::MinValue
$script:lastKeyguardPoll = [DateTime]::MinValue
$script:keyguardState = "unknown"
$script:manualOverride = $null
$script:manualOverrideUntil = [DateTime]::MinValue
$script:hotkeyWasDown = $false
$script:leftWasDown = $false
$script:trailClearAt = [DateTime]::MinValue
$script:lastBoundsKey = ""

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

    if (($now - $script:lastWindowPoll).TotalMilliseconds -ge [int]$OverlayConfig.WindowPollMilliseconds) {
        $script:lastWindowPoll = $now

        if ($script:targetHwnd -eq [IntPtr]::Zero -or -not [AHMOverlayNative]::IsWindow($script:targetHwnd)) {
            $script:targetHwnd = [AHMOverlayNative]::FindWindowByExactTitle($WindowTitle)
        }

        if ($script:targetHwnd -ne [IntPtr]::Zero -and [AHMOverlayNative]::IsWindow($script:targetHwnd)) {
            $script:targetSeen = $true
            $script:missingSince = $null

            $rect = New-Object AHMOverlayNative+RECT
            if ([AHMOverlayNative]::TryGetClientScreenRect($script:targetHwnd, [ref]$rect)) {
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
                if ($boundsKey -ne $script:lastBoundsKey -and $canvas.ActualWidth -gt 0 -and $canvas.ActualHeight -gt 0) {
                    $script:lastBoundsKey = $boundsKey
                    Update-Grid
                }
            }
        }
        elseif ($script:targetSeen) {
            if ($null -eq $script:missingSince) {
                $script:missingSince = $now
            }
            elseif (($now - $script:missingSince).TotalMilliseconds -gt 3000) {
                $timer.Stop()
                $window.Close()
                return
            }
        }
    }

    if (($now - $script:lastKeyguardPoll).TotalMilliseconds -ge [int]$OverlayConfig.KeyguardPollMilliseconds) {
        $script:lastKeyguardPoll = $now
        if ($OverlayConfig.AutoShowOnKeyguard) {
            $script:keyguardState = Get-KeyguardState
            if ($script:keyguardState -eq "unlocked") {
                $script:manualOverride = $null
            }
        }
    }

    $foreground = (
        $script:targetHwnd -ne [IntPtr]::Zero -and
        [AHMOverlayNative]::GetForegroundWindow() -eq $script:targetHwnd
    )

    $hotkeyDown = $foreground -and (Test-HotkeyDown $hotkey)
    if ($hotkeyDown -and -not $script:hotkeyWasDown) {
        $currentlyVisible = $window.IsVisible
        $script:manualOverride = -not $currentlyVisible
        $script:manualOverrideUntil = $now.AddSeconds([Math]::Max(5, [int]$OverlayConfig.ManualShowSeconds))
    }
    $script:hotkeyWasDown = $hotkeyDown

    if ($null -ne $script:manualOverride -and $now -gt $script:manualOverrideUntil) {
        $script:manualOverride = $null
    }

    $autoVisible = ($OverlayConfig.AutoShowOnKeyguard -and $script:keyguardState -eq "locked")
    if ($null -ne $script:manualOverride) {
        $shouldShow = [bool]$script:manualOverride
    }
    else {
        $shouldShow = [bool]$autoVisible
    }

    $shouldShow = $shouldShow -and $foreground -and $script:targetHwnd -ne [IntPtr]::Zero

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
                if ([AHMOverlayNative]::TryGetClientScreenRect($script:targetHwnd, [ref]$rect)) {
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
        elseif ($script:leftWasDown) {
            $script:trailClearAt = $now.AddMilliseconds([Math]::Max(0, [int]$OverlayConfig.TrailHoldMilliseconds))
        }
        elseif ($trail.Points.Count -gt 0 -and $now -ge $script:trailClearAt) {
            $trail.Points.Clear()
        }

        $script:leftWasDown = $leftDown
    }
})

$window.Add_Closed({
    $timer.Stop()
})

$timer.Start()
[System.Windows.Threading.Dispatcher]::Run()
