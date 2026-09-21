[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$SupervisorPath = Join-Path $Root "Start-PhoneMirror.ps1"
$StopPath = Join-Path $Root "Stop-PhoneMirror.ps1"
$OverlayPath = Join-Path $Root "PatternOverlay.ps1"
$MirrorChromePath = Join-Path $Root "MirrorChrome.ps1"
$ResetLockScreenPath = Join-Path $Root "Reset-LockScreenChoices.ps1"

$script:Assertions = 0

function Assert-True {
    param(
        [Parameter(Mandatory)][bool]$Condition,
        [Parameter(Mandatory)][string]$Message
    )

    $script:Assertions++
    if (-not $Condition) {
        throw "Assertion failed: $Message"
    }
}

function Assert-False {
    param(
        [Parameter(Mandatory)][bool]$Condition,
        [Parameter(Mandatory)][string]$Message
    )

    Assert-True -Condition (-not $Condition) -Message $Message
}

function Assert-Equal {
    param(
        $Expected,
        $Actual,
        [Parameter(Mandatory)][string]$Message
    )

    $script:Assertions++
    if ($Expected -is [System.Array] -or $Actual -is [System.Array]) {
        $expectedJson = @($Expected) | ConvertTo-Json -Compress
        $actualJson = @($Actual) | ConvertTo-Json -Compress
        if ($expectedJson -ne $actualJson) {
            throw ("Assertion failed: " + $Message + [Environment]::NewLine + "Expected: " + $expectedJson + [Environment]::NewLine + "Actual:   " + $actualJson)
        }
        return
    }

    if ($Expected -ne $Actual) {
        throw ("Assertion failed: " + $Message + [Environment]::NewLine + "Expected: " + $Expected + [Environment]::NewLine + "Actual:   " + $Actual)
    }
}

function Assert-Contains {
    param(
        [Parameter(Mandatory)][object[]]$Collection,
        [Parameter(Mandatory)]$Value,
        [Parameter(Mandatory)][string]$Message
    )

    Assert-True -Condition ($Collection -contains $Value) -Message $Message
}

function Get-SupervisorFunctionDefinitions {
    param([string[]]$Names)

    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile(
        $SupervisorPath,
        [ref]$tokens,
        [ref]$errors
    )

    Assert-Equal -Expected 0 -Actual $errors.Count -Message "Supervisor must parse before behavior tests run."

    $definitions = @{}
    $ast.FindAll(
        {
            param($Node)
            $Node -is [System.Management.Automation.Language.FunctionDefinitionAst]
        },
        $true
    ) | ForEach-Object {
        $definitions[$_.Name] = $_.Extent.Text
    }

    $selectedDefinitions = @()
    foreach ($name in $Names) {
        Assert-True -Condition $definitions.ContainsKey($name) -Message "Supervisor function '$name' must exist."
        $selectedDefinitions += $definitions[$name]
    }

    return $selectedDefinitions
}

function Get-OverlayFunctionDefinitions {
    param([string[]]$Names)

    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile(
        $OverlayPath,
        [ref]$tokens,
        [ref]$errors
    )

    Assert-Equal -Expected 0 -Actual $errors.Count -Message "Pattern overlay must parse before behavior tests run."

    $definitions = @{}
    $ast.FindAll(
        {
            param($Node)
            $Node -is [System.Management.Automation.Language.FunctionDefinitionAst]
        },
        $true
    ) | ForEach-Object {
        $definitions[$_.Name] = $_.Extent.Text
    }

    $selectedDefinitions = @()
    foreach ($name in $Names) {
        Assert-True -Condition $definitions.ContainsKey($name) -Message "Overlay function '$name' must exist."
        $selectedDefinitions += $definitions[$name]
    }

    return $selectedDefinitions
}

$functionDefinitions = @(Get-SupervisorFunctionDefinitions -Names @(
    "Ensure-StateShape",
    "Get-DeviceProfile",
    "Get-AdbDevices",
    "Is-PrivateIPv4",
    "Get-PhoneIpCandidates",
    "Unique-Strings",
    "Select-Device",
    "Build-ScrcpyArguments",
    "Invoke-Scrcpy",
    "Start-PatternOverlay",
    "Prepare-DeviceForMirror"
))

foreach ($definition in $functionDefinitions) {
    Invoke-Expression $definition
}

$overlayFunctionDefinitions = @(Get-OverlayFunctionDefinitions -Names @(
    "Get-KeyguardStateFromText",
    "Get-FittedContentRect",
    "Get-PatternGridPoints",
    "ConvertFrom-AndroidBounds",
    "New-PatternGeometry",
    "Get-PatternGeometryFromUiXml",
    "Get-PatternPointsFromGeometry",
    "Get-CalibrationPath",
    "Convert-CalibrationRecordToGeometry",
    "Load-PatternCalibration",
    "Save-PatternCalibration",
    "Remove-PatternCalibration",
    "Get-GridBoundsNormalizedFromPoints",
    "Clamp-CalibrationBounds",
    "Get-HotkeySpec"
))

foreach ($definition in $overlayFunctionDefinitions) {
    Invoke-Expression $definition
}


Write-Host "[powershell] Testing overlay script safe-load mode..."
& $OverlayPath -Serial "TEST_SERIAL" -TestOnly
Assert-True -Condition $true -Message "PatternOverlay.ps1 -TestOnly should execute without creating WPF state."

Write-Host "[powershell] Testing mirror-toolbar script safe-load mode..."
& $MirrorChromePath -Serial "TEST_SERIAL" -TestOnly
Assert-True -Condition $true -Message "MirrorChrome.ps1 -TestOnly should compile native helpers without creating a toolbar or magnifier."


Write-Host "[powershell] Testing non-pattern modes never launch an overlay..."
Assert-True -Condition ($null -eq (Start-PatternOverlay "USB123" "none")) -Message "No-lock mode must not start an overlay."
Assert-True -Condition ($null -eq (Start-PatternOverlay "USB123" "other")) -Message "PIN/password/other mode must not start an overlay."
Assert-True -Condition ($null -eq (Start-PatternOverlay "USB123" "session-off")) -Message "Ask-later/session-off mode must not start an overlay."

Write-Host "[powershell] Testing private IPv4 classification..."
foreach ($ip in @(
    "10.0.0.1",
    "10.255.255.255",
    "172.16.0.1",
    "172.31.255.254",
    "192.168.0.1",
    "192.168.255.254"
)) {
    Assert-True -Condition (Is-PrivateIPv4 $ip) -Message "$ip should be private."
}

foreach ($ip in @(
    "8.8.8.8",
    "127.0.0.1",
    "172.15.255.255",
    "172.32.0.1",
    "192.167.255.255",
    "192.169.0.1",
    "256.1.2.3",
    "foo"
)) {
    Assert-False -Condition (Is-PrivateIPv4 $ip) -Message "$ip should not be private."
}

Write-Host "[powershell] Testing uniqueness helper..."
$unique = @(Unique-Strings @(
    " 192.168.1.1 ",
    "192.168.1.1",
    "",
    $null,
    "10.0.0.2",
    "10.0.0.2"
))
Assert-Equal -Expected @("192.168.1.1", "10.0.0.2") -Actual $unique -Message "Unique-Strings should trim, drop empties, and preserve first occurrence."

Write-Host "[powershell] Testing state migration and per-device profiles..."
$legacyState = [pscustomobject]@{
    PreferredSerial = "USB123"
    WirelessHosts = @("192.168.1.20")
}
$legacyState = Ensure-StateShape $legacyState
Assert-True -Condition ($null -ne $legacyState.PSObject.Properties["DeviceProfiles"]) -Message "Old state should gain DeviceProfiles."
Assert-Equal -Expected 0 -Actual @($legacyState.DeviceProfiles).Count -Message "Migrated DeviceProfiles should start empty."

$profileState = [pscustomobject]@{
    PreferredSerial = ""
    WirelessHosts = @()
    DeviceProfiles = @(
        [pscustomobject]@{ Serial = "A"; LockScreenMode = "none" },
        [pscustomobject]@{ Serial = "B"; LockScreenMode = "pattern" }
    )
}
$profile = Get-DeviceProfile $profileState "B"
Assert-Equal -Expected "pattern" -Actual $profile.LockScreenMode -Message "Per-device lock mode should be retrieved by serial."
Assert-True -Condition ($null -eq (Get-DeviceProfile $profileState "MISSING")) -Message "Unknown devices should not inherit another phone's lock mode."

Write-Host "[powershell] Testing pattern overlay keyguard parsing..."
Assert-Equal -Expected "locked" -Actual (Get-KeyguardStateFromText "mKeyguardShowing=true" "") -Message "Generic keyguard=true should be locked."
Assert-Equal -Expected "locked" -Actual (Get-KeyguardStateFromText "" "deviceLocked: 1") -Message "Trust deviceLocked=1 should be locked."
Assert-Equal -Expected "unlocked" -Actual (Get-KeyguardStateFromText "mShowingLockscreen=false" "") -Message "Generic lockscreen=false should be unlocked."
Assert-Equal -Expected "unlocked" -Actual (Get-KeyguardStateFromText "" "deviceLocked=false") -Message "Trust deviceLocked=false should be unlocked."
Assert-Equal -Expected "unknown" -Actual (Get-KeyguardStateFromText "unrelated output" "nothing useful") -Message "Missing OEM signals should remain unknown."

Write-Host "[powershell] Testing pattern overlay geometry..."
$portraitRect = Get-FittedContentRect 1000 1000 1080 2400
Assert-Equal -Expected 275.0 -Actual ([Math]::Round($portraitRect.X, 3)) -Message "Portrait video should be horizontally letterboxed in a square client."
Assert-Equal -Expected 0.0 -Actual ([Math]::Round($portraitRect.Y, 3)) -Message "Portrait video should fill square-client height."
Assert-Equal -Expected 450.0 -Actual ([Math]::Round($portraitRect.Width, 3)) -Message "Portrait fitted width should preserve aspect ratio."
Assert-Equal -Expected 1000.0 -Actual ([Math]::Round($portraitRect.Height, 3)) -Message "Portrait fitted height should fill client height."

$landscapeRect = Get-FittedContentRect 1600 900 1080 2400
Assert-Equal -Expected 0.0 -Actual ([Math]::Round($landscapeRect.X, 3)) -Message "Landscape video should fill client width."
Assert-Equal -Expected 90.0 -Actual ([Math]::Round($landscapeRect.Y, 3)) -Message "Landscape video should be vertically letterboxed."
Assert-Equal -Expected 1600.0 -Actual ([Math]::Round($landscapeRect.Width, 3)) -Message "Landscape fitted width should fill client width."
Assert-Equal -Expected 720.0 -Actual ([Math]::Round($landscapeRect.Height, 3)) -Message "Landscape fitted height should preserve aspect ratio."

$overlayConfig = [pscustomobject]@{
    GridSizeRelativeToWidth = 0.6
    GridCenterX = 0.5
    GridCenterY = 0.6
}
$points = @(Get-PatternGridPoints $portraitRect $overlayConfig)
Assert-Equal -Expected 9 -Actual $points.Count -Message "Pattern guide must always contain nine points."
Assert-Equal -Expected ([Math]::Round($portraitRect.X + ($portraitRect.Width * 0.5), 3)) -Actual ([Math]::Round($points[4].X, 3)) -Message "Middle pattern dot should use configured horizontal center."
Assert-Equal -Expected ([Math]::Round($portraitRect.Y + ($portraitRect.Height * 0.6), 3)) -Actual ([Math]::Round($points[4].Y, 3)) -Message "Middle pattern dot should use configured vertical center."

Write-Host "[powershell] Testing Android UI hierarchy pattern discovery..."
$viewXml = @'
<hierarchy rotation="0">
  <node class="android.widget.FrameLayout" bounds="[0,0][1080,2400]">
    <node class="com.android.internal.widget.LockPatternView" resource-id="com.android.systemui:id/lockPatternView" content-desc="Pattern area" bounds="[140,820][940,1620]" />
  </node>
</hierarchy>
'@
$viewGeometry = Get-PatternGeometryFromUiXml $viewXml
Assert-True -Condition ($null -ne $viewGeometry) -Message "LockPatternView should be discovered from UI hierarchy."
Assert-Equal -Expected "ui-view" -Actual $viewGeometry.Source -Message "Parent LockPatternView should produce ui-view geometry."
Assert-Equal -Expected 1080.0 -Actual $viewGeometry.ScreenWidth -Message "Hierarchy should infer Android screen width."
Assert-Equal -Expected 2400.0 -Actual $viewGeometry.ScreenHeight -Message "Hierarchy should infer Android screen height."
Assert-Equal -Expected ([Math]::Round((140.0 + (800.0 / 6.0)) / 1080.0, 6)) -Actual ([Math]::Round($viewGeometry.GridBoundsNormalized.Left, 6)) -Message "Parent view should derive first-column center at one sixth."
Assert-Equal -Expected ([Math]::Round((820.0 + (800.0 / 6.0)) / 2400.0, 6)) -Actual ([Math]::Round($viewGeometry.GridBoundsNormalized.Top, 6)) -Message "Parent view should derive first-row center at one sixth."

$dotsXml = @'
<hierarchy rotation="0">
  <node class="android.widget.FrameLayout" bounds="[0,0][1080,2400]">
    <node class="com.android.internal.widget.LockPatternView" resource-id="com.android.systemui:id/lockPatternView" content-desc="Pattern area" bounds="[140,820][940,1620]">
      <node class="android.view.View" content-desc="Pattern cell 1" bounds="[270,970][330,1030]" />
      <node class="android.view.View" content-desc="Pattern cell 2" bounds="[510,970][570,1030]" />
      <node class="android.view.View" content-desc="Pattern cell 3" bounds="[750,970][810,1030]" />
      <node class="android.view.View" content-desc="Pattern cell 4" bounds="[270,1190][330,1250]" />
      <node class="android.view.View" content-desc="Pattern cell 5" bounds="[510,1190][570,1250]" />
      <node class="android.view.View" content-desc="Pattern cell 6" bounds="[750,1190][810,1250]" />
      <node class="android.view.View" content-desc="Pattern cell 7" bounds="[270,1410][330,1470]" />
      <node class="android.view.View" content-desc="Pattern cell 8" bounds="[510,1410][570,1470]" />
      <node class="android.view.View" content-desc="Pattern cell 9" bounds="[750,1410][810,1470]" />
    </node>
  </node>
</hierarchy>
'@
$dotsGeometry = Get-PatternGeometryFromUiXml $dotsXml
Assert-Equal -Expected "ui-dots" -Actual $dotsGeometry.Source -Message "Nine explicit virtual cells should outrank parent-view geometry."
Assert-True -Condition $dotsGeometry.ExactDots -Message "Virtual-cell geometry should be marked exact."
Assert-Equal -Expected ([Math]::Round(300.0 / 1080.0, 6)) -Actual ([Math]::Round($dotsGeometry.GridBoundsNormalized.Left, 6)) -Message "Exact dot geometry should use virtual-cell centers."
Assert-Equal -Expected ([Math]::Round(780.0 / 1080.0, 6)) -Actual ([Math]::Round($dotsGeometry.GridBoundsNormalized.Right, 6)) -Message "Exact dot geometry should use outer virtual-cell centers."
Assert-True -Condition ($null -eq (Get-PatternGeometryFromUiXml "<hierarchy><node class='android.widget.TextView' bounds='[0,0][1080,2400]' /></hierarchy>")) -Message "Unrelated UI hierarchy should not invent pattern geometry."

Write-Host "[powershell] Testing discovered geometry mapping into scrcpy client coordinates..."
$mapped = Get-PatternPointsFromGeometry $viewGeometry 540 1200 1080 2400 $overlayConfig
Assert-Equal -Expected "ui-view" -Actual $mapped.Source -Message "Mapped layout should preserve geometry source."
Assert-Equal -Expected 9 -Actual @($mapped.Points).Count -Message "Discovered geometry should map to nine client points."
Assert-Equal -Expected ([Math]::Round((140.0 + (800.0 / 6.0)) / 2.0, 3)) -Actual ([Math]::Round($mapped.Points[0].X, 3)) -Message "Android X coordinates should map proportionally into scrcpy content."
Assert-Equal -Expected ([Math]::Round((820.0 + (800.0 / 6.0)) / 2.0, 3)) -Actual ([Math]::Round($mapped.Points[0].Y, 3)) -Message "Android Y coordinates should map proportionally into scrcpy content."

Write-Host "[powershell] Testing calibration bounds validation..."
$clamped = Clamp-CalibrationBounds ([pscustomobject]@{ Left = -0.1; Top = 0.2; Right = 1.2; Bottom = 0.8 })
Assert-Equal -Expected 0.0 -Actual ([Math]::Round($clamped.Left, 4)) -Message "Calibration should clamp left edge."
Assert-Equal -Expected 1.0 -Actual ([Math]::Round($clamped.Right, 4)) -Message "Calibration should clamp right edge."
$invalidCalibration = Convert-CalibrationRecordToGeometry ([pscustomobject]@{ Left = 0.8; Top = 0.2; Right = 0.2; Bottom = 0.8 })
Assert-True -Condition ($null -eq $invalidCalibration) -Message "Inverted calibration bounds should be rejected."

Write-Host "[powershell] Testing overlay hotkey parsing..."
$hotkey = Get-HotkeySpec "Ctrl+Alt+P"
Assert-Equal -Expected @(0x11, 0x12) -Actual @($hotkey.Modifiers) -Message "Ctrl+Alt modifiers should parse."
Assert-Equal -Expected ([int][char]'P') -Actual $hotkey.Key -Message "P key should parse."
Assert-True -Condition ($null -eq (Get-HotkeySpec "Ctrl+Banana")) -Message "Invalid hotkeys should be rejected."

Write-Host "[powershell] Testing ADB device parsing with a fake executable..."
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("android-headless-mirror-tests-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $temp | Out-Null

try {
    Write-Host "[powershell] Testing per-device calibration persistence..."
    $originalRoot = $script:Root
    $hadOverlayConfig = Test-Path variable:script:OverlayConfig
    $originalOverlayConfig = if ($hadOverlayConfig) { $script:OverlayConfig } else { $null }

    try {
        $script:Root = $temp
        $script:OverlayConfig = [pscustomobject]@{
            CalibrationEnabled = $true
            CalibrationDirectory = "pattern-calibration"
        }

        $bounds = [pscustomobject]@{
            Left = 0.20
            Top = 0.35
            Right = 0.80
            Bottom = 0.72
        }

        Assert-True -Condition (Save-PatternCalibration "USB:123" $bounds) -Message "Calibration should save successfully."
        $calibrationPath = Get-CalibrationPath "USB:123"
        Assert-True -Condition (Test-Path $calibrationPath) -Message "Calibration should be stored in a per-device file."
        Assert-True -Condition ($calibrationPath -match 'USB_123\.json
    $fakeAdb = Join-Path $temp "fake-adb.cmd"
    @'
@echo off
echo List of devices attached
echo USB123 device product:oriole model:Pixel_6 transport_id:1
echo 192.168.1.40:5555 device product:oriole model:Pixel_6 transport_id:2
echo AUTH unauthorized transport_id:3
echo OFF offline transport_id:4
echo NOPERM no permissions transport_id:5
'@ | Set-Content -Path $fakeAdb -Encoding ASCII

    $devices = @(Get-AdbDevices $fakeAdb)
    Assert-Equal -Expected 5 -Actual $devices.Count -Message "Get-AdbDevices should parse all known ADB states."
    Assert-Equal -Expected "USB123" -Actual $devices[0].Serial -Message "First serial should parse."
    Assert-False -Condition $devices[0].IsTcp -Message "USB serial must not be classified as TCP."
    Assert-True -Condition $devices[1].IsTcp -Message "host:port serial must be classified as TCP."
    Assert-Equal -Expected "unauthorized" -Actual $devices[2].State -Message "Unauthorized state should parse."
    Assert-Equal -Expected "offline" -Actual $devices[3].State -Message "Offline state should parse."
    Assert-Equal -Expected "no permissions" -Actual $devices[4].State -Message "No-permissions state should parse."

    Write-Host "[powershell] Testing device selection..."
    $state = [pscustomobject]@{ PreferredSerial = "" }
    $script:Config = [pscustomobject]@{
        PreferredSerial = ""
        PreferUsb = $true
    }
    $selected = Select-Device $devices $state
    Assert-Equal -Expected "USB123" -Actual $selected.Serial -Message "USB should win over TCP by default."

    $state.PreferredSerial = "192.168.1.40:5555"
    $selected = Select-Device $devices $state
    Assert-Equal -Expected "192.168.1.40:5555" -Actual $selected.Serial -Message "Saved preferred serial should win."

    $script:Config.PreferredSerial = "USB123"
    $selected = Select-Device $devices $state
    Assert-Equal -Expected "USB123" -Actual $selected.Serial -Message "Configured preferred serial should override saved state."

    $script:Config.PreferredSerial = ""
    $state.PreferredSerial = "MISSING_DEVICE"
    $selected = Select-Device $devices $state
    Assert-Equal -Expected "USB123" -Actual $selected.Serial -Message "A missing learned/preferred phone must not block another authorised USB Android device."

    $nonReady = @(
        [pscustomobject]@{ Serial = "A"; State = "offline"; IsTcp = $false },
        [pscustomobject]@{ Serial = "B"; State = "unauthorized"; IsTcp = $false }
    )
    $selected = Select-Device $nonReady $state
    Assert-True -Condition ($null -eq $selected) -Message "Non-ready devices must never be selected."

    Write-Host "[powershell] Testing phone IP extraction..."
    $fakeIpAdb = Join-Path $temp "fake-ip-adb.cmd"
    @'
@echo off
echo 3: rmnet0    inet 10.123.45.67/32 scope global rmnet0
echo 12: swlan0    inet 192.168.43.1/24 brd 192.168.43.255 scope global swlan0
echo 13: wlan0     inet 192.168.43.1/24 brd 192.168.43.255 scope global wlan0
echo 14: rndis0    inet 192.168.42.129/24 brd 192.168.42.255 scope global rndis0
'@ | Set-Content -Path $fakeIpAdb -Encoding ASCII

    $ips = @(Get-PhoneIpCandidates $fakeIpAdb "USB123")
    Assert-Equal -Expected @("192.168.43.1") -Actual $ips -Message "Wi-Fi/hotspot interfaces should be preferred and deduplicated."

    Write-Host "[powershell] Testing scrcpy argument construction..."
    $script:Config = [pscustomobject]@{
        WindowTitle = "Android Device"
        TurnPhysicalScreenOff = $true
        StayAwakeWhenUsb = $true
        KeepActiveDuringMirror = $true
        DismissKeyguardWhenPossible = $true
        WakeBeforeMirror = $true
        PowerOffOnClose = $false
        MaxSize = 1920
        MaxFps = 60
        VideoBitRate = "12M"
        PreferredSerial = ""
        PreferUsb = $true
    }

    $usbArgs = @(Build-ScrcpyArguments "USB123" $false)
    foreach ($expected in @(
        "--serial=USB123",
        "--window-title=Android Device [USB123]",
        "--mouse=sdk",
        "--turn-screen-off"
        "--stay-awake",
        "--keep-active",
        "--max-size=1920",
        "--max-fps=60",
        "--video-bit-rate=12M"
    )) {
        Assert-Contains -Collection $usbArgs -Value $expected -Message "USB scrcpy args should include $expected."
    }
    Assert-False -Condition ($usbArgs -contains "--power-off-on-close") -Message "Disabled power-off-on-close should not be emitted."

    $tcpArgs = @(Build-ScrcpyArguments "192.168.1.40:5555" $true)
    Assert-False -Condition ($tcpArgs -contains "--stay-awake") -Message "TCP sessions should not receive the USB stay-awake flag."
    Assert-Contains -Collection $tcpArgs -Value "--keep-active" -Message "TCP sessions should still receive --keep-active."

    $script:Config.PowerOffOnClose = $true
    $script:Config.KeepActiveDuringMirror = $false
    $script:Config.MaxSize = 0
    $script:Config.MaxFps = 0
    $script:Config.VideoBitRate = ""
    $minimalArgs = @(Build-ScrcpyArguments "USB123" $false)
    Assert-Contains -Collection $minimalArgs -Value "--power-off-on-close" -Message "Enabled power-off-on-close should be emitted."
    Assert-False -Condition ($minimalArgs -contains "--keep-active") -Message "Disabled KeepActiveDuringMirror should suppress --keep-active."
    Assert-False -Condition (@($minimalArgs | Where-Object { $_ -like "--max-size=*" }).Count -gt 0) -Message "MaxSize=0 should suppress --max-size."
    Assert-False -Condition (@($minimalArgs | Where-Object { $_ -like "--max-fps=*" }).Count -gt 0) -Message "MaxFps=0 should suppress --max-fps."
    Assert-False -Condition (@($minimalArgs | Where-Object { $_ -like "--video-bit-rate=*" }).Count -gt 0) -Message "Blank bitrate should suppress --video-bit-rate."

    Write-Host "[powershell] Testing native scrcpy argument boundaries..."
    $scrcpyArgLog = Join-Path $temp "scrcpy-args.log"
    $env:AHM_SCRCPY_ARG_LOG = $scrcpyArgLog
    $fakeScrcpy = Join-Path $temp "fake-scrcpy.exe"
    $probeSource = @'
using System;
using System.IO;

public static class ArgProbe
{
    public static int Main(string[] args)
    {
        File.WriteAllLines(
            Environment.GetEnvironmentVariable("AHM_SCRCPY_ARG_LOG"),
            args
        );

        Console.Error.WriteLine("INFO: scrcpy-server: 1 file pushed, 0 skipped.");

        int exitCode;
        if (!int.TryParse(Environment.GetEnvironmentVariable("AHM_SCRCPY_EXIT_CODE"), out exitCode))
        {
            exitCode = 23;
        }

        return exitCode;
    }
}
'@
    $probeSourceFile = Join-Path $temp "ArgProbe.cs"
    Set-Content -Path $probeSourceFile -Value $probeSource -Encoding UTF8

    $cscCandidates = @(
        (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
        (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
    )
    $csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    Assert-True -Condition (-not [string]::IsNullOrWhiteSpace([string]$csc)) -Message "A .NET Framework C# compiler should be available on Windows."

    & $csc /nologo /target:exe "/out:$fakeScrcpy" $probeSourceFile
    Assert-Equal -Expected 0 -Actual $LASTEXITCODE -Message "Native argument probe should compile."
    Assert-True -Condition (Test-Path $fakeScrcpy) -Message "Native argument probe executable should exist."

    $nativeArgs = @(
        "--serial=USB123",
        "--window-title=Android Device [USB123]",
        "--max-fps=60"
    )
    $nativeExit = Invoke-Scrcpy $fakeScrcpy $nativeArgs $false
    Assert-Equal -Expected 23 -Actual $nativeExit -Message "Invoke-Scrcpy should return the native process exit code."

    $received = @(Get-Content $scrcpyArgLog | ForEach-Object { [string]$_ })
    Assert-Equal -Expected @(
        "--serial=USB123",
        "--window-title=Android Device [USB123]",
        "--max-fps=60"
    ) -Actual $received -Message "Native .exe invocation must preserve every scrcpy argument boundary."

    Write-Host "[powershell] Testing healthy native stderr does not become a supervisor failure..."
    $env:AHM_SCRCPY_EXIT_CODE = "0"
    $healthyExit = Invoke-Scrcpy $fakeScrcpy $nativeArgs $false
    Assert-Equal -Expected 0 -Actual $healthyExit -Message "Informational native stderr must not turn a healthy scrcpy exit into a PowerShell failure."
    Assert-Equal -Expected "Stop" -Actual $ErrorActionPreference -Message "Invoke-Scrcpy must restore the caller's ErrorActionPreference."

    Write-Host "[powershell] Testing real device preparation commands..."
    $prepareLog = Join-Path $temp "prepare.log"
    $env:AHM_PREPARE_LOG = $prepareLog
    $fakePrepareAdb = Join-Path $temp "fake-prepare-adb.cmd"
    @'
@echo off
echo %*>>"%AHM_PREPARE_LOG%"
exit /b 0
'@ | Set-Content -Path $fakePrepareAdb -Encoding ASCII

    Prepare-DeviceForMirror $fakePrepareAdb "USB123"

    $prepareLines = @(Get-Content $prepareLog)
    Assert-True -Condition (@($prepareLines | Where-Object { $_ -match '-s USB123 shell input keyevent KEYCODE_WAKEUP' }).Count -eq 1) -Message "Prepare-DeviceForMirror should send KEYCODE_WAKEUP."
    Assert-True -Condition (@($prepareLines | Where-Object { $_ -match '-s USB123 shell wm dismiss-keyguard' }).Count -eq 1) -Message "Prepare-DeviceForMirror should request best-effort keyguard dismissal."

    $script:Config.WakeBeforeMirror = $false
    $script:Config.DismissKeyguardWhenPossible = $false
    Clear-Content $prepareLog
    Prepare-DeviceForMirror $fakePrepareAdb "USB123"
    Assert-Equal -Expected 0 -Actual @(Get-Content $prepareLog -ErrorAction SilentlyContinue).Count -Message "Disabled preparation controls should emit no ADB commands."

    Write-Host "[powershell] Testing lock-screen choice reset utility..."
    $resetSandbox = Join-Path $temp "reset-sandbox"
    New-Item -ItemType Directory -Force -Path $resetSandbox | Out-Null
    $isolatedReset = Join-Path $resetSandbox "Reset-LockScreenChoices.ps1"
    Copy-Item -Path $ResetLockScreenPath -Destination $isolatedReset
    $resetStatePath = Join-Path $resetSandbox "state.json"
    $resetCalibrationDir = Join-Path $resetSandbox "pattern-calibration"
    New-Item -ItemType Directory -Force -Path $resetCalibrationDir | Out-Null
    '{"Version":1,"Serial":"USB123","Left":0.2,"Top":0.3,"Right":0.8,"Bottom":0.7}' | Set-Content (Join-Path $resetCalibrationDir "USB123.json") -Encoding UTF8
    '{"Version":1,"Serial":"USB456","Left":0.2,"Top":0.3,"Right":0.8,"Bottom":0.7}' | Set-Content (Join-Path $resetCalibrationDir "USB456.json") -Encoding UTF8

    [pscustomobject]@{
        PreferredSerial = "USB123"
        WirelessHosts = @("192.168.1.20")
        DeviceProfiles = @(
            [pscustomobject]@{ Serial = "USB123"; LockScreenMode = "pattern" },
            [pscustomobject]@{ Serial = "USB456"; LockScreenMode = "none" }
        )
    } | ConvertTo-Json -Depth 6 | Set-Content -Path $resetStatePath -Encoding UTF8

    & $isolatedReset -Serial "USB123"
    $resetState = Get-Content $resetStatePath -Raw | ConvertFrom-Json
    Assert-Equal -Expected "USB123" -Actual $resetState.PreferredSerial -Message "Resetting lock mode must preserve preferred serial."
    Assert-Equal -Expected @("192.168.1.20") -Actual @($resetState.WirelessHosts) -Message "Resetting lock mode must preserve wireless hosts."
    Assert-Equal -Expected 1 -Actual @($resetState.DeviceProfiles).Count -Message "Per-device reset should remove only one device profile."
    Assert-Equal -Expected "USB456" -Actual @($resetState.DeviceProfiles)[0].Serial -Message "Other device profiles must remain."
    Assert-False -Condition (Test-Path (Join-Path $resetCalibrationDir "USB123.json")) -Message "Per-device reset should remove that device's calibration."
    Assert-True -Condition (Test-Path (Join-Path $resetCalibrationDir "USB456.json")) -Message "Per-device reset must preserve other device calibrations."

    & $isolatedReset -Serial "ALL"
    $resetState = Get-Content $resetStatePath -Raw | ConvertFrom-Json
    Assert-Equal -Expected 0 -Actual @($resetState.DeviceProfiles).Count -Message "ALL should clear every lock-screen choice."
    Assert-Equal -Expected 0 -Actual @(Get-ChildItem $resetCalibrationDir -Filter "*.json" -ErrorAction SilentlyContinue).Count -Message "ALL should clear every saved calibration."

    Write-Host "[powershell] Testing real STOP lifecycle in an isolated directory..."
    $stopSandbox = Join-Path $temp "stop-sandbox"
    New-Item -ItemType Directory -Force -Path $stopSandbox | Out-Null
    $isolatedStop = Join-Path $stopSandbox "Stop-PhoneMirror.ps1"
    Copy-Item -Path $StopPath -Destination $isolatedStop

    & $isolatedStop

    $flag = Join-Path $stopSandbox "stop.flag"
    Assert-True -Condition (Test-Path $flag) -Message "STOP must persist stop.flag."
    $flagText = (Get-Content $flag -Raw).Trim()
    $parsedTimestamp = [DateTime]::MinValue
    Assert-True -Condition ([DateTime]::TryParse($flagText, [ref]$parsedTimestamp)) -Message "stop.flag should contain a parseable timestamp."

    Write-Host "[powershell] Testing destructive-operation guards..."
    $supervisor = Get-Content $SupervisorPath -Raw
    Assert-False -Condition ($supervisor -match 'Remove-Item\s+-Force\s+\$StopFile') -Message "Supervisor must not clear persistent OFF state."

    $stopSource = Get-Content $StopPath -Raw
    Assert-False -Condition ($stopSource -match 'kill-server') -Message "STOP must not kill the shared ADB server."
    Assert-True -Condition ($stopSource -match 'PatternOverlay\\.ps1') -Message "STOP should clean pattern overlay sidecars."
    Assert-True -Condition ($stopSource -match 'MirrorChrome\\.ps1') -Message "STOP should clean mirror toolbar/host-zoom sidecars."
}
finally {
    if (Test-Path $temp) {
        Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue
    }
}

Write-Host ""
Write-Host "PowerShell behavior tests passed: $script:Assertions assertions." -ForegroundColor Green
