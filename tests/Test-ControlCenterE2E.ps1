[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$ControlCenterPath = Join-Path $Root "ControlCenter.ps1"
$DeviceControlPath = Join-Path $Root "DeviceControl.ps1"

$script:Assertions = 0

function Assert-True {
    param([bool]$Condition, [string]$Message)
    $script:Assertions++
    if (-not $Condition) { throw "ASSERTION FAILED: $Message" }
}

function Assert-False {
    param([bool]$Condition, [string]$Message)
    Assert-True (-not $Condition) $Message
}

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    $script:Assertions++
    if ($Expected -is [array] -or $Actual -is [array]) {
        $expectedJson = @($Expected) | ConvertTo-Json -Compress
        $actualJson = @($Actual) | ConvertTo-Json -Compress
        if ($expectedJson -ne $actualJson) {
            throw "ASSERTION FAILED: $Message Expected=$expectedJson Actual=$actualJson"
        }
        return
    }

    if ($Expected -ne $Actual) {
        throw "ASSERTION FAILED: $Message Expected=[$Expected] Actual=[$Actual]"
    }
}

function Assert-Contains {
    param($Collection, $Value, [string]$Message)
    Assert-True (@($Collection) -contains $Value) $Message
}

function Click-Control([string]$Name) {
    $control = C $Name
    $control.RaiseEvent((New-Object System.Windows.RoutedEventArgs([System.Windows.Controls.Button]::ClickEvent)))
    [System.Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke(
        [Action]{},
        [System.Windows.Threading.DispatcherPriority]::Background
    )
}

function Select-Tab([string]$Name) {
    (C "MainTabs").SelectedItem = C $Name
    [System.Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke(
        [Action]{},
        [System.Windows.Threading.DispatcherPriority]::Background
    )
}

Write-Host "[control-center] Loading real WPF control center against deterministic fake device..."
. $ControlCenterPath -Serial "TEST123" -TestMode

Assert-Equal "Samsung Galaxy S21 Ultra" (C "DeviceNameText").Text "Fake device identity should populate title bar."
Assert-True ((C "DeviceMetaText").Text -match "SM-G998B") "Device metadata should include model."
Assert-Equal "Connected" (C "ConnectionText").Text "Test-mode device should be connected."

Write-Host "[control-center] Exercising all top-level tabs..."
foreach ($tab in @("ControlsTab","PcSettingsTab","DeviceSettingsTab","AdvancedTab","DiagnosticsTab")) {
    Select-Tab $tab
    Assert-True ((C $tab).IsSelected) "$tab should be selectable."
}

Write-Host "[control-center] Exercising complete scrcpy runtime control surface..."
$runtimeButtons = @{
    FullscreenButton="scrcpy:fullscreen"
    FitButton="scrcpy:fit"
    PixelPerfectButton="scrcpy:pixel-perfect"
    RotateLeftButton="scrcpy:rotate-left"
    RotateRightButton="scrcpy:rotate-right"
    FlipHorizontalButton="scrcpy:flip-horizontal"
    FlipVerticalButton="scrcpy:flip-vertical"
    PauseButton="scrcpy:pause"
    UnpauseButton="scrcpy:resume"
    ResetCaptureButton="scrcpy:reset-capture"
    FpsButton="scrcpy:fps"
    HomeButton="scrcpy:home"
    BackButton="scrcpy:back"
    AppsButton="scrcpy:apps"
    MenuButton="scrcpy:menu"
    PowerButton="scrcpy:power"
    SleepButton="scrcpy:sleep"
    WakeButton="scrcpy:wake"
    RotateDeviceButton="scrcpy:rotate-device"
    NotificationsButton="scrcpy:notifications"
    QuickSettingsButton="scrcpy:quick-settings"
    CollapsePanelsButton="scrcpy:collapse-panels"
    VolumeDownButton="scrcpy:volume-down"
    VolumeUpButton="scrcpy:volume-up"
    CopyButton="scrcpy:copy"
    CutButton="scrcpy:cut"
    PasteSyncButton="scrcpy:paste-sync"
    PasteInjectButton="scrcpy:paste-inject"
    KeyboardSettingsButton="scrcpy:keyboard-settings"
}

foreach ($buttonName in $runtimeButtons.Keys) {
    $before = $script:TestActions.Count
    Click-Control $buttonName
    Assert-Equal ($before + 1) $script:TestActions.Count "$buttonName should dispatch exactly one test action."
    Assert-Equal $runtimeButtons[$buttonName] $script:TestActions[$script:TestActions.Count - 1] "$buttonName should dispatch the correct scrcpy action."
}

Write-Host "[control-center] Exercising host-only zoom reset..."
Click-Control "ResetHostZoomButton"
Assert-Contains $script:TestActions "chrome:reset-zoom" "Reset host zoom should dispatch to MirrorChrome."

Write-Host "[control-center] Exercising screenshot and folder flows..."
Click-Control "ScreenshotButton"
Assert-Contains $script:TestActions "capture:screenshot" "Screenshot button should dispatch capture action."
Assert-True ((C "CaptureStatusText").Text -match "Test screenshot") "Screenshot status should confirm success in fake mode."
Click-Control "OpenScreenshotFolderButton"
Assert-Contains $script:TestActions "capture:open-folder" "Open screenshot folder should dispatch."

Write-Host "[control-center] Exercising PC settings validation and save/discard flows..."
Select-Tab "PcSettingsTab"
(C "PcMaxFpsText").Text = "999"
Click-Control "SavePcSettingsButton"
Assert-True ((C "StatusBarText").Text -match "1-240") "Invalid max FPS should be rejected."

(C "PcMaxFpsText").Text = "90"
(C "PcMaxSizeText").Text = "1600"
(C "PcVideoBitRateText").Text = "10M"
(C "PcHostZoomMaxText").Text = "3.5"
(C "PcAudioBufferText").Text = "80"
(C "PcRecordDirectoryText").Text = "captures/recordings"
(C "PcRecordOnStartCheck").IsChecked = $true
(C "PcFullscreenCheck").IsChecked = $true
(C "PcAudioDupCheck").IsChecked = $true
Select-ComboTag (C "PcVideoCodecCombo") "h265"
Select-ComboTag (C "PcAudioCodecCombo") "aac"

$beforeSave = $script:TestActions.Count
Click-Control "SavePcSettingsButton"
Assert-Equal ($beforeSave + 1) $script:TestActions.Count "Valid PC settings should dispatch one save action."
Assert-Equal "pc-settings:save" $script:TestActions[$script:TestActions.Count - 1] "PC settings save action should be recorded."
Assert-Equal 90 $Config.MaxFps "Max FPS should update in memory."
Assert-Equal 1600 $Config.MaxSize "Max size should update in memory."
Assert-Equal "10M" $Config.VideoBitRate "Bitrate should update in memory."
Assert-Equal "h265" $Config.ScrcpySession.VideoCodec "Video codec should update in memory."
Assert-Equal "aac" $Config.ScrcpySession.AudioCodec "Audio codec should update in memory."
Assert-True $Config.ScrcpySession.RecordOnStart "Recording preference should update in memory."
Assert-True $Config.ScrcpySession.Fullscreen "Fullscreen preference should update in memory."
Assert-True $Config.ScrcpySession.AudioDup "Audio duplication preference should update in memory."

(C "PcMaxFpsText").Text = "33"
Click-Control "ReloadPcSettingsButton"
Assert-Equal "90" (C "PcMaxFpsText").Text "Discard changes should reload saved in-memory config."

Write-Host "[control-center] Exercising friendly Android device controls..."
Select-Tab "DeviceSettingsTab"

$deviceButtons = @{
    WifiOnButton="device:wifi=enable"
    WifiOffButton="device:wifi=disable"
    MobileDataOnButton="device:mobile-data=enable"
    MobileDataOffButton="device:mobile-data=disable"
    AirplaneOnButton="device:airplane-mode=enable"
    AirplaneOffButton="device:airplane-mode=disable"
    ApplyWmSizeButton="device:wm-size=1080x2400"
    ResetWmSizeButton="device:wm-size=reset"
    ApplyWmDensityButton="device:wm-density=420"
    ResetWmDensityButton="device:wm-density=reset"
}

foreach ($buttonName in $deviceButtons.Keys) {
    if ($buttonName -eq "ApplyWmSizeButton") { (C "DeviceWmSizeText").Text = "1080x2400" }
    if ($buttonName -eq "ApplyWmDensityButton") { (C "DeviceWmDensityText").Text = "420" }

    Click-Control $buttonName
    Assert-Equal $deviceButtons[$buttonName] $script:TestActions[$script:TestActions.Count - 1] "$buttonName should dispatch expected device action."
}

(C "DeviceAutoRotateCheck").IsChecked = $false
Click-Control "DeviceAutoRotateCheck"
Assert-Contains $script:TestActions "device:auto-rotate=0" "Auto rotate checkbox should dispatch off."

(C "DeviceShowTouchesCheck").IsChecked = $true
Click-Control "DeviceShowTouchesCheck"
Assert-Contains $script:TestActions "device:show-touches=1" "Show touches should dispatch on."

(C "DeviceStayAwakeCheck").IsChecked = $false
Click-Control "DeviceStayAwakeCheck"
Assert-Contains $script:TestActions "device:stay-awake=0" "Stay awake should dispatch off."

Click-Control "DeviceWakeButton"
Assert-Contains $script:TestActions "adb:KEYCODE_WAKEUP" "Wake button should dispatch ADB wake."

Click-Control "DeviceSleepButton"
Assert-Contains $script:TestActions "scrcpy:sleep" "Device sleep should use scrcpy screen-off control."

Write-Host "[control-center] Exercising advanced settings enumeration, filtering and guardrails..."
Select-Tab "AdvancedTab"
Refresh-AdvancedRows
Assert-Equal 4 @((C "AdvancedSettingsGrid").ItemsSource).Count "Fake namespace should expose four rows."

(C "AdvancedSearchText").Text = "font"
[System.Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([Action]{}, [System.Windows.Threading.DispatcherPriority]::Background)
Assert-Equal 1 @((C "AdvancedSettingsGrid").ItemsSource).Count "Advanced search should filter rows."
Assert-Equal "font_scale" @((C "AdvancedSettingsGrid").ItemsSource)[0].Key "Advanced search should find font_scale."

(C "AdvancedSearchText").Text = ""
Apply-AdvancedFilter
$protected = @((C "AdvancedSettingsGrid").ItemsSource | Where-Object { $_.Key -eq "adb_enabled" })[0]
(C "AdvancedSettingsGrid").SelectedItem = $protected
(C "AdvancedKeyText").Text = "adb_enabled"
(C "AdvancedValueText").Text = "0"
$beforeProtected = $script:TestActions.Count
Click-Control "AdvancedWriteButton"
Assert-Equal $beforeProtected $script:TestActions.Count "Protected adb_enabled write must never dispatch."
Assert-True ((C "StatusBarText").Text -match "protected") "Protected write should explain why it is blocked."

(C "AdvancedKeyText").Text = "font_scale"
(C "AdvancedValueText").Text = "1.15"
Click-Control "AdvancedWriteButton"
Assert-Contains $script:TestActions "advanced-write:system:font_scale=1.15" "Normal advanced write should dispatch."

(C "AdvancedKeyText").Text = "font_scale"
Click-Control "AdvancedDeleteButton"
Assert-Contains $script:TestActions "advanced-delete:system:font_scale" "Normal advanced delete should dispatch."

Write-Host "[control-center] Exercising diagnostics..."
Select-Tab "DiagnosticsTab"
Click-Control "RefreshDiagnosticsButton"
Assert-True ((C "DiagnosticsText").Text -match "Samsung Galaxy S21 Ultra") "Diagnostics should include device identity."
Assert-True ((C "DiagnosticsText").Text -match "AUTHORIZED") "Diagnostics should include authorization state."
Click-Control "OpenLogsButton"
Assert-Contains $script:TestActions "diagnostics:open-logs" "Open logs should dispatch."

Write-Host "[control-center] Verifying every named button has an exercised flow..."
$NamespaceManager.AddNamespace("d", "http://schemas.microsoft.com/winfx/2006/xaml/presentation")
$xamlButtons = @(
    $Xaml.SelectNodes("//d:Button[@x:Name]", $NamespaceManager) |
        ForEach-Object { [string]$_.GetAttribute("Name", "http://schemas.microsoft.com/winfx/2006/xaml") }
)
$exercised = @(
    $runtimeButtons.Keys +
    @(
        "RefreshAllButton","ResetHostZoomButton","ScreenshotButton","OpenScreenshotFolderButton",
        "SavePcSettingsButton","ReloadPcSettingsButton","DeviceAutoRotateCheck",
        "WifiOnButton","WifiOffButton","MobileDataOnButton","MobileDataOffButton",
        "AirplaneOnButton","AirplaneOffButton","ApplyWmSizeButton","ResetWmSizeButton",
        "ApplyWmDensityButton","ResetWmDensityButton","DeviceWakeButton","DeviceSleepButton",
        "AdvancedRefreshButton","AdvancedWriteButton","AdvancedDeleteButton",
        "RefreshDiagnosticsButton","OpenLogsButton"
    )
)
foreach ($buttonName in $xamlButtons) {
    Assert-True ($exercised -contains $buttonName) "Button '$buttonName' must have an explicit E2E flow."
}

Write-Host "[control-center] Testing DeviceControl against a fake adb executable..."
. $DeviceControlPath
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("ahm-control-e2e-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $temp | Out-Null

try {
    $fakeAdb = Join-Path $temp "adb.cmd"
    $log = Join-Path $temp "adb.log"
    $env:AHM_FAKE_ADB_LOG = $log

    @'
@echo off
echo %*>>"%AHM_FAKE_ADB_LOG%"
set args=%*
echo %args% | findstr /C:"getprop ro.product.manufacturer" >nul && (echo Samsung& exit /b 0)
echo %args% | findstr /C:"getprop ro.product.model" >nul && (echo SM-G998B& exit /b 0)
echo %args% | findstr /C:"getprop ro.build.version.release" >nul && (echo 15& exit /b 0)
echo %args% | findstr /C:"getprop ro.build.version.sdk" >nul && (echo 35& exit /b 0)
echo %args% | findstr /C:"settings list system" >nul && (
  echo screen_brightness=128
  echo font_scale=1.0
  echo adb_enabled=1
  exit /b 0
)
echo %args% | findstr /C:"settings get system screen_off_timeout" >nul && (echo 60000& exit /b 0)
echo %args% | findstr /C:"cmd -l" >nul && (
  echo package
  echo connectivity
  echo uimode
  exit /b 0
)
echo OK
exit /b 0
'@ | Set-Content -Path $fakeAdb -Encoding ASCII

    $identity = Get-AndroidDeviceIdentity $fakeAdb "USB123"
    Assert-Equal "Samsung" $identity.Manufacturer "Fake ADB manufacturer should parse."
    Assert-Equal "SM-G998B" $identity.Model "Fake ADB model should parse."
    Assert-Equal "15" $identity.AndroidVersion "Fake ADB Android version should parse."

    $namespace = Get-AndroidSettingsNamespace $fakeAdb "USB123" "system"
    Assert-True $namespace.Ok "settings list system should succeed."
    Assert-Equal 3 @($namespace.Rows).Count "settings list should parse every key."
    Assert-Equal "protected" @($namespace.Rows | Where-Object Key -eq "adb_enabled")[0].Risk "adb_enabled should be protected."

    $protectedWrite = Set-AndroidSetting $fakeAdb "USB123" "global" "adb_enabled" "0"
    Assert-False $protectedWrite.Ok "Protected ADB setting write should be blocked before process execution."

    $normalWrite = Set-AndroidSetting $fakeAdb "USB123" "system" "font_scale" "1.1"
    Assert-True $normalWrite.Ok "Normal settings write should reach fake ADB."

    $wifi = Set-FriendlyAndroidSetting $fakeAdb "USB123" "wifi" "enable"
    Assert-True $wifi.Ok "Friendly Wi-Fi command should reach fake ADB."

    $services = @(Get-AndroidCommandServices $fakeAdb "USB123")
    Assert-Equal @("connectivity","package","uimode") $services "cmd -l services should parse and sort."

    $logLines = @(Get-Content $log)
    Assert-True (@($logLines | Where-Object { $_ -match "settings put system font_scale" }).Count -eq 1) "Fake ADB log should contain font_scale write."
    Assert-True (@($logLines | Where-Object { $_ -match "svc wifi enable" }).Count -eq 1) "Fake ADB log should contain Wi-Fi enable."

    $failedAdb = Join-Path $temp "adb-fail.cmd"
    @'
@echo off
echo permission denied 1>&2
exit /b 13
'@ | Set-Content -Path $failedAdb -Encoding ASCII

    $failedNamespace = Get-AndroidSettingsNamespace $failedAdb "USB123" "secure"
    Assert-False $failedNamespace.Ok "ADB permission failure should surface as an error result."
    Assert-True ($failedNamespace.Error -match "permission denied") "ADB error text should be preserved."
}
finally {
    if ($null -ne $Window) {
        try { $Window.Close() } catch {}
    }
    if (Test-Path $temp) {
        Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue
    }
}

Write-Host ""
Write-Host ("Control Center E2E passed: " + $script:Assertions + " assertions.") -ForegroundColor Green
