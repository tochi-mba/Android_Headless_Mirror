[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Continue"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$ScrcpyBase = Join-Path $Root "tools\scrcpy"
$StateFile = Join-Path $Root "state.json"
$StopFile = Join-Path $Root "stop.flag"
$LogFile = Join-Path $Root "logs\mirror.log"

function Find-Tool([string]$Name) {
    $bundled = Get-ChildItem -Path $ScrcpyBase -Filter $Name -File -Recurse -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($bundled) { return $bundled.FullName }

    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    return $null
}

function Get-AdbDeviceRows([string]$Adb) {
    $rows = @()
    foreach ($line in @(& $Adb devices -l 2>&1)) {
        $text = [string]$line
        if ($text -match '^\s*(\S+)\s+(device|unauthorized|offline|no permissions)(?:\s+|$)') {
            $rows += [pscustomobject]@{
                Serial = $Matches[1]
                State = $Matches[2]
                Raw = $text
            }
        }
    }
    return @($rows)
}

function Get-PackageProcess([string]$ProcessName, [string]$CommandNeedle) {
    $escapedRoot = [regex]::Escape($Root)
    return Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -eq $ProcessName -and
            $_.CommandLine -and
            $_.CommandLine -match [regex]::Escape($CommandNeedle) -and
            $_.CommandLine -match $escapedRoot
        } |
        Select-Object -First 1
}

$scrcpy = Find-Tool "scrcpy.exe"
$adb = Find-Tool "adb.exe"

Write-Host ""
Write-Host "=== Android Headless Mirror Diagnostics ===" -ForegroundColor Cyan
Write-Host ("Root:          {0}" -f $Root)
Write-Host ("Persistent OFF:{0}" -f $(if (Test-Path $StopFile) { " yes" } else { " no" }))
Write-Host ("ADB:           {0}" -f $(if ($adb) { "OK" } else { "NOT FOUND" }))
Write-Host ("scrcpy:        {0}" -f $(if ($scrcpy) { "OK" } else { "NOT FOUND" }))

if ($adb) {
    $adbVersion = (& $adb version 2>&1 | Select-Object -First 1)
    Write-Host ("ADB version:   {0}" -f $adbVersion)
}

if ($scrcpy) {
    $scrcpyVersion = (& $scrcpy --version 2>&1 | Select-Object -First 1)
    Write-Host ("scrcpy version:{0}" -f $scrcpyVersion)
}

$supervisor = Get-PackageProcess "powershell.exe" "Start-PhoneMirror.ps1"
if (-not $supervisor) {
    $supervisor = Get-PackageProcess "pwsh.exe" "Start-PhoneMirror.ps1"
}
$mirrorProc = Get-Process -Name "scrcpy" -ErrorAction SilentlyContinue | Select-Object -First 1

Write-Host ("Supervisor:    {0}" -f $(if ($supervisor) { "RUNNING (PID $($supervisor.ProcessId))" } else { "stopped" }))
Write-Host ("Mirror:        {0}" -f $(if ($mirrorProc) { "RUNNING (PID $($mirrorProc.Id))" } else { "stopped" }))

if ($adb) {
    & $adb start-server | Out-Null
    $devices = @(Get-AdbDeviceRows $adb)

    if ($devices.Count -eq 0) {
        Write-Host "Device:        NOT DETECTED" -ForegroundColor Yellow
        Write-Host "Hint: check the USB cable/port, Android USB mode, and Windows ADB driver."
    }
    else {
        foreach ($device in $devices) {
            Write-Host ""
            Write-Host ("Serial:        {0}" -f $device.Serial)
            Write-Host ("ADB state:     {0}" -f $device.State)

            switch ($device.State) {
                "device" {
                    $manufacturer = (& $adb -s $device.Serial shell getprop ro.product.manufacturer 2>$null | Out-String).Trim()
                    $model = (& $adb -s $device.Serial shell getprop ro.product.model 2>$null | Out-String).Trim()
                    if ([string]::IsNullOrWhiteSpace($manufacturer)) { $manufacturer = "Android" }
                    if ([string]::IsNullOrWhiteSpace($model)) { $model = "device" }
                    Write-Host ("Device:        {0} {1}" -f $manufacturer, $model) -ForegroundColor Green
                    Write-Host "ADB auth:      AUTHORIZED" -ForegroundColor Green
                }
                "unauthorized" {
                    Write-Host "ADB auth:      UNAUTHORIZED" -ForegroundColor Yellow
                    Write-Host "Action: unlock the Android device, approve 'Allow USB debugging'," -ForegroundColor Yellow
                    Write-Host "        and select 'Always allow from this computer'." -ForegroundColor Yellow
                }
                "offline" {
                    Write-Host "ADB auth:      OFFLINE" -ForegroundColor Yellow
                    Write-Host "Action: reconnect USB, then restart ADB or reboot the device if needed." -ForegroundColor Yellow
                }
                default {
                    Write-Host ("ADB auth:      {0}" -f $device.State) -ForegroundColor Yellow
                }
            }
        }
    }
}

if (Test-Path $StateFile) {
    Write-Host ""
    Write-Host "--- Saved state ---"
    Get-Content $StateFile
}

if (Test-Path $LogFile) {
    Write-Host ""
    Write-Host "--- Last 40 log lines ---"
    Get-Content $LogFile -Tail 40
}

Write-Host ""
