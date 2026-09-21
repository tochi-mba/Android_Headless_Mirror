[CmdletBinding()]
param(
    [string]$DestinationDirectory = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($DestinationDirectory)) {
    $DestinationDirectory = [Environment]::GetFolderPath("Desktop")
}

if ([string]::IsNullOrWhiteSpace($DestinationDirectory)) {
    throw "Could not resolve the current user's Desktop folder."
}

$shortcutPath = Join-Path $DestinationDirectory "REX.lnk"
if (Test-Path $shortcutPath) {
    Remove-Item -Force $shortcutPath
    Write-Host "Removed REX desktop shortcut: $shortcutPath"
}
else {
    Write-Host "REX desktop shortcut is already absent."
}
