[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(
        "status",
        "devices",
        "identity",
        "scrcpy-action",
        "mirror-command",
        "friendly-set",
        "settings-list",
        "settings-get",
        "settings-set",
        "settings-delete",
        "set-lock-mode",
        "screenshot",
        "cmd-services"
    )]
    [string]$Action,

    [string]$Serial = "",
    [string]$Name = "",
    [string]$Namespace = "",
    [string]$Key = "",
    [AllowEmptyString()][string]$Value = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$ConfigPath = Join-Path $Root "config.json"
$StatePath = Join-Path $Root "state.json"
$StopPath = Join-Path $Root "stop.flag"

. (Join-Path $Root "DeviceControl.ps1")
. (Join-Path $Root "ScrcpyControl.ps1")

function Find-RexTool([string]$ToolName) {
    $base = Join-Path $Root "tools\scrcpy"

    if (Test-Path $base) {
        $tool = Get-ChildItem -Path $base -Filter $ToolName -File -Recurse -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($tool) { return $tool.FullName }
    }

    $command = Get-Command $ToolName -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    return $null
}

function Get-AdbRows([string]$Adb) {
    if ([string]::IsNullOrWhiteSpace($Adb)) { return @() }

    & $Adb start-server 2>$null | Out-Null
    $rows = @()

    foreach ($line in @(& $Adb devices -l 2>&1)) {
        $text = [string]$line
        if ($text -notmatch '^\s*(\S+)\s+(device|unauthorized|offline|no permissions)(?:\s+|$)') {
            continue
        }

        $serialValue = $Matches[1]
        $stateValue = $Matches[2]
        $manufacturer = ""
        $model = ""

        if ($stateValue -eq "device") {
            try {
                $manufacturer = (& $Adb -s $serialValue shell getprop ro.product.manufacturer 2>$null | Out-String).Trim()
                $model = (& $Adb -s $serialValue shell getprop ro.product.model 2>$null | Out-String).Trim()
            }
            catch {}
        }

        $rows += [pscustomobject]@{
            Serial = $serialValue
            State = $stateValue
            IsTcp = ($serialValue -match ':\d+$')
            Manufacturer = $manufacturer
            Model = $model
            DisplayName = ((@($manufacturer, $model) | Where-Object {
                -not [string]::IsNullOrWhiteSpace([string]$_)
            }) -join " ").Trim()
        }
    }

    return @($rows)
}

function Get-PackageProcess([string]$ProcessName, [string]$Needle) {
    $escapedRoot = [regex]::Escape($Root)

    return Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -eq $ProcessName -and
            $_.CommandLine -and
            $_.CommandLine -match [regex]::Escape($Needle) -and
            $_.CommandLine -match $escapedRoot
        } |
        Select-Object -First 1
}

function Get-AutostartState {
    $startup = [Environment]::GetFolderPath("Startup")
    return (Test-Path (Join-Path $startup "Android Headless Mirror.lnk"))
}

function Get-OrCreateState {
    if (Test-Path $StatePath) {
        try {
            $state = Get-Content $StatePath -Raw | ConvertFrom-Json
        }
        catch {
            $state = [pscustomobject]@{}
        }
    }
    else {
        $state = [pscustomobject]@{}
    }

    if ($null -eq $state.PSObject.Properties["PreferredSerial"]) {
        $state | Add-Member -NotePropertyName PreferredSerial -NotePropertyValue ""
    }
    if ($null -eq $state.PSObject.Properties["WirelessHosts"]) {
        $state | Add-Member -NotePropertyName WirelessHosts -NotePropertyValue @()
    }
    if ($null -eq $state.PSObject.Properties["DeviceProfiles"]) {
        $state | Add-Member -NotePropertyName DeviceProfiles -NotePropertyValue @()
    }

    return $state
}

function Set-LockMode([string]$TargetSerial, [string]$Mode) {
    if ([string]::IsNullOrWhiteSpace($TargetSerial)) {
        throw "Serial is required."
    }

    if ($Mode -notin @("pattern", "other", "none")) {
        throw "Lock-screen mode must be pattern, other, or none."
    }

    $state = Get-OrCreateState
    $profiles = @($state.DeviceProfiles | Where-Object {
        [string]$_.Serial -ne $TargetSerial
    })

    $profiles += [pscustomobject]@{
        Serial = $TargetSerial
        LockScreenMode = $Mode
    }

    $state.DeviceProfiles = @($profiles)
    $state | ConvertTo-Json -Depth 8 | Set-Content -Path $StatePath -Encoding UTF8

    return [pscustomobject]@{
        Ok = $true
        Serial = $TargetSerial
        LockScreenMode = $Mode
    }
}

function Require-Adb {
    $adb = Find-RexTool "adb.exe"
    if ([string]::IsNullOrWhiteSpace($adb)) {
        throw "adb.exe was not found. Run setup first."
    }
    return $adb
}

function Require-Serial {
    if ([string]::IsNullOrWhiteSpace($Serial)) {
        throw "A device serial is required for this action."
    }
}

try {
    $result = $null

    switch ($Action) {
        "status" {
            $adb = Find-RexTool "adb.exe"
            $scrcpy = Find-RexTool "scrcpy.exe"
            $supervisor = Get-PackageProcess "powershell.exe" "Start-PhoneMirror.ps1"
            if (-not $supervisor) {
                $supervisor = Get-PackageProcess "pwsh.exe" "Start-PhoneMirror.ps1"
            }

            $mirror = Get-Process -Name "scrcpy" -ErrorAction SilentlyContinue | Select-Object -First 1
            $devices = if ($adb) { @(Get-AdbRows $adb) } else { @() }

            $result = [pscustomobject]@{
                Ok = $true
                Root = $Root
                ConfigPresent = (Test-Path $ConfigPath)
                SetupComplete = (-not [string]::IsNullOrWhiteSpace($adb) -and -not [string]::IsNullOrWhiteSpace($scrcpy))
                AdbPath = [string]$adb
                ScrcpyPath = [string]$scrcpy
                AutostartEnabled = (Get-AutostartState)
                PersistentOff = (Test-Path $StopPath)
                SupervisorRunning = ($null -ne $supervisor)
                MirrorRunning = ($null -ne $mirror)
                Devices = @($devices)
            }
        }

        "devices" {
            $adb = Require-Adb
            $result = [pscustomobject]@{
                Ok = $true
                Devices = @(Get-AdbRows $adb)
            }
        }

        "identity" {
            Require-Serial
            $adb = Require-Adb
            $identity = Get-AndroidDeviceIdentity $adb $Serial
            $result = [pscustomobject]@{ Ok = $true; Device = $identity }
        }

        "scrcpy-action" {
            Require-Serial
            if ([string]::IsNullOrWhiteSpace($Name)) { throw "Name is required." }
            $config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
            $title = "{0} [{1}]" -f ([string]$config.WindowTitle), $Serial
            $result = Invoke-ScrcpyNamedShortcut -WindowTitle $title -Name $Name
        }

        "mirror-command" {
            Require-Serial
            if ($Name -notin @("zoom-in", "zoom-out", "reset-zoom", "open-controls")) {
                throw "Unsupported mirror command '$Name'."
            }

            $safeSerial = ($Serial -replace '[^A-Za-z0-9._-]', '_')
            $runtime = Join-Path (Join-Path $Root "runtime") $safeSerial
            New-Item -ItemType Directory -Force -Path $runtime | Out-Null
            Set-Content -Path (Join-Path $runtime "mirror-chrome.command") -Value $Name -Encoding ASCII

            $result = [pscustomobject]@{
                Ok = $true
                Text = "Mirror command queued: $Name"
            }
        }

        "friendly-set" {
            Require-Serial
            if ([string]::IsNullOrWhiteSpace($Name)) { throw "Name is required." }
            $adb = Require-Adb
            $result = Set-FriendlyAndroidSetting $adb $Serial $Name $Value
        }

        "settings-list" {
            Require-Serial
            $adb = Require-Adb
            $result = Get-AndroidSettingsNamespace $adb $Serial $Namespace
        }

        "settings-get" {
            Require-Serial
            $adb = Require-Adb
            $result = Get-AndroidSetting $adb $Serial $Namespace $Key
        }

        "settings-set" {
            Require-Serial
            $adb = Require-Adb
            $result = Set-AndroidSetting $adb $Serial $Namespace $Key $Value
        }

        "settings-delete" {
            Require-Serial
            $adb = Require-Adb
            $result = Remove-AndroidSetting $adb $Serial $Namespace $Key
        }

        "set-lock-mode" {
            $result = Set-LockMode $Serial $Value
        }

        "screenshot" {
            Require-Serial
            $adb = Require-Adb
            $config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
            $directory = Join-Path $Root ([string]$config.ControlCenter.ScreenshotDirectory)
            $result = Save-AndroidScreenshot $adb $Serial $directory
        }

        "cmd-services" {
            Require-Serial
            $adb = Require-Adb
            $result = [pscustomobject]@{
                Ok = $true
                Services = @(Get-AndroidCommandServices $adb $Serial)
            }
        }
    }

    $result | ConvertTo-Json -Depth 12 -Compress
}
catch {
    [pscustomobject]@{
        Ok = $false
        Error = $_.Exception.Message
    } | ConvertTo-Json -Depth 6 -Compress
    exit 1
}
