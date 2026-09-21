$ErrorActionPreference = "SilentlyContinue"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$StopFlag = Join-Path $Root "stop.flag"

Write-Host ""
Write-Host "Stopping S21 Headless Mirror..." -ForegroundColor Cyan

# Tell supervisor not to relaunch scrcpy
Set-Content -Path $StopFlag -Value "stop" -Force

# Stop scrcpy
Get-Process scrcpy -ErrorAction SilentlyContinue |
    Stop-Process -Force

# Stop the background PowerShell supervisor
Get-CimInstance Win32_Process |
    Where-Object {
        $_.Name -in @("powershell.exe", "pwsh.exe") -and
        $_.CommandLine -like "*Start-PhoneMirror.ps1*"
    } |
    ForEach-Object {
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }

# Stop any hidden VBS launcher still alive
Get-CimInstance Win32_Process |
    Where-Object {
        $_.Name -eq "wscript.exe" -and
        $_.CommandLine -like "*Start-Hidden.vbs*"
    } |
    ForEach-Object {
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }

# Stop bundled ADB
$Adb = Get-ChildItem "$Root\tools" -Filter adb.exe -Recurse -File |
    Select-Object -First 1

if ($Adb) {
    & $Adb.FullName kill-server | Out-Null
}

Start-Sleep -Seconds 1

Write-Host ""
Write-Host "S21 mirror completely stopped." -ForegroundColor Green
Write-Host ""
