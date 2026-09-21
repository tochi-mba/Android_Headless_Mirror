[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Startup = [Environment]::GetFolderPath("Startup")
$ShortcutPath = Join-Path $Startup "S21 Headless Mirror.lnk"

if (Test-Path $ShortcutPath) {
    Remove-Item -Force $ShortcutPath
    Write-Host "Removed: $ShortcutPath"
}
else {
    Write-Host "No startup shortcut was installed."
}
