[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Temp = Join-Path ([System.IO.Path]::GetTempPath()) ("rex-shortcut-tests-" + [guid]::NewGuid().ToString("N"))

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "ASSERTION FAILED: $Message" }
}

function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ([string]$Expected -ne [string]$Actual) {
        throw "ASSERTION FAILED: $Message Expected=[$Expected] Actual=[$Actual]"
    }
}

try {
    New-Item -ItemType Directory -Force -Path $Temp | Out-Null
    $desktop = Join-Path $Temp "Desktop"
    New-Item -ItemType Directory -Force -Path $desktop | Out-Null

    foreach ($file in @("Install-Rex-Shortcut.ps1","Remove-Rex-Shortcut.ps1")) {
        Copy-Item (Join-Path $RepoRoot $file) (Join-Path $Temp $file)
    }
    Set-Content -Path (Join-Path $Temp "REX.bat") -Value "@echo off" -Encoding ASCII

    & (Join-Path $Temp "Install-Rex-Shortcut.ps1") -DestinationDirectory $desktop

    $shortcutPath = Join-Path $desktop "REX.lnk"
    Assert-True (Test-Path $shortcutPath) "Installer should create REX.lnk."

    $wsh = New-Object -ComObject WScript.Shell
    $shortcut = $wsh.CreateShortcut($shortcutPath)

    Assert-Equal $env:ComSpec $shortcut.TargetPath "Shortcut should launch through the Windows command processor."
    Assert-True ($shortcut.Arguments -match 'REX\.bat') "Shortcut arguments should reference REX.bat."
    Assert-True ($shortcut.Arguments -match '\bsmart\b') "Shortcut must invoke the smart launch command."
    Assert-Equal $Temp $shortcut.WorkingDirectory "Shortcut should use the package root as working directory."
    Assert-True ($shortcut.Description -match "REX Technologies") "Shortcut should carry REX branding."

    & (Join-Path $Temp "Install-Rex-Shortcut.ps1") -DestinationDirectory $desktop
    Assert-True (Test-Path $shortcutPath) "Reinstall should be idempotent."

    & (Join-Path $Temp "Remove-Rex-Shortcut.ps1") -DestinationDirectory $desktop
    Assert-True (-not (Test-Path $shortcutPath)) "Remove should delete REX.lnk."

    & (Join-Path $Temp "Remove-Rex-Shortcut.ps1") -DestinationDirectory $desktop
    Assert-True (-not (Test-Path $shortcutPath)) "Repeated remove should remain idempotent."

    Write-Host "REX desktop shortcut behavior passed." -ForegroundColor Green
}
finally {
    if (Test-Path $Temp) {
        Remove-Item -Recurse -Force $Temp -ErrorAction SilentlyContinue
    }
}
