[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$SupervisorPath = Join-Path $Root "Start-PhoneMirror.ps1"
$StopPath = Join-Path $Root "Stop-PhoneMirror.ps1"

$script:Assertions = 0

function Assert-True {
    param(
        [Parameter(Mandatory)][bool]$Condition,
        [Parameter(Mandatory)][string]$Message
    )

    $script:Assertions++
    if (-not $Condition) {
        throw "Assertion failed: $Message"
    }
}

function Assert-False {
    param(
        [Parameter(Mandatory)][bool]$Condition,
        [Parameter(Mandatory)][string]$Message
    )

    Assert-True -Condition (-not $Condition) -Message $Message
}

function Assert-Equal {
    param(
        $Expected,
        $Actual,
        [Parameter(Mandatory)][string]$Message
    )

    $script:Assertions++
    if ($Expected -is [System.Array] -or $Actual -is [System.Array]) {
        $expectedJson = @($Expected) | ConvertTo-Json -Compress
        $actualJson = @($Actual) | ConvertTo-Json -Compress
        if ($expectedJson -ne $actualJson) {
            throw ("Assertion failed: " + $Message + [Environment]::NewLine + "Expected: " + $expectedJson + [Environment]::NewLine + "Actual:   " + $actualJson)
        }
        return
    }

    if ($Expected -ne $Actual) {
        throw ("Assertion failed: " + $Message + [Environment]::NewLine + "Expected: " + $Expected + [Environment]::NewLine + "Actual:   " + $Actual)
    }
}

function Assert-Contains {
    param(
        [Parameter(Mandatory)][object[]]$Collection,
        [Parameter(Mandatory)]$Value,
        [Parameter(Mandatory)][string]$Message
    )

    Assert-True -Condition ($Collection -contains $Value) -Message $Message
}

function Get-SupervisorFunctionDefinitions {
    param([string[]]$Names)

    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile(
        $SupervisorPath,
        [ref]$tokens,
        [ref]$errors
    )

    Assert-Equal -Expected 0 -Actual $errors.Count -Message "Supervisor must parse before behavior tests run."

    $definitions = @{}
    $ast.FindAll(
        {
            param($Node)
            $Node -is [System.Management.Automation.Language.FunctionDefinitionAst]
        },
        $true
    ) | ForEach-Object {
        $definitions[$_.Name] = $_.Extent.Text
    }

    $selectedDefinitions = @()
    foreach ($name in $Names) {
        Assert-True -Condition $definitions.ContainsKey($name) -Message "Supervisor function '$name' must exist."
        $selectedDefinitions += $definitions[$name]
    }

    return $selectedDefinitions
}

$functionDefinitions = @(Get-SupervisorFunctionDefinitions -Names @(
    "Get-AdbDevices",
    "Is-PrivateIPv4",
    "Get-PhoneIpCandidates",
    "Unique-Strings",
    "Select-Device",
    "Build-ScrcpyArguments",
    "Invoke-Scrcpy",
    "Prepare-DeviceForMirror"
))

foreach ($definition in $functionDefinitions) {
    Invoke-Expression $definition
}

Write-Host "[powershell] Testing private IPv4 classification..."
foreach ($ip in @(
    "10.0.0.1",
    "10.255.255.255",
    "172.16.0.1",
    "172.31.255.254",
    "192.168.0.1",
    "192.168.255.254"
)) {
    Assert-True -Condition (Is-PrivateIPv4 $ip) -Message "$ip should be private."
}

foreach ($ip in @(
    "8.8.8.8",
    "127.0.0.1",
    "172.15.255.255",
    "172.32.0.1",
    "192.167.255.255",
    "192.169.0.1",
    "256.1.2.3",
    "foo"
)) {
    Assert-False -Condition (Is-PrivateIPv4 $ip) -Message "$ip should not be private."
}

Write-Host "[powershell] Testing uniqueness helper..."
$unique = @(Unique-Strings @(
    " 192.168.1.1 ",
    "192.168.1.1",
    "",
    $null,
    "10.0.0.2",
    "10.0.0.2"
))
Assert-Equal -Expected @("192.168.1.1", "10.0.0.2") -Actual $unique -Message "Unique-Strings should trim, drop empties, and preserve first occurrence."

Write-Host "[powershell] Testing ADB device parsing with a fake executable..."
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("android-headless-mirror-tests-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $temp | Out-Null

try {
    $fakeAdb = Join-Path $temp "fake-adb.cmd"
    @'
@echo off
echo List of devices attached
echo USB123 device product:oriole model:Pixel_6 transport_id:1
echo 192.168.1.40:5555 device product:oriole model:Pixel_6 transport_id:2
echo AUTH unauthorized transport_id:3
echo OFF offline transport_id:4
echo NOPERM no permissions transport_id:5
'@ | Set-Content -Path $fakeAdb -Encoding ASCII

    $devices = @(Get-AdbDevices $fakeAdb)
    Assert-Equal -Expected 5 -Actual $devices.Count -Message "Get-AdbDevices should parse all known ADB states."
    Assert-Equal -Expected "USB123" -Actual $devices[0].Serial -Message "First serial should parse."
    Assert-False -Condition $devices[0].IsTcp -Message "USB serial must not be classified as TCP."
    Assert-True -Condition $devices[1].IsTcp -Message "host:port serial must be classified as TCP."
    Assert-Equal -Expected "unauthorized" -Actual $devices[2].State -Message "Unauthorized state should parse."
    Assert-Equal -Expected "offline" -Actual $devices[3].State -Message "Offline state should parse."
    Assert-Equal -Expected "no permissions" -Actual $devices[4].State -Message "No-permissions state should parse."

    Write-Host "[powershell] Testing device selection..."
    $state = [pscustomobject]@{ PreferredSerial = "" }
    $script:Config = [pscustomobject]@{
        PreferredSerial = ""
        PreferUsb = $true
    }
    $selected = Select-Device $devices $state
    Assert-Equal -Expected "USB123" -Actual $selected.Serial -Message "USB should win over TCP by default."

    $state.PreferredSerial = "192.168.1.40:5555"
    $selected = Select-Device $devices $state
    Assert-Equal -Expected "192.168.1.40:5555" -Actual $selected.Serial -Message "Saved preferred serial should win."

    $script:Config.PreferredSerial = "USB123"
    $selected = Select-Device $devices $state
    Assert-Equal -Expected "USB123" -Actual $selected.Serial -Message "Configured preferred serial should override saved state."

    $script:Config.PreferredSerial = ""
    $state.PreferredSerial = "MISSING_DEVICE"
    $selected = Select-Device $devices $state
    Assert-Equal -Expected "USB123" -Actual $selected.Serial -Message "A missing learned/preferred phone must not block another authorised USB Android device."

    $nonReady = @(
        [pscustomobject]@{ Serial = "A"; State = "offline"; IsTcp = $false },
        [pscustomobject]@{ Serial = "B"; State = "unauthorized"; IsTcp = $false }
    )
    $selected = Select-Device $nonReady $state
    Assert-True -Condition ($null -eq $selected) -Message "Non-ready devices must never be selected."

    Write-Host "[powershell] Testing phone IP extraction..."
    $fakeIpAdb = Join-Path $temp "fake-ip-adb.cmd"
    @'
@echo off
echo 3: rmnet0    inet 10.123.45.67/32 scope global rmnet0
echo 12: swlan0    inet 192.168.43.1/24 brd 192.168.43.255 scope global swlan0
echo 13: wlan0     inet 192.168.43.1/24 brd 192.168.43.255 scope global wlan0
echo 14: rndis0    inet 192.168.42.129/24 brd 192.168.42.255 scope global rndis0
'@ | Set-Content -Path $fakeIpAdb -Encoding ASCII

    $ips = @(Get-PhoneIpCandidates $fakeIpAdb "USB123")
    Assert-Equal -Expected @("192.168.43.1") -Actual $ips -Message "Wi-Fi/hotspot interfaces should be preferred and deduplicated."

    Write-Host "[powershell] Testing scrcpy argument construction..."
    $script:Config = [pscustomobject]@{
        WindowTitle = "Android Device"
        TurnPhysicalScreenOff = $true
        StayAwakeWhenUsb = $true
        KeepActiveDuringMirror = $true
        DismissKeyguardWhenPossible = $true
        WakeBeforeMirror = $true
        PowerOffOnClose = $false
        MaxSize = 1920
        MaxFps = 60
        VideoBitRate = "12M"
        PreferredSerial = ""
        PreferUsb = $true
    }

    $usbArgs = @(Build-ScrcpyArguments "USB123" $false)
    foreach ($expected in @(
        "--serial=USB123",
        "--window-title=Android Device",
        "--turn-screen-off",
        "--stay-awake",
        "--keep-active",
        "--max-size=1920",
        "--max-fps=60",
        "--video-bit-rate=12M"
    )) {
        Assert-Contains -Collection $usbArgs -Value $expected -Message "USB scrcpy args should include $expected."
    }
    Assert-False -Condition ($usbArgs -contains "--power-off-on-close") -Message "Disabled power-off-on-close should not be emitted."

    $tcpArgs = @(Build-ScrcpyArguments "192.168.1.40:5555" $true)
    Assert-False -Condition ($tcpArgs -contains "--stay-awake") -Message "TCP sessions should not receive the USB stay-awake flag."
    Assert-Contains -Collection $tcpArgs -Value "--keep-active" -Message "TCP sessions should still receive --keep-active."

    $script:Config.PowerOffOnClose = $true
    $script:Config.KeepActiveDuringMirror = $false
    $script:Config.MaxSize = 0
    $script:Config.MaxFps = 0
    $script:Config.VideoBitRate = ""
    $minimalArgs = @(Build-ScrcpyArguments "USB123" $false)
    Assert-Contains -Collection $minimalArgs -Value "--power-off-on-close" -Message "Enabled power-off-on-close should be emitted."
    Assert-False -Condition ($minimalArgs -contains "--keep-active") -Message "Disabled KeepActiveDuringMirror should suppress --keep-active."
    Assert-False -Condition (@($minimalArgs | Where-Object { $_ -like "--max-size=*" }).Count -gt 0) -Message "MaxSize=0 should suppress --max-size."
    Assert-False -Condition (@($minimalArgs | Where-Object { $_ -like "--max-fps=*" }).Count -gt 0) -Message "MaxFps=0 should suppress --max-fps."
    Assert-False -Condition (@($minimalArgs | Where-Object { $_ -like "--video-bit-rate=*" }).Count -gt 0) -Message "Blank bitrate should suppress --video-bit-rate."

    Write-Host "[powershell] Testing native scrcpy argument boundaries..."
    $scrcpyArgLog = Join-Path $temp "scrcpy-args.log"
    $env:AHM_SCRCPY_ARG_LOG = $scrcpyArgLog
    $fakeScrcpy = Join-Path $temp "fake-scrcpy.exe"
    $probeSource = @'
using System;
using System.IO;

public static class ArgProbe
{
    public static int Main(string[] args)
    {
        File.WriteAllLines(
            Environment.GetEnvironmentVariable("AHM_SCRCPY_ARG_LOG"),
            args
        );
        return 23;
    }
}
'@
    $probeSourceFile = Join-Path $temp "ArgProbe.cs"
    Set-Content -Path $probeSourceFile -Value $probeSource -Encoding UTF8

    $cscCandidates = @(
        (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
        (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
    )
    $csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    Assert-True -Condition (-not [string]::IsNullOrWhiteSpace([string]$csc)) -Message "A .NET Framework C# compiler should be available on Windows."

    & $csc /nologo /target:exe "/out:$fakeScrcpy" $probeSourceFile
    Assert-Equal -Expected 0 -Actual $LASTEXITCODE -Message "Native argument probe should compile."
    Assert-True -Condition (Test-Path $fakeScrcpy) -Message "Native argument probe executable should exist."

    $nativeArgs = @(
        "--serial=USB123",
        "--window-title=Android Device",
        "--max-fps=60"
    )
    $nativeExit = Invoke-Scrcpy $fakeScrcpy $nativeArgs $false
    Assert-Equal -Expected 23 -Actual $nativeExit -Message "Invoke-Scrcpy should return the native process exit code."

    $received = @(Get-Content $scrcpyArgLog | ForEach-Object { [string]$_ })
    Assert-Equal -Expected @(
        "--serial=USB123",
        "--window-title=Android Device",
        "--max-fps=60"
    ) -Actual $received -Message "Native .exe invocation must preserve every scrcpy argument boundary."

    Write-Host "[powershell] Testing real device preparation commands..."
    $prepareLog = Join-Path $temp "prepare.log"
    $env:AHM_PREPARE_LOG = $prepareLog
    $fakePrepareAdb = Join-Path $temp "fake-prepare-adb.cmd"
    @'
@echo off
echo %*>>"%AHM_PREPARE_LOG%"
exit /b 0
'@ | Set-Content -Path $fakePrepareAdb -Encoding ASCII

    Prepare-DeviceForMirror $fakePrepareAdb "USB123"

    $prepareLines = @(Get-Content $prepareLog)
    Assert-True -Condition (@($prepareLines | Where-Object { $_ -match '-s USB123 shell input keyevent KEYCODE_WAKEUP' }).Count -eq 1) -Message "Prepare-DeviceForMirror should send KEYCODE_WAKEUP."
    Assert-True -Condition (@($prepareLines | Where-Object { $_ -match '-s USB123 shell wm dismiss-keyguard' }).Count -eq 1) -Message "Prepare-DeviceForMirror should request best-effort keyguard dismissal."

    $script:Config.WakeBeforeMirror = $false
    $script:Config.DismissKeyguardWhenPossible = $false
    Clear-Content $prepareLog
    Prepare-DeviceForMirror $fakePrepareAdb "USB123"
    Assert-Equal -Expected 0 -Actual @(Get-Content $prepareLog -ErrorAction SilentlyContinue).Count -Message "Disabled preparation controls should emit no ADB commands."

    Write-Host "[powershell] Testing real STOP lifecycle in an isolated directory..."
    $stopSandbox = Join-Path $temp "stop-sandbox"
    New-Item -ItemType Directory -Force -Path $stopSandbox | Out-Null
    $isolatedStop = Join-Path $stopSandbox "Stop-PhoneMirror.ps1"
    Copy-Item -Path $StopPath -Destination $isolatedStop

    & $isolatedStop

    $flag = Join-Path $stopSandbox "stop.flag"
    Assert-True -Condition (Test-Path $flag) -Message "STOP must persist stop.flag."
    $flagText = (Get-Content $flag -Raw).Trim()
    $parsedTimestamp = [DateTime]::MinValue
    Assert-True -Condition ([DateTime]::TryParse($flagText, [ref]$parsedTimestamp)) -Message "stop.flag should contain a parseable timestamp."

    Write-Host "[powershell] Testing destructive-operation guards..."
    $supervisor = Get-Content $SupervisorPath -Raw
    Assert-False -Condition ($supervisor -match 'Remove-Item\s+-Force\s+\$StopFile') -Message "Supervisor must not clear persistent OFF state."

    $stopSource = Get-Content $StopPath -Raw
    Assert-False -Condition ($stopSource -match 'kill-server') -Message "STOP must not kill the shared ADB server."
}
finally {
    if (Test-Path $temp) {
        Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue
    }
}

Write-Host ""
Write-Host "PowerShell behavior tests passed: $script:Assertions assertions." -ForegroundColor Green
