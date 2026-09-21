[CmdletBinding()]
param(
    [string]$Serial
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$StateFile = Join-Path $Root "state.json"

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
    Write-Host "Cleared all saved lock-screen choices. Devices will be asked again next time." -ForegroundColor Green
    exit 0
}

$remaining = @($profiles | Where-Object { [string]$_.Serial -ne $Serial })
if ($remaining.Count -eq $profiles.Count) {
    Write-Host "No saved lock-screen choice was found for serial '$Serial'." -ForegroundColor Yellow
    exit 0
}

$state.DeviceProfiles = @($remaining)
$state | ConvertTo-Json -Depth 6 | Set-Content -Path $StateFile -Encoding UTF8
Write-Host "Cleared the saved lock-screen choice for '$Serial'. It will be asked again next time." -ForegroundColor Green
