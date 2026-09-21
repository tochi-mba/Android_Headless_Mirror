[CmdletBinding()]
param(
    [string]$Serial
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$StateFile = Join-Path $Root "state.json"
$ConfigFile = Join-Path $Root "config.json"

$CalibrationDirectory = Join-Path $Root "pattern-calibration"
if (Test-Path $ConfigFile) {
    try {
        $config = Get-Content $ConfigFile -Raw | ConvertFrom-Json
        if (
            $null -ne $config.PSObject.Properties["PatternOverlay"] -and
            $null -ne $config.PatternOverlay.PSObject.Properties["CalibrationDirectory"] -and
            -not [string]::IsNullOrWhiteSpace([string]$config.PatternOverlay.CalibrationDirectory)
        ) {
            $CalibrationDirectory = Join-Path $Root ([string]$config.PatternOverlay.CalibrationDirectory)
        }
    }
    catch {}
}

function Get-CalibrationPath([string]$SerialValue) {
    $safeSerial = ($SerialValue -replace '[^A-Za-z0-9._-]', '_')
    if ([string]::IsNullOrWhiteSpace($safeSerial)) { $safeSerial = "device" }
    return Join-Path $CalibrationDirectory ($safeSerial + ".json")
}

function Remove-CalibrationForSerial([string]$SerialValue) {
    $path = Get-CalibrationPath $SerialValue
    if (Test-Path $path) {
        Remove-Item -Force $path -ErrorAction SilentlyContinue
    }
}

function Remove-AllCalibrations {
    if (-not (Test-Path $CalibrationDirectory)) { return }

    Get-ChildItem -Path $CalibrationDirectory -Filter "*.json" -File -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path $StateFile)) {
    Write-Host "No saved device lock-screen choices exist." -ForegroundColor Yellow
    exit 0
}

$state = Get-Content $StateFile -Raw | ConvertFrom-Json

if ($null -eq $state.PSObject.Properties["DeviceProfiles"]) {
    Write-Host "No saved device lock-screen choices exist." -ForegroundColor Yellow
    exit 0
}

$profiles = @($state.DeviceProfiles)
if ($profiles.Count -eq 0) {
    Write-Host "No saved device lock-screen choices exist." -ForegroundColor Yellow
    exit 0
}

if ([string]::IsNullOrWhiteSpace($Serial)) {
    Write-Host ""
    Write-Host "Saved device choices:" -ForegroundColor Cyan
    foreach ($profile in $profiles) {
        Write-Host ("  {0}  ->  {1}" -f $profile.Serial, $profile.LockScreenMode)
    }

    Write-Host ""
    $Serial = Read-Host "Enter a device serial to reset, or ALL to clear every saved choice"
}

if ($Serial -eq "ALL") {
    $state.DeviceProfiles = @()
    $state | ConvertTo-Json -Depth 6 | Set-Content -Path $StateFile -Encoding UTF8
    Remove-AllCalibrations
    Write-Host "Cleared all saved lock-screen choices and pattern calibrations. Devices will be asked again next time." -ForegroundColor Green
    exit 0
}

$remaining = @($profiles | Where-Object { [string]$_.Serial -ne $Serial })
if ($remaining.Count -eq $profiles.Count) {
    Write-Host "No saved lock-screen choice was found for serial '$Serial'." -ForegroundColor Yellow
    exit 0
}

$state.DeviceProfiles = @($remaining)
$state | ConvertTo-Json -Depth 6 | Set-Content -Path $StateFile -Encoding UTF8
Remove-CalibrationForSerial $Serial
Write-Host "Cleared the saved lock-screen choice and pattern calibration for '$Serial'. It will be asked again next time." -ForegroundColor Green
