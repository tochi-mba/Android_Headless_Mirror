[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Temp = Join-Path ([System.IO.Path]::GetTempPath()) ("rex-bridge-e2e-" + [guid]::NewGuid().ToString("N"))
$script:Assertions = 0

function Assert-True([bool]$Condition, [string]$Message) {
    $script:Assertions++
    if (-not $Condition) { throw "ASSERTION FAILED: $Message" }
}

function Assert-Equal($Expected, $Actual, [string]$Message) {
    $script:Assertions++
    if ([string]$Expected -ne [string]$Actual) {
        throw "ASSERTION FAILED: $Message Expected=[$Expected] Actual=[$Actual]"
    }
}

function Invoke-Bridge {
    param(
        [Parameter(Mandatory=$true)][string]$Action,
        [string[]]$Arguments = @(),
        [switch]$ExpectFailure
    )

    $bridge = Join-Path $Temp "RexBridge.ps1"
    $all = @("-NoLogo","-NoProfile","-ExecutionPolicy","Bypass","-File",$bridge,"-Action",$Action) + @($Arguments)
    $output = @(& powershell.exe @all 2>&1)
    $exitCode = $LASTEXITCODE
    $jsonLine = @($output | Where-Object { ([string]$_).TrimStart().StartsWith("{") } | Select-Object -Last 1)

    if ($jsonLine.Count -eq 0) {
        throw "Bridge produced no JSON. Output: $($output -join ' | ')"
    }

    if ($ExpectFailure) {
        Assert-True ($exitCode -ne 0) "$Action should fail."
    }
    else {
        Assert-Equal 0 $exitCode "$Action should succeed."
    }

    return (([string]$jsonLine[0]) | ConvertFrom-Json)
}

try {
    New-Item -ItemType Directory -Force -Path $Temp | Out-Null

    foreach ($file in @("RexBridge.ps1","DeviceControl.ps1","ScrcpyControl.ps1","config.json")) {
        Copy-Item (Join-Path $RepoRoot $file) (Join-Path $Temp $file)
    }

    $fakeAdb = Join-Path $Temp "fake-adb.cmd"
    $fakeScrcpy = Join-Path $Temp "fake-scrcpy.exe"
    $adbLog = Join-Path $Temp "adb.log"

    Set-Content -Path $fakeScrcpy -Value "placeholder" -Encoding ASCII
    $env:REX_FAKE_ADB_LOG = $adbLog
    $env:REX_ADB_PATH = $fakeAdb
    $env:REX_SCRCPY_PATH = $fakeScrcpy

    @'
@echo off
echo %*>>"%REX_FAKE_ADB_LOG%"
set args=%*

echo %args% | findstr /C:"devices -l" >nul && (
  echo List of devices attached
  echo USB123 device product:test model:Pixel_9 transport_id:1
  echo LOCKED unauthorized transport_id:2
  exit /b 0
)

echo %args% | findstr /C:"getprop ro.product.manufacturer" >nul && (echo Google& exit /b 0)
echo %args% | findstr /C:"getprop ro.product.model" >nul && (echo Pixel 9& exit /b 0)
echo %args% | findstr /C:"getprop ro.build.version.release" >nul && (echo 15& exit /b 0)
echo %args% | findstr /C:"getprop ro.build.version.sdk" >nul && (echo 35& exit /b 0)

echo %args% | findstr /C:"settings list global" >nul && (
  echo adb_enabled=1
  echo window_animation_scale=1
  exit /b 0
)

echo %args% | findstr /C:"settings get system screen_off_timeout" >nul && (echo 60000& exit /b 0)

echo %args% | findstr /C:"cmd -l" >nul && (
  echo package
  echo connectivity
  echo uimode
  exit /b 0
)

echo OK
exit /b 0
'@ | Set-Content -Path $fakeAdb -Encoding ASCII

    Write-Host "[rex-bridge] status and device discovery"
    $status = Invoke-Bridge "status"
    Assert-True $status.SetupComplete "Status should see explicit fake adb and scrcpy."
    Assert-Equal 2 @($status.Devices).Count "Status should expose both ADB rows."
    Assert-Equal "USB123" $status.Devices[0].Serial "Authorized serial should parse."
    Assert-Equal "Google Pixel 9" $status.Devices[0].DisplayName "Device identity should be resolved."
    Assert-Equal "unauthorized" $status.Devices[1].State "Unauthorized state should be preserved."

    Write-Host "[rex-bridge] friendly Android setting"
    $friendly = Invoke-Bridge "friendly-set" @("-Serial","USB123","-Name","brightness","-Value","200")
    Assert-True $friendly.Ok "Friendly brightness write should succeed."
    $log = Get-Content $adbLog -Raw
    Assert-True ($log -match "settings put system screen_brightness") "Brightness should map to Settings Provider write."
    Assert-True ($log -match "200") "Brightness value should reach fake ADB."

    Write-Host "[rex-bridge] runtime Android namespace enumeration"
    $settings = Invoke-Bridge "settings-list" @("-Serial","USB123","-Namespace","global")
    Assert-Equal 2 @($settings.Rows).Count "Global namespace should parse all rows."
    $protected = @($settings.Rows | Where-Object Key -eq "adb_enabled")[0]
    Assert-Equal "protected" $protected.Risk "adb_enabled must be risk-labelled protected."

    Write-Host "[rex-bridge] protected-key refusal"
    $failure = Invoke-Bridge "settings-set" @("-Serial","USB123","-Namespace","global","-Key","adb_enabled","-Value","0") -ExpectFailure
    Assert-True (-not $failure.Ok) "Protected key write should return Ok=false."
    $failureMessage = if ($null -ne $failure.PSObject.Properties["Error"]) {
        [string]$failure.Error
    }
    elseif ($null -ne $failure.PSObject.Properties["Text"]) {
        [string]$failure.Text
    }
    else {
        ""
    }
    Assert-True ($failureMessage -match "protect") "Protected key failure should explain the guardrail."

    Write-Host "[rex-bridge] lock-mode persistence"
    $lock = Invoke-Bridge "set-lock-mode" @("-Serial","USB123","-Value","pattern")
    Assert-Equal "pattern" $lock.LockScreenMode "Pattern mode should save."
    $state = Get-Content (Join-Path $Temp "state.json") -Raw | ConvertFrom-Json
    Assert-Equal "USB123" $state.DeviceProfiles[0].Serial "State should be keyed by serial."
    Assert-Equal "pattern" $state.DeviceProfiles[0].LockScreenMode "State should persist lock mode."

    Write-Host "[rex-bridge] mirror command sanitization"
    $mirror = Invoke-Bridge "mirror-command" @("-Serial","USB:123","-Name","zoom-in")
    Assert-True $mirror.Ok "Host zoom command should queue."
    $commandPath = Join-Path $Temp "runtime\USB_123\mirror-chrome.command"
    Assert-True (Test-Path $commandPath) "Runtime command path should sanitize serial."
    Assert-Equal "zoom-in" ((Get-Content $commandPath -Raw).Trim()) "Queued command should be exact."

    Write-Host "[rex-bridge] command service discovery"
    $services = Invoke-Bridge "cmd-services" @("-Serial","USB123")
    Assert-True (@($services.Services) -contains "package") "cmd service list should include package."
    Assert-True (@($services.Services) -contains "connectivity") "cmd service list should include connectivity."

    Write-Host ""
    Write-Host ("REX bridge E2E passed: " + $script:Assertions + " assertions.") -ForegroundColor Green
}
finally {
    $env:REX_ADB_PATH = $null
    $env:REX_SCRCPY_PATH = $null
    $env:REX_FAKE_ADB_LOG = $null

    if (Test-Path $Temp) {
        Remove-Item -Recurse -Force $Temp -ErrorAction SilentlyContinue
    }
}
