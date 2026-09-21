[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Serial,
    [string]$AdbPath = "",
    [switch]$TestMode,
    [string]$SnapshotPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$ConfigPath = Join-Path $Root "config.json"
$XamlPath = Join-Path $Root "ControlCenter.xaml"

. (Join-Path $Root "DeviceControl.ps1")
. (Join-Path $Root "ScrcpyControl.ps1")

$Config = Get-Content $ConfigPath -Raw | ConvertFrom-Json

if ([string]::IsNullOrWhiteSpace($AdbPath)) {
    $AdbPath = Get-ChildItem (Join-Path $Root "tools") -Filter "adb.exe" -Recurse -File -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not $TestMode -and [string]::IsNullOrWhiteSpace([string]$AdbPath)) {
    throw "adb.exe was not found."
}

Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

[xml]$Xaml = Get-Content $XamlPath -Raw
$Reader = New-Object System.Xml.XmlNodeReader $Xaml
$Window = [Windows.Markup.XamlReader]::Load($Reader)

$Controls = @{}
$NamespaceManager = New-Object System.Xml.XmlNamespaceManager($Xaml.NameTable)
$NamespaceManager.AddNamespace("x", "http://schemas.microsoft.com/winfx/2006/xaml")
foreach ($node in $Xaml.SelectNodes("//*[@x:Name]", $NamespaceManager)) {
    $name = [string]$node.GetAttribute("Name", "http://schemas.microsoft.com/winfx/2006/xaml")
    if (-not [string]::IsNullOrWhiteSpace($name)) {
        $Controls[$name] = $Window.FindName($name)
    }
}

function C([string]$Name) {
    if (-not $Controls.ContainsKey($Name)) { throw "Missing XAML control '$Name'." }
    return $Controls[$Name]
}

$script:TargetWindowTitle = "{0} [{1}]" -f ([string]$Config.WindowTitle), $Serial
$script:DeviceIdentity = $null
$script:AdvancedRows = @()
$script:TestActions = New-Object System.Collections.Generic.List[string]

$Window.Width = [double]$Config.ControlCenter.Width
$Window.Height = [double]$Config.ControlCenter.Height
$Window.Topmost = [bool]$Config.ControlCenter.AlwaysOnTop

function Get-ControlCenterRuntimeDirectory {
    $safeSerial = ($Serial -replace '[^A-Za-z0-9._-]', '_')
    return Join-Path (Join-Path $Root "runtime") $safeSerial
}

function Get-ControlCenterUiStatePath {
    return Join-Path (Get-ControlCenterRuntimeDirectory) "control-center-state.json"
}

function Restore-ControlCenterTab {
    if ($TestMode -or -not [bool]$Config.ControlCenter.RememberLastTab) { return }

    $path = Get-ControlCenterUiStatePath
    if (-not (Test-Path $path)) { return }

    try {
        $state = Get-Content $path -Raw | ConvertFrom-Json
        $name = [string]$state.LastTab
        if (-not [string]::IsNullOrWhiteSpace($name) -and $Controls.ContainsKey($name)) {
            (C "MainTabs").SelectedItem = C $name
        }
    }
    catch {}
}

function Save-ControlCenterTab {
    if ($TestMode -or -not [bool]$Config.ControlCenter.RememberLastTab) { return }

    $selected = (C "MainTabs").SelectedItem
    if ($null -eq $selected -or [string]::IsNullOrWhiteSpace([string]$selected.Name)) { return }

    $directory = Get-ControlCenterRuntimeDirectory
    New-Item -ItemType Directory -Force -Path $directory | Out-Null

    [pscustomobject]@{
        LastTab = [string]$selected.Name
    } | ConvertTo-Json -Compress | Set-Content -Path (Get-ControlCenterUiStatePath) -Encoding UTF8
}

function Apply-ControlCenterWindowPlacement {
    if ($TestMode -or -not [bool]$Config.ControlCenter.DockToMirror) { return }

    try {
        $rect = Get-ScrcpyWindowRect $script:TargetWindowTitle
        if ($null -eq $rect) { return }

        $work = [System.Windows.SystemParameters]::WorkArea
        $gap = 12.0
        $preferredLeft = [double]$rect.Right + $gap
        $fallbackLeft = [double]$rect.Left - $Window.Width - $gap

        $Window.WindowStartupLocation = [System.Windows.WindowStartupLocation]::Manual

        if (($preferredLeft + $Window.Width) -le $work.Right) {
            $Window.Left = $preferredLeft
        }
        elseif ($fallbackLeft -ge $work.Left) {
            $Window.Left = $fallbackLeft
        }
        else {
            $Window.Left = [Math]::Max($work.Left, [Math]::Min($work.Right - $Window.Width, [double]$rect.Left))
        }

        $Window.Top = [Math]::Max($work.Top, [Math]::Min($work.Bottom - $Window.Height, [double]$rect.Top))
    }
    catch {}
}

function Add-TestAction([string]$Action) {
    if ($TestMode) { $script:TestActions.Add($Action) }
}

function Set-Status([string]$Text, [bool]$IsError = $false) {
    (C "StatusBarText").Text = $Text
    (C "StatusBarText").Foreground = [System.Windows.Media.BrushConverter]::new().ConvertFromString(
        $(if ($IsError) { "#FF774D" } else { "#858D83" })
    )
}

function Get-ComboTag($Combo) {
    if ($null -eq $Combo.SelectedItem) { return "" }
    return [string]$Combo.SelectedItem.Tag
}

function Select-ComboTag($Combo, [string]$Value) {
    foreach ($item in $Combo.Items) {
        if ([string]$item.Tag -eq $Value) {
            $Combo.SelectedItem = $item
            return
        }
    }
}

function Invoke-UiScrcpyAction([string]$Name) {
    Add-TestAction ("scrcpy:" + $Name)
    $result = Invoke-ScrcpyNamedShortcut -WindowTitle $script:TargetWindowTitle -Name $Name -TestMode:$TestMode
    Set-Status $result.Text (-not $result.Ok)
}

$ShortcutButtons = @{
    FullscreenButton="fullscreen"
    FitButton="fit"
    PixelPerfectButton="pixel-perfect"
    RotateLeftButton="rotate-left"
    RotateRightButton="rotate-right"
    FlipHorizontalButton="flip-horizontal"
    FlipVerticalButton="flip-vertical"
    PauseButton="pause"
    UnpauseButton="resume"
    ResetCaptureButton="reset-capture"
    FpsButton="fps"
    HomeButton="home"
    BackButton="back"
    AppsButton="apps"
    MenuButton="menu"
    PowerButton="power"
    SleepButton="sleep"
    WakeButton="wake"
    RotateDeviceButton="rotate-device"
    NotificationsButton="notifications"
    QuickSettingsButton="quick-settings"
    CollapsePanelsButton="collapse-panels"
    VolumeDownButton="volume-down"
    VolumeUpButton="volume-up"
    CopyButton="copy"
    CutButton="cut"
    PasteSyncButton="paste-sync"
    PasteInjectButton="paste-inject"
    KeyboardSettingsButton="keyboard-settings"
}

foreach ($buttonName in $ShortcutButtons.Keys) {
    $button = C $buttonName
    $button.Add_Click({
        param($sender, $eventArgs)
        Invoke-UiScrcpyAction $ShortcutButtons[$sender.Name]
    })
}

function Get-RuntimeDirectory {
    $safeSerial = ($Serial -replace '[^A-Za-z0-9._-]', '_')
    return Join-Path (Join-Path $Root "runtime") $safeSerial
}

function Send-MirrorChromeCommand([string]$Command) {
    Add-TestAction ("chrome:" + $Command)

    if ($TestMode) {
        Set-Status ("Test mirror command: " + $Command)
        return
    }

    $directory = Get-RuntimeDirectory
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    Set-Content -Path (Join-Path $directory "mirror-chrome.command") -Value $Command -Encoding ASCII
}

(C "ResetHostZoomButton").Add_Click({ Send-MirrorChromeCommand "reset-zoom" })

function Ensure-ExtraScrcpyArgs {
    if ($null -eq $Config.PSObject.Properties["ExtraScrcpyArgs"]) {
        $Config | Add-Member -NotePropertyName ExtraScrcpyArgs -NotePropertyValue ""
    }
}

function Load-PcSettings {
    Ensure-ExtraScrcpyArgs

    (C "PcTurnScreenOffCheck").IsChecked = [bool]$Config.TurnPhysicalScreenOff
    (C "PcStayAwakeCheck").IsChecked = [bool]$Config.StayAwakeWhenUsb
    (C "PcKeepActiveCheck").IsChecked = [bool]$Config.KeepActiveDuringMirror
    (C "PcDismissKeyguardCheck").IsChecked = [bool]$Config.DismissKeyguardWhenPossible
    (C "PcRestartUnexpectedCheck").IsChecked = [bool]$Config.RestartOnUnexpectedExit
    (C "PcPreferUsbCheck").IsChecked = [bool]$Config.PreferUsb
    (C "PcMaxSizeText").Text = [string]$Config.MaxSize
    (C "PcMaxFpsText").Text = [string]$Config.MaxFps
    (C "PcVideoBitRateText").Text = [string]$Config.VideoBitRate

    (C "PcNativeTouchpadCheck").IsChecked = [bool]$Config.MirrorChrome.NativeTouchpadGestures
    (C "PcTouchpadAndroidPinchCheck").IsChecked = [bool]$Config.MirrorChrome.TouchpadPinchToAndroid
    (C "PcCtrlTouchpadHostZoomCheck").IsChecked = [bool]$Config.MirrorChrome.CtrlTouchpadPinchToHostZoom
    (C "PcCtrlWheelHostZoomCheck").IsChecked = [bool]$Config.MirrorChrome.CtrlWheelZoom
    (C "PcHostZoomMaxText").Text = [string]$Config.MirrorChrome.MaxZoom

    (C "PcPatternOverlayCheck").IsChecked = [bool]$Config.PatternOverlay.Enabled
    (C "PcPatternAutoDiscoverCheck").IsChecked = [bool]$Config.PatternOverlay.AutoDiscoverGeometry
    (C "PcPatternCalibrationCheck").IsChecked = [bool]$Config.PatternOverlay.CalibrationEnabled

    (C "PcWirelessEnabledCheck").IsChecked = [bool]$Config.Wireless.Enabled
    (C "PcWirelessTcpipBootstrapCheck").IsChecked = [bool]$Config.Wireless.EnableTcpipWhenUsbAvailable

    Select-ComboTag (C "PcVideoCodecCombo") ([string]$Config.ScrcpySession.VideoCodec)
    (C "PcFullscreenCheck").IsChecked = [bool]$Config.ScrcpySession.Fullscreen
    (C "PcAlwaysOnTopCheck").IsChecked = [bool]$Config.ScrcpySession.AlwaysOnTop
    (C "PcDisableScreensaverCheck").IsChecked = [bool]$Config.ScrcpySession.DisableScreensaver
    (C "PcAudioEnabledCheck").IsChecked = [bool]$Config.ScrcpySession.AudioEnabled
    Select-ComboTag (C "PcAudioCodecCombo") ([string]$Config.ScrcpySession.AudioCodec)
    (C "PcAudioBufferText").Text = [string]$Config.ScrcpySession.AudioBufferMs
    (C "PcAudioDupCheck").IsChecked = [bool]$Config.ScrcpySession.AudioDup
    (C "PcRecordOnStartCheck").IsChecked = [bool]$Config.ScrcpySession.RecordOnStart
    (C "PcRecordDirectoryText").Text = [string]$Config.ScrcpySession.RecordDirectory

    (C "PcControlCenterEnabledCheck").IsChecked = [bool]$Config.ControlCenter.Enabled
    (C "PcControlCenterOpenCheck").IsChecked = [bool]$Config.ControlCenter.OpenOnLaunch
    (C "PcControlCenterDockCheck").IsChecked = [bool]$Config.ControlCenter.DockToMirror
    (C "PcControlCenterTopmostCheck").IsChecked = [bool]$Config.ControlCenter.AlwaysOnTop
    (C "PcControlCenterRememberTabCheck").IsChecked = [bool]$Config.ControlCenter.RememberLastTab
    (C "PcAdvancedWritesEnabledCheck").IsChecked = [bool]$Config.ControlCenter.AdvancedSettingsWritesEnabled
    (C "PcConfirmAdvancedWritesCheck").IsChecked = [bool]$Config.ControlCenter.ConfirmSensitiveDeviceWrites
    (C "PcControlCenterWidthText").Text = [string]$Config.ControlCenter.Width
    (C "PcControlCenterHeightText").Text = [string]$Config.ControlCenter.Height

    (C "PcExtraScrcpyArgsText").Text = [string]$Config.ExtraScrcpyArgs
}

function Save-PcSettings {
    $maxSize = 0
    $maxFps = 0
    $maxZoom = 0.0

    if (-not [int]::TryParse((C "PcMaxSizeText").Text, [ref]$maxSize) -or $maxSize -lt 320 -or $maxSize -gt 8192) {
        Set-Status "Max size must be 320-8192." $true
        return
    }

    if (-not [int]::TryParse((C "PcMaxFpsText").Text, [ref]$maxFps) -or $maxFps -lt 1 -or $maxFps -gt 240) {
        Set-Status "Max FPS must be 1-240." $true
        return
    }

    if (-not [double]::TryParse((C "PcHostZoomMaxText").Text, [ref]$maxZoom) -or $maxZoom -lt 1.0 -or $maxZoom -gt 8.0) {
        Set-Status "Host zoom max must be 1-8." $true
        return
    }

    $bitrate = (C "PcVideoBitRateText").Text.Trim()
    if ($bitrate -notmatch '^\d+(K|M)?$') {
        Set-Status "Video bitrate must look like 8000K or 12M." $true
        return
    }

    $rawArgs = (C "PcExtraScrcpyArgsText").Text.Trim()
    if ($rawArgs.Length -gt 4096 -or $rawArgs -match '[\r\n\x00]') {
        Set-Status "Raw scrcpy arguments contain unsupported content." $true
        return
    }

    $audioBuffer = 0
    if (-not [int]::TryParse((C "PcAudioBufferText").Text, [ref]$audioBuffer) -or $audioBuffer -lt 0 -or $audioBuffer -gt 5000) {
        Set-Status "Audio buffer must be 0-5000 ms." $true
        return
    }

    $recordDirectory = (C "PcRecordDirectoryText").Text.Trim()
    if ([string]::IsNullOrWhiteSpace($recordDirectory) -or $recordDirectory -match '(^|[\\/])\.\.([\\/]|$)' -or $recordDirectory -match '[\r\n\x00]') {
        Set-Status "Recording folder must be a safe relative path." $true
        return
    }

    $centerWidth = 0
    $centerHeight = 0
    if (-not [int]::TryParse((C "PcControlCenterWidthText").Text, [ref]$centerWidth) -or $centerWidth -lt 900 -or $centerWidth -gt 2400) {
        Set-Status "Control Center width must be 900-2400." $true
        return
    }
    if (-not [int]::TryParse((C "PcControlCenterHeightText").Text, [ref]$centerHeight) -or $centerHeight -lt 640 -or $centerHeight -gt 1800) {
        Set-Status "Control Center height must be 640-1800." $true
        return
    }

    $Config.TurnPhysicalScreenOff = [bool](C "PcTurnScreenOffCheck").IsChecked
    $Config.StayAwakeWhenUsb = [bool](C "PcStayAwakeCheck").IsChecked
    $Config.KeepActiveDuringMirror = [bool](C "PcKeepActiveCheck").IsChecked
    $Config.DismissKeyguardWhenPossible = [bool](C "PcDismissKeyguardCheck").IsChecked
    $Config.RestartOnUnexpectedExit = [bool](C "PcRestartUnexpectedCheck").IsChecked
    $Config.PreferUsb = [bool](C "PcPreferUsbCheck").IsChecked
    $Config.MaxSize = $maxSize
    $Config.MaxFps = $maxFps
    $Config.VideoBitRate = $bitrate

    $Config.MirrorChrome.NativeTouchpadGestures = [bool](C "PcNativeTouchpadCheck").IsChecked
    $Config.MirrorChrome.TouchpadPinchToAndroid = [bool](C "PcTouchpadAndroidPinchCheck").IsChecked
    $Config.MirrorChrome.CtrlTouchpadPinchToHostZoom = [bool](C "PcCtrlTouchpadHostZoomCheck").IsChecked
    $Config.MirrorChrome.CtrlWheelZoom = [bool](C "PcCtrlWheelHostZoomCheck").IsChecked
    $Config.MirrorChrome.MaxZoom = $maxZoom

    $Config.PatternOverlay.Enabled = [bool](C "PcPatternOverlayCheck").IsChecked
    $Config.PatternOverlay.AutoDiscoverGeometry = [bool](C "PcPatternAutoDiscoverCheck").IsChecked
    $Config.PatternOverlay.CalibrationEnabled = [bool](C "PcPatternCalibrationCheck").IsChecked

    $Config.Wireless.Enabled = [bool](C "PcWirelessEnabledCheck").IsChecked
    $Config.Wireless.EnableTcpipWhenUsbAvailable = [bool](C "PcWirelessTcpipBootstrapCheck").IsChecked

    $Config.ScrcpySession.VideoCodec = Get-ComboTag (C "PcVideoCodecCombo")
    $Config.ScrcpySession.Fullscreen = [bool](C "PcFullscreenCheck").IsChecked
    $Config.ScrcpySession.AlwaysOnTop = [bool](C "PcAlwaysOnTopCheck").IsChecked
    $Config.ScrcpySession.DisableScreensaver = [bool](C "PcDisableScreensaverCheck").IsChecked
    $Config.ScrcpySession.AudioEnabled = [bool](C "PcAudioEnabledCheck").IsChecked
    $Config.ScrcpySession.AudioCodec = Get-ComboTag (C "PcAudioCodecCombo")
    $Config.ScrcpySession.AudioBufferMs = $audioBuffer
    $Config.ScrcpySession.AudioDup = [bool](C "PcAudioDupCheck").IsChecked
    $Config.ScrcpySession.RecordOnStart = [bool](C "PcRecordOnStartCheck").IsChecked
    $Config.ScrcpySession.RecordDirectory = $recordDirectory

    $Config.ControlCenter.Enabled = [bool](C "PcControlCenterEnabledCheck").IsChecked
    $Config.ControlCenter.OpenOnLaunch = [bool](C "PcControlCenterOpenCheck").IsChecked
    $Config.ControlCenter.DockToMirror = [bool](C "PcControlCenterDockCheck").IsChecked
    $Config.ControlCenter.AlwaysOnTop = [bool](C "PcControlCenterTopmostCheck").IsChecked
    $Config.ControlCenter.RememberLastTab = [bool](C "PcControlCenterRememberTabCheck").IsChecked
    $Config.ControlCenter.AdvancedSettingsWritesEnabled = [bool](C "PcAdvancedWritesEnabledCheck").IsChecked
    $Config.ControlCenter.ConfirmSensitiveDeviceWrites = [bool](C "PcConfirmAdvancedWritesCheck").IsChecked
    $Config.ControlCenter.Width = $centerWidth
    $Config.ControlCenter.Height = $centerHeight

    $Window.Topmost = [bool]$Config.ControlCenter.AlwaysOnTop
    $Window.Width = [double]$Config.ControlCenter.Width
    $Window.Height = [double]$Config.ControlCenter.Height

    $writesEnabled = [bool]$Config.ControlCenter.AdvancedSettingsWritesEnabled
    (C "AdvancedWriteButton").IsEnabled = $writesEnabled
    (C "AdvancedDeleteButton").IsEnabled = $writesEnabled
    (C "AdvancedValueText").IsEnabled = $writesEnabled
    (C "AdvancedKeyText").IsReadOnly = -not $writesEnabled

    Apply-ControlCenterWindowPlacement

    $Config.ExtraScrcpyArgs = $rawArgs

    Add-TestAction "pc-settings:save"

    if (-not $TestMode) {
        $Config | ConvertTo-Json -Depth 12 | Set-Content -Path $ConfigPath -Encoding UTF8
    }

    (C "PcSettingsStatusText").Text = "Saved. Launch-time settings apply on the next mirror start."
    Set-Status "PC / mirror settings saved."
}

(C "SavePcSettingsButton").Add_Click({ Save-PcSettings })
(C "ReloadPcSettingsButton").Add_Click({
    Load-PcSettings
    (C "PcSettingsStatusText").Text = "Discarded unsaved changes."
})

function Get-Identity {
    if ($TestMode) {
        return [pscustomobject]@{
            Manufacturer="Samsung"
            Model="SM-G998B"
            AndroidVersion="15"
            ApiLevel="35"
            Serial=$Serial
            DisplayName="Samsung Galaxy S21 Ultra"
        }
    }

    return Get-AndroidDeviceIdentity $AdbPath $Serial
}

function Get-DeviceState {
    if ($TestMode) {
        return [pscustomobject]@{
            Brightness="128"
            BrightnessMode="0"
            ScreenTimeoutMs="60000"
            AutoRotate="1"
            UserRotation="0"
            FontScale="1.0"
            ShowTouches="0"
            StayAwake="7"
            WindowAnimation="1"
            TransitionAnimation="1"
            AnimatorDuration="1"
            WmSize=("Physical size: 1440x3200" + [Environment]::NewLine + "Override size: 1080x2400")
            WmDensity=("Physical density: 515" + [Environment]::NewLine + "Override density: 420")
            UiMode="Night mode: auto"
        }
    }

    return Get-FriendlyAndroidState $AdbPath $Serial
}

function Refresh-DeviceState {
    $state = Get-DeviceState

    $brightness = 128
    [int]::TryParse([string]$state.Brightness, [ref]$brightness) | Out-Null
    (C "DeviceBrightnessSlider").Value = [Math]::Max(1, [Math]::Min(255, $brightness))
    (C "DeviceBrightnessValueText").Text = [string]$brightness

    Select-ComboTag (C "DeviceBrightnessModeCombo") ([string]$state.BrightnessMode)
    Select-ComboTag (C "DeviceTimeoutCombo") ([string]$state.ScreenTimeoutMs)
    (C "DeviceAutoRotateCheck").IsChecked = ([string]$state.AutoRotate -eq "1")
    Select-ComboTag (C "DeviceFontScaleCombo") ([string]$state.FontScale)
    (C "DeviceShowTouchesCheck").IsChecked = ([string]$state.ShowTouches -eq "1")
    (C "DeviceStayAwakeCheck").IsChecked = ([string]$state.StayAwake -ne "0")
    Select-ComboTag (C "DeviceAnimationCombo") ([string]$state.WindowAnimation)

    $mode = "auto"
    if ([string]$state.UiMode -match '(?i)yes|dark') { $mode = "yes" }
    elseif ([string]$state.UiMode -match '(?i)no|light') { $mode = "no" }
    Select-ComboTag (C "DeviceDarkModeCombo") $mode

    if ([string]$state.WmSize -match '(?im)Override size:\s*(\d+x\d+)') {
        (C "DeviceWmSizeText").Text = $Matches[1]
    }
    elseif ([string]$state.WmSize -match '(?im)Physical size:\s*(\d+x\d+)') {
        (C "DeviceWmSizeText").Text = $Matches[1]
    }

    if ([string]$state.WmDensity -match '(?im)Override density:\s*(\d+)') {
        (C "DeviceWmDensityText").Text = $Matches[1]
    }
    elseif ([string]$state.WmDensity -match '(?im)Physical density:\s*(\d+)') {
        (C "DeviceWmDensityText").Text = $Matches[1]
    }
}

function Invoke-Friendly([string]$Id, $Value) {
    Add-TestAction ("device:" + $Id + "=" + [string]$Value)

    if ($TestMode) {
        (C "DeviceSettingsStatusText").Text = "Test: $Id = $Value"
        Set-Status "Test Android setting applied."
        return
    }

    $result = Set-FriendlyAndroidSetting $AdbPath $Serial $Id $Value
    (C "DeviceSettingsStatusText").Text = $result.Text
    Set-Status ($(if ($result.Ok) { "Android setting updated." } else { $result.Text })) (-not $result.Ok)
}

(C "DeviceBrightnessSlider").Add_ValueChanged({
    (C "DeviceBrightnessValueText").Text = [string][int](C "DeviceBrightnessSlider").Value
})
(C "DeviceBrightnessSlider").Add_PreviewMouseUp({ Invoke-Friendly "brightness" ([int](C "DeviceBrightnessSlider").Value) })
(C "DeviceAutoRotateCheck").Add_Click({ Invoke-Friendly "auto-rotate" ($(if ((C "DeviceAutoRotateCheck").IsChecked) { "1" } else { "0" })) })
(C "DeviceShowTouchesCheck").Add_Click({ Invoke-Friendly "show-touches" ($(if ((C "DeviceShowTouchesCheck").IsChecked) { "1" } else { "0" })) })
(C "DeviceStayAwakeCheck").Add_Click({ Invoke-Friendly "stay-awake" ($(if ((C "DeviceStayAwakeCheck").IsChecked) { "7" } else { "0" })) })

$SelectionMappings = @{
    DeviceBrightnessModeCombo=@("brightness-mode")
    DeviceTimeoutCombo=@("screen-timeout-ms")
    DeviceFontScaleCombo=@("font-scale")
    DeviceDarkModeCombo=@("dark-mode")
}
foreach ($comboName in $SelectionMappings.Keys) {
    $combo = C $comboName
    $combo.Add_DropDownClosed({
        param($sender, $eventArgs)
        Invoke-Friendly $SelectionMappings[$sender.Name][0] (Get-ComboTag $sender)
    })
}

(C "DeviceAnimationCombo").Add_DropDownClosed({
    $value = Get-ComboTag (C "DeviceAnimationCombo")
    Invoke-Friendly "animation-window" $value
    Invoke-Friendly "animation-transition" $value
    Invoke-Friendly "animation-duration" $value
})

$FriendlyButtons = @{
    WifiOnButton=@("wifi","enable")
    WifiOffButton=@("wifi","disable")
    MobileDataOnButton=@("mobile-data","enable")
    MobileDataOffButton=@("mobile-data","disable")
    AirplaneOnButton=@("airplane-mode","enable")
    AirplaneOffButton=@("airplane-mode","disable")
}
foreach ($buttonName in $FriendlyButtons.Keys) {
    $button = C $buttonName
    $button.Add_Click({
        param($sender, $eventArgs)
        $spec = $FriendlyButtons[$sender.Name]
        Invoke-Friendly $spec[0] $spec[1]
    })
}

(C "ApplyWmSizeButton").Add_Click({ Invoke-Friendly "wm-size" (C "DeviceWmSizeText").Text.Trim() })
(C "ResetWmSizeButton").Add_Click({ Invoke-Friendly "wm-size" "reset" })
(C "ApplyWmDensityButton").Add_Click({ Invoke-Friendly "wm-density" (C "DeviceWmDensityText").Text.Trim() })
(C "ResetWmDensityButton").Add_Click({ Invoke-Friendly "wm-density" "reset" })

(C "DeviceWakeButton").Add_Click({
    Add-TestAction "adb:KEYCODE_WAKEUP"
    if (-not $TestMode) {
        Invoke-AdbText $AdbPath $Serial @("shell","input","keyevent","KEYCODE_WAKEUP") | Out-Null
    }
    Set-Status "Wake signal sent."
})
(C "DeviceSleepButton").Add_Click({ Invoke-UiScrcpyAction "sleep" })

function Get-FakeAdvancedRows([string]$Namespace) {
    return @(
        [pscustomobject]@{ Namespace=$Namespace; Key="screen_off_timeout"; Value="60000"; Risk="normal" },
        [pscustomobject]@{ Namespace=$Namespace; Key="font_scale"; Value="1.0"; Risk="normal" },
        [pscustomobject]@{ Namespace=$Namespace; Key="adb_enabled"; Value="1"; Risk="protected" },
        [pscustomobject]@{ Namespace=$Namespace; Key="window_animation_scale"; Value="1"; Risk="advanced" }
    )
}

function Apply-AdvancedFilter {
    $query = (C "AdvancedSearchText").Text.Trim()

    if ([string]::IsNullOrWhiteSpace($query)) {
        $filtered = @($script:AdvancedRows)
    }
    else {
        $filtered = @($script:AdvancedRows | Where-Object {
            $_.Key -match [regex]::Escape($query) -or
            $_.Value -match [regex]::Escape($query)
        })
    }

    # PowerShell 5.1 can unwrap a single object emitted by an if-expression.
    # WPF ItemsSource requires an IEnumerable even when exactly one row matches.
    $rows = New-Object System.Collections.ArrayList
    foreach ($row in @($filtered)) {
        [void]$rows.Add($row)
    }

    (C "AdvancedSettingsGrid").ItemsSource = $rows
}

function Refresh-AdvancedRows {
    $namespace = Get-ComboTag (C "AdvancedNamespaceCombo")
    if ([string]::IsNullOrWhiteSpace($namespace)) { $namespace = "system" }

    if ($TestMode) {
        $script:AdvancedRows = @(Get-FakeAdvancedRows $namespace)
    }
    else {
        $result = Get-AndroidSettingsNamespace $AdbPath $Serial $namespace
        if (-not $result.Ok) {
            $script:AdvancedRows = @()
            Set-Status $result.Error $true
        }
        else {
            $script:AdvancedRows = @($result.Rows)
        }
    }

    Apply-AdvancedFilter
}

(C "AdvancedRefreshButton").Add_Click({ Refresh-AdvancedRows })
(C "AdvancedSearchText").Add_TextChanged({ Apply-AdvancedFilter })
(C "AdvancedNamespaceCombo").Add_DropDownClosed({ Refresh-AdvancedRows })
(C "AdvancedSettingsGrid").Add_SelectionChanged({
    $row = (C "AdvancedSettingsGrid").SelectedItem
    if ($null -ne $row) {
        (C "AdvancedKeyText").Text = [string]$row.Key
        (C "AdvancedValueText").Text = [string]$row.Value
        (C "AdvancedDeleteButton").IsEnabled = ([string]$row.Risk -ne "protected")
    }
})

function Confirm-AdvancedChange([string]$Namespace, [string]$Key, [string]$Risk) {
    if ($TestMode) { return $true }
    if (-not $Config.ControlCenter.ConfirmSensitiveDeviceWrites) { return $true }
    if ($Risk -eq "normal") { return $true }

    $nl = [Environment]::NewLine
    $message = "Change Android setting?" + $nl + $nl + "$Namespace / $Key" + $nl + "Risk: $Risk" + $nl + $nl + "Android/OEM permissions may reject this change."

    $result = [System.Windows.MessageBox]::Show(
        $message,
        "Android Headless Mirror - advanced setting",
        [System.Windows.MessageBoxButton]::YesNo,
        [System.Windows.MessageBoxImage]::Warning
    )
    return ($result -eq [System.Windows.MessageBoxResult]::Yes)
}

if (-not [bool]$Config.ControlCenter.AdvancedSettingsWritesEnabled) {
    (C "AdvancedWriteButton").IsEnabled = $false
    (C "AdvancedDeleteButton").IsEnabled = $false
    (C "AdvancedValueText").IsEnabled = $false
    (C "AdvancedKeyText").IsReadOnly = $true
}

(C "AdvancedWriteButton").Add_Click({
    $namespace = Get-ComboTag (C "AdvancedNamespaceCombo")
    $key = (C "AdvancedKeyText").Text.Trim()
    $value = (C "AdvancedValueText").Text
    $risk = Get-AndroidSettingRisk $namespace $key

    if ($risk -eq "protected") {
        Set-Status "'$key' is protected because changing it could break ADB access or device identity." $true
        return
    }

    if (-not (Confirm-AdvancedChange $namespace $key $risk)) {
        Set-Status "Advanced write cancelled."
        return
    }

    Add-TestAction ("advanced-write:" + $namespace + ":" + $key + "=" + $value)

    if ($TestMode) {
        Set-Status "Test advanced write accepted."
        return
    }

    $result = Set-AndroidSetting $AdbPath $Serial $namespace $key $value
    Set-Status $result.Text (-not $result.Ok)
    if ($result.Ok) { Refresh-AdvancedRows }
})

(C "AdvancedDeleteButton").Add_Click({
    $namespace = Get-ComboTag (C "AdvancedNamespaceCombo")
    $key = (C "AdvancedKeyText").Text.Trim()
    $risk = Get-AndroidSettingRisk $namespace $key

    if ($risk -eq "protected") {
        Set-Status "'$key' is protected from deletion." $true
        return
    }

    if (-not (Confirm-AdvancedChange $namespace $key $risk)) {
        Set-Status "Advanced delete cancelled."
        return
    }

    Add-TestAction ("advanced-delete:" + $namespace + ":" + $key)

    if ($TestMode) {
        Set-Status "Test advanced delete accepted."
        return
    }

    $result = Remove-AndroidSetting $AdbPath $Serial $namespace $key
    Set-Status $result.Text (-not $result.Ok)
    if ($result.Ok) { Refresh-AdvancedRows }
})

(C "ScreenshotButton").Add_Click({
    Add-TestAction "capture:screenshot"

    if ($TestMode) {
        (C "CaptureStatusText").Text = "Test screenshot saved."
        Set-Status "Test screenshot saved."
        return
    }

    $directory = Join-Path $Root ([string]$Config.ControlCenter.ScreenshotDirectory)
    $result = Save-AndroidScreenshot $AdbPath $Serial $directory
    (C "CaptureStatusText").Text = if ($result.Ok) { $result.Path } else { $result.Text }
    Set-Status ($(if ($result.Ok) { "Screenshot saved." } else { $result.Text })) (-not $result.Ok)
})

(C "OpenScreenshotFolderButton").Add_Click({
    $directory = Join-Path $Root ([string]$Config.ControlCenter.ScreenshotDirectory)
    Add-TestAction "capture:open-folder"

    if (-not $TestMode) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
        Start-Process explorer.exe -ArgumentList ('"' + $directory + '"')
    }
})

function Get-DiagnosticsText {
    if ($TestMode) {
        return ("Android Headless Mirror diagnostics" + [Environment]::NewLine +
            "Device: Samsung Galaxy S21 Ultra" + [Environment]::NewLine +
            "Serial: " + $Serial + [Environment]::NewLine +
            "Android: 15 (API 35)" + [Environment]::NewLine +
            "ADB: AUTHORIZED" + [Environment]::NewLine +
            "Transport: USB" + [Environment]::NewLine +
            "Precision Touchpad API: available")
    }

    $state = Invoke-AdbText $AdbPath $Serial @("get-state")
    $services = Get-AndroidCommandServices $AdbPath $Serial

    return ("Android Headless Mirror diagnostics" + [Environment]::NewLine +
        "Device: " + $script:DeviceIdentity.DisplayName + [Environment]::NewLine +
        "Serial: " + $Serial + [Environment]::NewLine +
        "Android: " + $script:DeviceIdentity.AndroidVersion + " (API " + $script:DeviceIdentity.ApiLevel + ")" + [Environment]::NewLine +
        "ADB state: " + $state.Text + [Environment]::NewLine +
        "cmd services: " + $services.Count + [Environment]::NewLine +
        "scrcpy target: " + $script:TargetWindowTitle)
}

function Refresh-All {
    try {
        $script:DeviceIdentity = Get-Identity
        $displayName = if ([string]::IsNullOrWhiteSpace([string]$script:DeviceIdentity.DisplayName)) { "Android Device" } else { [string]$script:DeviceIdentity.DisplayName }

        (C "DeviceNameText").Text = $displayName
        (C "DeviceMetaText").Text = "$($script:DeviceIdentity.Model) - Android $($script:DeviceIdentity.AndroidVersion) - $Serial"
        (C "DeviceSettingsTitle").Text = "$displayName settings"
        (C "ConnectionText").Text = "Connected"
        (C "ConnectionDot").Fill = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#D7FF3F")

        Refresh-DeviceState
        Refresh-AdvancedRows
        (C "DiagnosticsText").Text = Get-DiagnosticsText
        Set-Status "Ready."
    }
    catch {
        (C "ConnectionText").Text = "Unavailable"
        (C "ConnectionDot").Fill = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#FF774D")
        Set-Status $_.Exception.Message $true
    }
}

(C "RefreshAllButton").Add_Click({ Refresh-All })
(C "RefreshDiagnosticsButton").Add_Click({ (C "DiagnosticsText").Text = Get-DiagnosticsText })
(C "OpenLogsButton").Add_Click({
    Add-TestAction "diagnostics:open-logs"
    if (-not $TestMode) {
        $logs = Join-Path $Root "logs"
        New-Item -ItemType Directory -Force -Path $logs | Out-Null
        Start-Process explorer.exe -ArgumentList ('"' + $logs + '"')
    }
})

function Render-ControlCenterSnapshot([string]$Path) {
    $Window.Width = [double]$Config.ControlCenter.Width
    $Window.Height = [double]$Config.ControlCenter.Height
    $Window.WindowStartupLocation = [System.Windows.WindowStartupLocation]::Manual
    $Window.Left = 0
    $Window.Top = 0

    $Window.Show()
    $Window.UpdateLayout()

    $width = [int][Math]::Ceiling($Window.ActualWidth)
    $height = [int][Math]::Ceiling($Window.ActualHeight)

    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap(
        $width,
        $height,
        96,
        96,
        [System.Windows.Media.PixelFormats]::Pbgra32
    )
    $bitmap.Render($Window)

    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create)
    try { $encoder.Save($stream) } finally { $stream.Dispose() }

    return [pscustomobject]@{ Width=$width; Height=$height; Path=$Path }
}

Load-PcSettings
Refresh-All
Restore-ControlCenterTab
Apply-ControlCenterWindowPlacement

(C "MainTabs").Add_SelectionChanged({
    Save-ControlCenterTab
})

if (-not [string]::IsNullOrWhiteSpace($SnapshotPath)) {
    Render-ControlCenterSnapshot $SnapshotPath | Out-Null
    $Window.Close()
    return
}

if ($TestMode) {
    return
}

$Window.ShowDialog() | Out-Null
