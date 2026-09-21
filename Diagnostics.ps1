[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Continue"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$ScrcpyBase = Join-Path $Root "tools\scrcpy"
$StateFile = Join-Path $Root "state.json"
$LogFile = Join-Path $Root "logs\mirror.log"

function Find-Tool([string]$Name) {
    $bundled = Get-ChildItem -Path $ScrcpyBase -Filter $Name -File -Recurse -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($bundled) { return $bundled.FullName }
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

$scrcpy = Find-Tool "scrcpy.exe"
$adb = Find-Tool "adb.exe"

Write-Host "=== S21 Headless Mirror diagnostics ==="
Write-Host "Root:    $Root"
Write-Host "scrcpy:  $scrcpy"
Write-Host "adb:     $adb"
Write-Host ""

if ($scrcpy) {
    Write-Host "--- scrcpy version ---"
    & $scrcpy --version
}
if ($adb) {
    Write-Host ""
    Write-Host "--- adb devices -l ---"
    & $adb start-server | Out-Null
    & $adb devices -l
}
if (Test-Path $StateFile) {
    Write-Host ""
    Write-Host "--- saved state ---"
    Get-Content $StateFile
}
if (Test-Path $LogFile) {
    Write-Host ""
    Write-Host "--- last 60 log lines ---"
    Get-Content $LogFile -Tail 60
}
