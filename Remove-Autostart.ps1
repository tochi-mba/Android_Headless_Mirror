[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Startup = [Environment]::GetFolderPath("Startup")
$ShortcutNames = @(
    "Android Headless Mirror.lnk",
    "S21 Headless Mirror.lnk"
)

$removed = $false
foreach ($name in $ShortcutNames) {
    $path = Join-Path $Startup $name
    if (Test-Path $path) {
        Remove-Item -Force $path
        Write-Host "Removed: $path"
        $removed = $true
    }
}

if (-not $removed) {
    Write-Host "No Android Headless Mirror startup shortcut was installed."
}
