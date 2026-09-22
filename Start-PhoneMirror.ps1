[CmdletBinding()]
param(
    [switch]$Foreground
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$ConfigFile = Join-Path $Root "config.json"
$StateFile = Join-Path $Root "state.json"
$StopFile = Join-Path $Root "stop.flag"
$LogDir = Join-Path $Root "logs"
$LogFile = Join-Path $LogDir "mirror.log"
$ScrcpyBase = Join-Path $Root "tools\scrcpy"

if (Test-Path $StopFile) {
    if ($Foreground) {
        Write-Host "Android Headless Mirror is stopped. Run START_NOW.bat to enable it."
    }
    exit 0
}
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

if (-not (Test-Path $ConfigFile)) {
    throw "Missing config.json"
}
$Config = Get-Content $ConfigFile -Raw | ConvertFrom-Json

function Ensure-HeadlessSessionDefaults {
    $changed = $false

    if ($null -eq $Config.PSObject.Properties["TurnPhysicalScreenOff"]) {
        $Config | Add-Member -NotePropertyName TurnPhysicalScreenOff -NotePropertyValue $true
        $changed = $true
    }

    if ($changed) {
        $temp = $ConfigFile + ".tmp"
        $Config | ConvertTo-Json -Depth 20 | Set-Content -Path $temp -Encoding UTF8
        [void](Get-Content $temp -Raw | ConvertFrom-Json)
        Move-Item -Force -Path $temp -Destination $ConfigFile
    }
}

Ensure-HeadlessSessionDefaults

$createdNew = $false
$Mutex = New-Object System.Threading.Mutex($true, "Local\AndroidHeadlessMirrorSupervisor", [ref]$createdNew)
if (-not $createdNew) {
    exit 0
}

function Rotate-Log {
    if (-not $Config.Logging.Enabled) { return }
    if (-not (Test-Path $LogFile)) { return }

    $maxBytes = [int64]$Config.Logging.MaxBytes
    if ((Get-Item $LogFile).Length -lt $maxBytes) { return }

    $keep = [int]$Config.Logging.KeepFiles
    for ($i = $keep - 1; $i -ge 1; $i--) {
        $src = "$LogFile.$i"
        $dst = "$LogFile." + ($i + 1)
        if (Test-Path $src) {
            Move-Item -Force $src $dst
        }
    }
    Move-Item -Force $LogFile "$LogFile.1"
}

function Log([string]$Message, [string]$Level = "INFO") {
    $line = "{0} [{1}] {2}" -f ([DateTime]::Now.ToString("yyyy-MM-dd HH:mm:ss")), $Level, $Message
    if ($Foreground) { Write-Host $line }
    if ($Config.Logging.Enabled) {
        Rotate-Log
        Add-Content -Path $LogFile -Value $line -Encoding UTF8
    }
}


function Ensure-StateShape($State) {
    if ($null -eq $State.PSObject.Properties["PreferredSerial"]) {
        $State | Add-Member -NotePropertyName PreferredSerial -NotePropertyValue ""
    }
    if ($null -eq $State.PSObject.Properties["WirelessHosts"]) {
        $State | Add-Member -NotePropertyName WirelessHosts -NotePropertyValue @()
    }
    if ($null -eq $State.PSObject.Properties["DeviceProfiles"]) {
        $State | Add-Member -NotePropertyName DeviceProfiles -NotePropertyValue @()
    }

    return $State
}

function Get-DeviceProfile($State, [string]$Serial) {
    if ($null -eq $State.PSObject.Properties["DeviceProfiles"]) { return $null }

    return @($State.DeviceProfiles | Where-Object {
        [string]$_.Serial -eq $Serial
    }) | Select-Object -First 1
}

function Set-DeviceLockScreenMode($State, [string]$Serial, [string]$Mode) {
    $profiles = @()
    if ($null -ne $State.PSObject.Properties["DeviceProfiles"]) {
        $profiles = @($State.DeviceProfiles | Where-Object {
            [string]$_.Serial -ne $Serial
        })
    }

    $profiles += [pscustomobject]@{
        Serial = $Serial
        LockScreenMode = $Mode
    }

    $State.DeviceProfiles = @($profiles)
    Save-State $State
}

function Get-DeviceLabel([string]$Adb, [string]$Serial) {
    try {
        $manufacturer = (& $Adb -s $Serial shell getprop ro.product.manufacturer 2>$null | Out-String).Trim()
        $model = (& $Adb -s $Serial shell getprop ro.product.model 2>$null | Out-String).Trim()
        $label = ((@($manufacturer, $model) | Where-Object {
            -not [string]::IsNullOrWhiteSpace([string]$_)
        }) -join " ").Trim()

        if (-not [string]::IsNullOrWhiteSpace($label)) {
            return $label
        }
    }
    catch {}

    return $Serial
}

function Get-OrPromptLockScreenMode([string]$Adb, [string]$Serial, $State) {
    if (-not $Config.PatternOverlay.Enabled) { return "none" }

    $profile = Get-DeviceProfile $State $Serial
    if ($profile -and -not [string]::IsNullOrWhiteSpace([string]$profile.LockScreenMode)) {
        $mode = ([string]$profile.LockScreenMode).ToLowerInvariant()
        if ($mode -in @("pattern", "other", "none")) {
            return $mode
        }
    }

    if (-not $Config.PatternOverlay.PromptPerDevice) {
        return "none"
    }

    $deviceLabel = Get-DeviceLabel $Adb $Serial

    try {
        Add-Type -AssemblyName PresentationFramework -ErrorAction Stop
        $nl = [Environment]::NewLine

        $hasLockMessage =
            "Does this Android device use any screen lock?" + $nl + $nl +
            $deviceLabel + $nl + $Serial + $nl + $nl +
            "Yes  - it uses a pattern, PIN, password, biometric-backed lock, or another lock method." + $nl +
            "No   - it has no screen lock. No lock-screen overlay is needed." + $nl +
            "Cancel - continue this session without saving a choice."

        $hasLock = [System.Windows.MessageBox]::Show(
            $hasLockMessage,
            "Android Headless Mirror - lock screen setup",
            [System.Windows.MessageBoxButton]::YesNoCancel,
            [System.Windows.MessageBoxImage]::Question
        )

        if ($hasLock -eq [System.Windows.MessageBoxResult]::No) {
            Set-DeviceLockScreenMode $State $Serial "none"
            return "none"
        }

        if ($hasLock -eq [System.Windows.MessageBoxResult]::Cancel) {
            return "session-off"
        }

        $patternMessage =
            "Does this device use Android pattern unlock?" + $nl + $nl +
            $deviceLabel + $nl + $Serial + $nl + $nl +
            "Yes  - show the click-through 3x3 pattern guide while the keyguard is visible." + $nl +
            "No   - it uses PIN, password, biometric/other lock. Do not show the pattern guide." + $nl +
            "Cancel - continue this session without saving a choice."

        $isPattern = [System.Windows.MessageBox]::Show(
            $patternMessage,
            "Android Headless Mirror - unlock method",
            [System.Windows.MessageBoxButton]::YesNoCancel,
            [System.Windows.MessageBoxImage]::Question
        )

        if ($isPattern -eq [System.Windows.MessageBoxResult]::Yes) {
            Set-DeviceLockScreenMode $State $Serial "pattern"
            return "pattern"
        }

        if ($isPattern -eq [System.Windows.MessageBoxResult]::No) {
            Set-DeviceLockScreenMode $State $Serial "other"
            return "other"
        }
    }
    catch {
        Log "Could not show per-device lock-screen prompt: $($_.Exception.Message)" "WARN"
    }

    return "session-off"
}

function Find-Tool([string]$Name) {
    if (Test-Path $ScrcpyBase) {
        $bundled = Get-ChildItem -Path $ScrcpyBase -Filter $Name -File -Recurse -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($bundled) { return $bundled.FullName }
    }

    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

function Load-State {
    if (Test-Path $StateFile) {
        try {
            return Ensure-StateShape (Get-Content $StateFile -Raw | ConvertFrom-Json)
        }
        catch {
            Log "Ignoring corrupt state.json: $($_.Exception.Message)" "WARN"
        }
    }

    return Ensure-StateShape ([pscustomobject]@{
        PreferredSerial = ""
        WirelessHosts = @()
        DeviceProfiles = @()
    })
}

function Save-State($State) {
    $State | ConvertTo-Json -Depth 6 | Set-Content -Path $StateFile -Encoding UTF8
}

function Get-AdbDevices([string]$Adb) {
    $output = @(& $Adb devices -l 2>&1)
    $items = @()

    foreach ($line in $output) {
        $text = [string]$line
        if ($text -match '^\s*(\S+)\s+(device|unauthorized|offline|no permissions)(?:\s+|$)') {
            $items += [pscustomobject]@{
                Serial = $Matches[1]
                State = $Matches[2]
                IsTcp = ($Matches[1] -match ':\d+$')
                Raw = $text
            }
        }
    }
    return @($items)
}

function Is-PrivateIPv4([string]$Ip) {
    $parsed = $null
    if (-not [System.Net.IPAddress]::TryParse($Ip, [ref]$parsed)) { return $false }
    $b = $parsed.GetAddressBytes()
    if ($b.Count -ne 4) { return $false }

    if ($b[0] -eq 10) { return $true }
    if ($b[0] -eq 192 -and $b[1] -eq 168) { return $true }
    if ($b[0] -eq 172 -and $b[1] -ge 16 -and $b[1] -le 31) { return $true }
    return $false
}

function Get-PhoneIpCandidates([string]$Adb, [string]$Serial) {
    $result = New-Object System.Collections.Generic.List[string]
    try {
        $lines = @(& $Adb -s $Serial shell ip -o -4 addr show 2>$null)
        foreach ($line in $lines) {
            $s = [string]$line
            if ($s -match '^\d+:\s+([^:\s]+).*?\sinet\s+(\d+\.\d+\.\d+\.\d+)/') {
                $iface = $Matches[1]
                $ip = $Matches[2]

                if (-not (Is-PrivateIPv4 $ip)) { continue }
                if ($ip -eq "127.0.0.1") { continue }

                # Prefer Samsung/Android Wi-Fi/hotspot-style interfaces.
                if ($iface -match '(?i)(wlan|swlan|ap|softap|wifi)') {
                    if (-not $result.Contains($ip)) { $result.Add($ip) }
                }
            }
        }

        # If no Wi-Fi/AP-looking interface was found, keep private addresses as a last resort.
        if ($result.Count -eq 0) {
            foreach ($line in $lines) {
                $s = [string]$line
                if ($s -match '\sinet\s+(\d+\.\d+\.\d+\.\d+)/') {
                    $ip = $Matches[1]
                    if ((Is-PrivateIPv4 $ip) -and $ip -ne "127.0.0.1" -and -not $result.Contains($ip)) {
                        $result.Add($ip)
                    }
                }
            }
        }
    }
    catch {
        Log "Could not discover phone IP addresses: $($_.Exception.Message)" "WARN"
    }
    return @($result)
}

function Get-DefaultGatewayCandidates {
    $result = New-Object System.Collections.Generic.List[string]
    try {
        $configs = Get-NetIPConfiguration -ErrorAction Stop |
            Where-Object { $_.IPv4DefaultGateway -ne $null }

        foreach ($cfg in $configs) {
            $ip = [string]$cfg.IPv4DefaultGateway.NextHop
            if ((Is-PrivateIPv4 $ip) -and -not $result.Contains($ip)) {
                $result.Add($ip)
            }
        }
    }
    catch {
        Log "Could not enumerate Windows default gateways: $($_.Exception.Message)" "WARN"
    }
    return @($result)
}

function Unique-Strings($Values) {
    $set = New-Object System.Collections.Generic.HashSet[string]
    $out = New-Object System.Collections.Generic.List[string]
    foreach ($v in @($Values)) {
        if ($null -eq $v) { continue }
        $s = ([string]$v).Trim()
        if ($s.Length -eq 0) { continue }
        if ($set.Add($s)) { $out.Add($s) }
    }
    return @($out)
}

function Save-WirelessHosts($State, $Hosts) {
    $existing = @()
    if ($null -ne $State.WirelessHosts) { $existing = @($State.WirelessHosts) }
    $State.WirelessHosts = @(Unique-Strings ($existing + @($Hosts)))
    Save-State $State
}

function Try-WirelessConnections([string]$Adb, $State) {
    if (-not $Config.Wireless.Enabled) { return }

    $port = [int]$Config.Wireless.Port
    $hosts = @()

    if ($Config.Wireless.TrySavedAddresses -and $null -ne $State.WirelessHosts) {
        $hosts += @($State.WirelessHosts)
    }

    if ($null -ne $Config.Wireless.ManualHosts) {
        $hosts += @($Config.Wireless.ManualHosts)
    }

    if ($Config.Wireless.TryWindowsDefaultGateway) {
        $hosts += @(Get-DefaultGatewayCandidates)
    }

    $hosts = Unique-Strings $hosts

    foreach ($hostName in $hosts) {
        if (-not (Is-PrivateIPv4 $hostName)) { continue }
        $endpoint = "{0}:{1}" -f $hostName, $port
        try {
            $output = (& $Adb connect $endpoint 2>&1 | Out-String).Trim()
            if ($output -match '(?i)(connected to|already connected to)') {
                Log "ADB TCP/IP connected to $endpoint"
            }
        }
        catch {
            # Expected while the phone is not reachable. Keep polling quietly.
        }
    }
}

function Configure-WirelessFromUsb([string]$Adb, [string]$Serial, $State) {
    if (-not $Config.Wireless.Enabled) { return }
    if (-not $Config.Wireless.EnableTcpipWhenUsbAvailable) { return }

    $port = [int]$Config.Wireless.Port
    try {
        $ips = @(Get-PhoneIpCandidates $Adb $Serial)
        if ($ips.Count -gt 0) {
            Save-WirelessHosts $State $ips
            Log ("Saved wireless candidate(s): " + ($ips -join ", "))
        }

        # Enabling legacy ADB TCP/IP is useful for hotspot/Wi-Fi fallback.
        # Android normally resets this after a phone reboot, so USB remains the true recovery path.
        $result = (& $Adb -s $Serial tcpip $port 2>&1 | Out-String).Trim()
        Log "adb tcpip response: $result"

        Start-Sleep -Milliseconds 900
        foreach ($ip in $ips) {
            try {
                $endpoint = "{0}:{1}" -f $ip, $port
                $connect = (& $Adb connect $endpoint 2>&1 | Out-String).Trim()
                Log "Wireless bootstrap $endpoint -> $connect"
            }
            catch {
                Log "Wireless bootstrap failed for $ip" "WARN"
            }
        }
    }
    catch {
        Log "Wireless bootstrap failed: $($_.Exception.Message)" "WARN"
    }
}

function Select-Device($Devices, $State) {
    $ready = @($Devices | Where-Object { $_.State -eq "device" })
    if ($ready.Count -eq 0) { return $null }

    # An explicitly configured serial is a preference, not a device lock.
    # The learned serial is also only a preference. If it is absent, any
    # other authorised USB Android device may be selected.
    $preferred = [string]$Config.PreferredSerial
    if ([string]::IsNullOrWhiteSpace($preferred)) {
        $preferred = [string]$State.PreferredSerial
    }

    if (-not [string]::IsNullOrWhiteSpace($preferred)) {
        $match = $ready | Where-Object { $_.Serial -eq $preferred } | Select-Object -First 1
        if ($match) { return $match }
    }

    if ($Config.PreferUsb) {
        $usb = @($ready | Where-Object { -not $_.IsTcp })
        if ($usb.Count -gt 0) { return $usb[0] }
    }

    return $ready[0]
}

function Split-ExtraScrcpyArguments([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) { return @() }
    if ($Text -match '[\r\n\x00]') { throw "ExtraScrcpyArgs contains unsupported characters." }

    $tokens = New-Object System.Collections.Generic.List[string]
    $builder = New-Object System.Text.StringBuilder
    $quote = [char]0
    $escapeNext = $false
    $doubleQuote = [char]34
    $singleQuote = [char]39
    $backslash = [char]92

    foreach ($ch in $Text.ToCharArray()) {
        if ($escapeNext) {
            [void]$builder.Append($ch)
            $escapeNext = $false
            continue
        }

        if ($quote -ne [char]0) {
            if ($ch -eq $quote) {
                $quote = [char]0
                continue
            }

            if ($quote -eq $doubleQuote -and $ch -eq $backslash) {
                $escapeNext = $true
                continue
            }

            [void]$builder.Append($ch)
            continue
        }

        if ($ch -eq $doubleQuote -or $ch -eq $singleQuote) {
            $quote = $ch
            continue
        }

        if ([char]::IsWhiteSpace($ch)) {
            if ($builder.Length -gt 0) {
                $tokens.Add($builder.ToString())
                [void]$builder.Clear()
            }
            continue
        }

        [void]$builder.Append($ch)
    }

    if ($escapeNext -or $quote -ne [char]0) {
        throw "ExtraScrcpyArgs contains an unterminated quoted value."
    }

    if ($builder.Length -gt 0) {
        $tokens.Add($builder.ToString())
    }

    $result = @()
    foreach ($value in $tokens) {
        if ([string]::IsNullOrWhiteSpace($value)) { continue }

        if (
            $value -match '^--(serial|window-title|mouse|shortcut-mod)(=|$)' -or
            $value -in @("--no-control","--no-window","--no-video")
        ) {
            throw "ExtraScrcpyArgs cannot override required Android Headless Mirror option '$value'."
        }

        $result += [string]$value
    }

    return @($result)
}

function Build-ScrcpyArguments([string]$Serial, [bool]$IsTcp) {
    $args = New-Object System.Collections.Generic.List[string]
    $args.Add("--serial=$Serial")
    $sessionTitle = "{0} [{1}]" -f ([string]$Config.WindowTitle), $Serial
    $args.Add("--window-title=$sessionTitle")
    # scrcpy's Ctrl+click-and-drag pinch simulation requires SDK mouse mode.
    $args.Add("--mouse=sdk")
    # REX runtime controls intentionally use one deterministic scrcpy MOD.
    # Prevent ExtraScrcpyArgs from changing this or GUI actions can silently stop matching.
    $args.Add("--shortcut-mod=lalt")

    if ($Config.TurnPhysicalScreenOff) {
        $args.Add("--turn-screen-off")
    }

    if ($Config.StayAwakeWhenUsb -and -not $IsTcp) {
        $args.Add("--stay-awake")
    }

    if ($Config.KeepActiveDuringMirror) {
        $args.Add("--keep-active")
    }

    if ($Config.PowerOffOnClose) {
        $args.Add("--power-off-on-close")
    }

    if ([int]$Config.MaxSize -gt 0) {
        $args.Add("--max-size=$([int]$Config.MaxSize)")
    }

    if ([int]$Config.MaxFps -gt 0) {
        $args.Add("--max-fps=$([int]$Config.MaxFps)")
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$Config.VideoBitRate)) {
        $args.Add("--video-bit-rate=$([string]$Config.VideoBitRate)")
    }

    if ($null -ne $Config.PSObject.Properties["ScrcpySession"]) {
        $session = $Config.ScrcpySession

        if (-not [string]::IsNullOrWhiteSpace([string]$session.VideoCodec)) {
            $args.Add("--video-codec=$([string]$session.VideoCodec)")
        }

        if (-not [bool]$session.AudioEnabled) {
            $args.Add("--no-audio")
        }
        else {
            if (-not [string]::IsNullOrWhiteSpace([string]$session.AudioCodec)) {
                $args.Add("--audio-codec=$([string]$session.AudioCodec)")
            }

            if ([bool]$session.AudioDup) {
                $args.Add("--audio-dup")
            }

            if ([int]$session.AudioBufferMs -gt 0) {
                $args.Add("--audio-buffer=$([int]$session.AudioBufferMs)")
            }
        }

        if ([bool]$session.Fullscreen) { $args.Add("--fullscreen") }
        if ([bool]$session.AlwaysOnTop) { $args.Add("--always-on-top") }
        if ([bool]$session.DisableScreensaver) { $args.Add("--disable-screensaver") }

        if ([bool]$session.RecordOnStart) {
            $recordDirectory = Join-Path $Root ([string]$session.RecordDirectory)
            New-Item -ItemType Directory -Force -Path $recordDirectory | Out-Null
            $recordPath = Join-Path $recordDirectory ("android-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".mp4")
            $args.Add("--record=$recordPath")
        }
    }

    if ($null -ne $Config.PSObject.Properties["ExtraScrcpyArgs"]) {
        foreach ($extra in @(Split-ExtraScrcpyArguments ([string]$Config.ExtraScrcpyArgs))) {
            $args.Add($extra)
        }
    }

    return @($args)
}

function Invoke-Scrcpy([string]$Executable, [string[]]$Arguments, [bool]$ShowOutput) {
    # Invoke the native executable with PowerShell's splatted argument array.
    # This preserves argument boundaries (for example, "--window-title=Android Device")
    # instead of flattening them into a single command line as Start-Process
    # -ArgumentList does on Windows PowerShell.
    #
    # scrcpy legitimately writes informational/progress lines to stderr. The
    # supervisor runs with ErrorActionPreference=Stop, so Windows PowerShell can
    # otherwise promote those native stderr records to terminating errors even
    # when scrcpy itself is healthy. Native process success is determined by the
    # process exit code, not by whether stderr received text.
    $previousErrorActionPreference = $ErrorActionPreference
    $hasNativeErrorPreference = Test-Path variable:PSNativeCommandUseErrorActionPreference
    $previousNativeErrorPreference = $null

    if ($hasNativeErrorPreference) {
        $previousNativeErrorPreference = $PSNativeCommandUseErrorActionPreference
    }

    try {
        $ErrorActionPreference = "Continue"
        if ($hasNativeErrorPreference) {
            $PSNativeCommandUseErrorActionPreference = $false
        }

        & $Executable @Arguments 2>&1 | ForEach-Object {
            if ($ShowOutput) {
                Write-Host ([string]$_)
            }
        }

        return [int]$LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
        if ($hasNativeErrorPreference) {
            $PSNativeCommandUseErrorActionPreference = $previousNativeErrorPreference
        }
    }
}


function Start-MirrorChrome([string]$Serial) {
    if ($null -eq $Config.PSObject.Properties["MirrorChrome"]) { return $null }
    if (-not $Config.MirrorChrome.Enabled) { return $null }

    $chromeScript = Join-Path $Root "MirrorChrome.ps1"
    if (-not (Test-Path $chromeScript)) {
        Log "Mirror toolbar requested but MirrorChrome.ps1 is missing." "WARN"
        return $null
    }

    try {
        $quote = [char]34
        $argumentLine =
            "-NoLogo -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " +
            $quote + $chromeScript + $quote +
            " -Serial " + $quote + $Serial + $quote

        return Start-Process -FilePath "powershell.exe" -ArgumentList $argumentLine -PassThru -WindowStyle Hidden
    }
    catch {
        Log "Could not start mirror toolbar: $($_.Exception.Message)" "WARN"
        return $null
    }
}

function Stop-MirrorChrome($Process) {
    if ($null -eq $Process) { return }

    try {
        if (-not $Process.HasExited) {
            Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
        }
    }
    catch {}
}

function Start-PatternOverlay([string]$Serial, [string]$LockScreenMode) {
    if ($LockScreenMode -ne "pattern") { return $null }
    if (-not $Config.PatternOverlay.Enabled) { return $null }

    $overlayScript = Join-Path $Root "PatternOverlay.ps1"
    if (-not (Test-Path $overlayScript)) {
        Log "Pattern overlay requested but PatternOverlay.ps1 is missing." "WARN"
        return $null
    }

    try {
        $quote = [char]34
        $argumentLine =
            "-NoLogo -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " +
            $quote + $overlayScript + $quote +
            " -Serial " + $quote + $Serial + $quote

        return Start-Process -FilePath "powershell.exe" -ArgumentList $argumentLine -PassThru -WindowStyle Hidden
    }
    catch {
        Log "Could not start pattern overlay: $($_.Exception.Message)" "WARN"
        return $null
    }
}

function Stop-PatternOverlay($Process) {
    if ($null -eq $Process) { return }

    try {
        if (-not $Process.HasExited) {
            Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
        }
    }
    catch {}
}

function Prepare-DeviceForMirror([string]$Adb, [string]$Serial) {
    if ($Config.WakeBeforeMirror) {
        try {
            & $Adb -s $Serial shell input keyevent KEYCODE_WAKEUP 2>$null | Out-Null
        }
        catch {
            Log "Wake command failed; continuing." "WARN"
        }
    }

    if ($Config.DismissKeyguardWhenPossible) {
        try {
            # Android only dismisses the keyguard here when authentication is not
            # required (for example, an insecure/trusted keyguard). If credentials
            # are required, Android keeps the security boundary intact.
            & $Adb -s $Serial shell wm dismiss-keyguard 2>$null | Out-Null
        }
        catch {
            Log "Keyguard dismiss request failed; continuing with the lock screen visible." "WARN"
        }
    }
}

function Wait-ForDeviceDisconnect([string]$Adb, [string]$Serial) {
    Log "Mirror closed cleanly; waiting for $Serial to disconnect before auto-opening again."

    while (-not (Test-Path $StopFile)) {
        $devices = @(Get-AdbDevices $Adb)
        $stillConnected = @(
            $devices | Where-Object {
                $_.Serial -eq $Serial -and $_.State -eq "device"
            }
        )

        if ($stillConnected.Count -eq 0) {
            Log "Device $Serial disconnected; armed for automatic launch on next connection."
            return
        }

        Start-Sleep -Seconds ([int]$Config.PollSeconds)
    }
}

function Show-FirstUseHint {
    try {
        Add-Type -AssemblyName PresentationFramework -ErrorAction SilentlyContinue
        [System.Windows.MessageBox]::Show(
            "An Android device is connected, but this computer is not authorised for ADB debugging.`n`n" +
            "Unlock the device and accept 'Allow USB debugging'. Tick 'Always allow from this computer'.`n`n" +
            "After that, Android Headless Mirror can reconnect automatically.",
            "Android Headless Mirror - one-time setup"
        ) | Out-Null
    }
    catch {
        Log "USB debugging authorization is required on the phone." "WARN"
    }
}

$Adb = $null
$Scrcpy = $null
$unauthorizedNoticeShown = $false
$wirelessBootstrappedFor = ""

try {
    $Adb = Find-Tool "adb.exe"
    $Scrcpy = Find-Tool "scrcpy.exe"

    if (-not $Adb -or -not $Scrcpy) {
        Log "scrcpy/adb not found. Run SETUP_AND_START.bat first." "ERROR"
        exit 2
    }

    Log "Supervisor starting. adb=$Adb scrcpy=$Scrcpy"
    & $Adb start-server | Out-Null

    $State = Load-State

    while (-not (Test-Path $StopFile)) {
        try {
            $devices = @(Get-AdbDevices $Adb)

            $selected = Select-Device $devices $State

            if (-not $selected) {
                $unauthorized = @($devices | Where-Object { $_.State -eq "unauthorized" })
                if ($unauthorized.Count -gt 0 -and -not $unauthorizedNoticeShown) {
                    Log "Android device detected, but this computer is not authorised for ADB debugging." "WARN"
                    Show-FirstUseHint
                    $unauthorizedNoticeShown = $true
                }
                elseif ($unauthorized.Count -eq 0) {
                    $unauthorizedNoticeShown = $false
                }

                Try-WirelessConnections $Adb $State
                Start-Sleep -Seconds ([int]$Config.PollSeconds)
                continue
            }

            # At least one usable device exists, so do not interrupt the user just
            # because a second attached phone is still unauthorized.
            $unauthorizedNoticeShown = $false

            if (-not $selected.IsTcp) {
                if ([string]::IsNullOrWhiteSpace([string]$State.PreferredSerial)) {
                    $State.PreferredSerial = $selected.Serial
                    Save-State $State
                    Log "Saved preferred USB serial $($selected.Serial)"
                }

                if ($wirelessBootstrappedFor -ne $selected.Serial) {
                    Configure-WirelessFromUsb $Adb $selected.Serial $State
                    $wirelessBootstrappedFor = $selected.Serial
                }
            }

            $lockScreenMode = Get-OrPromptLockScreenMode $Adb $selected.Serial $State
            Prepare-DeviceForMirror $Adb $selected.Serial

            $args = @(Build-ScrcpyArguments $selected.Serial $selected.IsTcp)
            Log ("Launching scrcpy for {0} ({1}) args={2}" -f $selected.Serial, ($(if ($selected.IsTcp) { "TCP/IP" } else { "USB" })), ($args -join " "))

            $chromeProcess = Start-MirrorChrome $selected.Serial
            $overlayProcess = Start-PatternOverlay $selected.Serial $lockScreenMode
            try {
                $exitCode = Invoke-Scrcpy $Scrcpy $args ([bool]$Foreground)
            }
            finally {
                Stop-PatternOverlay $overlayProcess
                Stop-MirrorChrome $chromeProcess
            }

            Log "scrcpy exited with code $exitCode"

            if (Test-Path $StopFile) { break }

            # If the user intentionally closes the mirror while the phone remains
            # connected, do not immediately reopen it. Keep the hidden supervisor alive
            # and arm automatic launch again after a real disconnect/reconnect cycle.
            if ($exitCode -eq 0) {
                Wait-ForDeviceDisconnect $Adb $selected.Serial
                if (Test-Path $StopFile) { break }
                continue
            }

            if (-not $Config.RestartOnUnexpectedExit) {
                Log "RestartOnUnexpectedExit=false; stopping."
                break
            }

            # A cable pull/device restart is not a scrcpy crash. If the selected
            # transport is gone, return directly to the 1-second connection poll.
            # Only use the longer retry delay when the device is still online and
            # scrcpy itself failed unexpectedly.
            $afterExit = @(Get-AdbDevices $Adb)
            $stillOnline = @(
                $afterExit | Where-Object {
                    $_.Serial -eq $selected.Serial -and $_.State -eq "device"
                }
            )
            if ($stillOnline.Count -gt 0) {
                Start-Sleep -Seconds ([int]$Config.RetrySeconds)
            }
        }
        catch {
            Log "Loop error: $($_.Exception.Message)" "ERROR"
            Start-Sleep -Seconds ([int]$Config.RetrySeconds)
        }
    }

    Log "Supervisor stopped."
}
finally {
    if ($Mutex) {
        try { $Mutex.ReleaseMutex() | Out-Null } catch {}
        $Mutex.Dispose()
    }
}
