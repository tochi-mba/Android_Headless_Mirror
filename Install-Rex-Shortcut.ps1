[CmdletBinding()]
param(
    [string]$DestinationDirectory = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$RexBat = Join-Path $Root "REX.bat"
$RexExe = Join-Path $Root "tools\rex\rex.exe"

if ([string]::IsNullOrWhiteSpace($DestinationDirectory)) {
    $DestinationDirectory = [Environment]::GetFolderPath("Desktop")
}

if ([string]::IsNullOrWhiteSpace($DestinationDirectory)) {
    throw "Could not resolve the current user's Desktop folder."
}

if (-not (Test-Path $RexBat)) {
    throw "REX.bat was not found: $RexBat"
}

New-Item -ItemType Directory -Force -Path $DestinationDirectory | Out-Null

$shortcutPath = Join-Path $DestinationDirectory "REX.lnk"
$wsh = New-Object -ComObject WScript.Shell
$shortcut = $wsh.CreateShortcut($shortcutPath)

$shortcut.TargetPath = $env:ComSpec
$shortcut.Arguments = '/d /c ""' + $RexBat + '" smart"'
$shortcut.WorkingDirectory = $Root
$shortcut.Description = "REX Technologies - Android Headless Mirror"
$shortcut.WindowStyle = 1

if (Test-Path $RexExe) {
    $shortcut.IconLocation = $RexExe + ",0"
}
else {
    $shortcut.IconLocation = $env:ComSpec + ",0"
}

$shortcut.Save()

if (-not (Test-Path $shortcutPath)) {
    throw "Windows did not create the REX desktop shortcut."
}

Write-Host "REX desktop shortcut installed: $shortcutPath"
