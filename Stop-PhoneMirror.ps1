[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "SilentlyContinue"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$StopFile = Join-Path $Root "stop.flag"

Write-Host ""
Write-Host "Stopping Android Headless Mirror..." -ForegroundColor Cyan

# Persist the OFF state. Windows autostart respects this flag until an explicit START clears it.
Set-Content -Path $StopFile -Value ([DateTime]::UtcNow.ToString("o")) -Encoding ASCII -Force

# Stop only scrcpy instances launched from this package.
$escapedRoot = [regex]::Escape($Root)
Get-CimInstance Win32_Process -Filter "Name='scrcpy.exe'" -ErrorAction SilentlyContinue |
    Where-Object {
        $_.ExecutablePath -and $_.ExecutablePath -match $escapedRoot
    } |
    ForEach-Object {
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }

# Stop only this package's pattern-overlay sidecars.
Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -in @("powershell.exe", "pwsh.exe") -and
        $_.CommandLine -and
        $_.CommandLine -match "PatternOverlay\.ps1" -and
        $_.CommandLine -match $escapedRoot
    } |
    ForEach-Object {
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }

# Stop only this package's mirror-toolbar / host-zoom sidecars.
Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -in @("powershell.exe", "pwsh.exe") -and
        $_.CommandLine -and
        $_.CommandLine -match "MirrorChrome\.ps1" -and
        $_.CommandLine -match $escapedRoot
    } |
    ForEach-Object {
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }

# Stop only this package's control-center sidecars.
Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -in @("powershell.exe", "pwsh.exe") -and
        $_.CommandLine -and
        $_.CommandLine -match "ControlCenter\.ps1" -and
        $_.CommandLine -match $escapedRoot
    } |
    ForEach-Object {
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }

# Stop only this package's supervisor process. Do not stop the shared/global ADB server.
Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -in @("powershell.exe", "pwsh.exe") -and
        $_.CommandLine -and
        $_.CommandLine -match "Start-PhoneMirror\.ps1" -and
        $_.CommandLine -match $escapedRoot
    } |
    ForEach-Object {
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }

# A hidden launcher normally exits immediately, but stop a matching one if it is still alive.
Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -eq "wscript.exe" -and
        $_.CommandLine -and
        $_.CommandLine -match "Start-Hidden\.vbs" -and
        $_.CommandLine -match $escapedRoot
    } |
    ForEach-Object {
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }

Write-Host ""
Write-Host "Android Headless Mirror is OFF." -ForegroundColor Green
Write-Host "Run START_NOW.bat when you want to enable it again." -ForegroundColor DarkGray
Write-Host ""
