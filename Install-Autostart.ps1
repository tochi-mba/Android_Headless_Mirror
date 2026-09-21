[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Startup = [Environment]::GetFolderPath("Startup")
$ShortcutPath = Join-Path $Startup "S21 Headless Mirror.lnk"
$VbsPath = Join-Path $Root "Start-Hidden.vbs"

if (-not (Test-Path $VbsPath)) {
    throw "Start-Hidden.vbs not found: $VbsPath"
}

$Shell = New-Object -ComObject WScript.Shell
$Shortcut = $Shell.CreateShortcut($ShortcutPath)
$Shortcut.TargetPath = "$env:WINDIR\System32\wscript.exe"
$Shortcut.Arguments = """" + $VbsPath + """"
$Shortcut.WorkingDirectory = $Root
$Shortcut.Description = "Automatically start the Galaxy S21 Ultra mirror supervisor"
$Shortcut.Save()

Write-Host "Installed startup shortcut:"
Write-Host "  $ShortcutPath"
