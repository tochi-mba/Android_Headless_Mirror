[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "SilentlyContinue"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$StopFile = Join-Path $Root "stop.flag"

Set-Content -Path $StopFile -Value ([DateTime]::UtcNow.ToString("o")) -Encoding ASCII

Get-Process -Name "scrcpy" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Host "Stop requested. The background supervisor will exit."
