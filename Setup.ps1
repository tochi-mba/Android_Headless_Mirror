[CmdletBinding()]
param(
    [switch]$SkipAutostart
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$ToolsDir = Join-Path $Root "tools"
$ScrcpyBase = Join-Path $ToolsDir "scrcpy"
$TempDir = Join-Path $env:TEMP ("android-headless-mirror-setup-" + [guid]::NewGuid().ToString("N"))

function Write-Step([string]$Message) {
    Write-Host ("[setup] " + $Message)
}

function Find-BundledScrcpy {
    if (-not (Test-Path $ScrcpyBase)) { return $null }
    $exe = Get-ChildItem -Path $ScrcpyBase -Filter "scrcpy.exe" -File -Recurse -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    return $exe
}

try {
    New-Item -ItemType Directory -Force -Path $ScrcpyBase | Out-Null
    New-Item -ItemType Directory -Force -Path $TempDir | Out-Null

    $existing = Find-BundledScrcpy
    if ($existing) {
        Write-Step "Bundled scrcpy already exists at $($existing.FullName)"
    }
    else {
        Write-Step "Downloading the latest official scrcpy Windows x64 release..."
        $headers = @{ "User-Agent" = "Android-Headless-Mirror" }
        $release = Invoke-RestMethod `
            -Uri "https://api.github.com/repos/Genymobile/scrcpy/releases/latest" `
            -Headers $headers `
            -TimeoutSec 30

        $zipAsset = $release.assets |
            Where-Object { $_.name -match '^scrcpy-win64-v[0-9].*\.zip$' } |
            Select-Object -First 1
        $sumAsset = $release.assets |
            Where-Object { $_.name -eq 'SHA256SUMS.txt' } |
            Select-Object -First 1

        if (-not $zipAsset) {
            throw "Could not find the official Windows x64 scrcpy ZIP in the latest GitHub release."
        }
        if (-not $sumAsset) {
            throw "Could not find SHA256SUMS.txt in the latest scrcpy GitHub release."
        }

        $zipPath = Join-Path $TempDir $zipAsset.name
        $sumPath = Join-Path $TempDir "SHA256SUMS.txt"

        Invoke-WebRequest -Uri $zipAsset.browser_download_url -OutFile $zipPath -Headers $headers -TimeoutSec 120
        Invoke-WebRequest -Uri $sumAsset.browser_download_url -OutFile $sumPath -Headers $headers -TimeoutSec 30

        Write-Step "Verifying SHA-256 checksum..."
        $line = Get-Content $sumPath | Where-Object { $_ -match [regex]::Escape($zipAsset.name) } | Select-Object -First 1
        if (-not $line) {
            throw "The checksum file did not contain an entry for $($zipAsset.name)."
        }

        $expected = (($line -split '\s+')[0]).Trim().ToLowerInvariant()
        $actual = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()

        if ($expected -ne $actual) {
            throw "Checksum verification failed. Expected $expected but got $actual. Nothing was installed."
        }

        Write-Step "Checksum OK."
        $versionFolder = Join-Path $ScrcpyBase $release.tag_name
        if (Test-Path $versionFolder) {
            Remove-Item -Recurse -Force $versionFolder
        }
        New-Item -ItemType Directory -Force -Path $versionFolder | Out-Null

        $extractDir = Join-Path $TempDir "extract"
        Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force
        $inner = Get-ChildItem -Path $extractDir -Directory | Select-Object -First 1
        if ($inner) {
            Copy-Item -Path (Join-Path $inner.FullName "*") -Destination $versionFolder -Recurse -Force
        }
        else {
            Copy-Item -Path (Join-Path $extractDir "*") -Destination $versionFolder -Recurse -Force
        }

        $scrcpy = Get-ChildItem -Path $versionFolder -Filter "scrcpy.exe" -File -Recurse | Select-Object -First 1
        $adb = Get-ChildItem -Path $versionFolder -Filter "adb.exe" -File -Recurse | Select-Object -First 1
        if (-not $scrcpy -or -not $adb) {
            throw "The downloaded archive did not contain scrcpy.exe and adb.exe."
        }

        Write-Step "Installed scrcpy $($release.tag_name)."
    }

    $scrcpy = Find-BundledScrcpy
    $adb = Get-ChildItem -Path $scrcpy.Directory.FullName -Filter "adb.exe" -File | Select-Object -First 1
    if (-not $adb) { throw "adb.exe is missing next to scrcpy.exe." }

    Write-Step "Checking installed binaries..."
    & $scrcpy.FullName --version | Select-Object -First 1 | ForEach-Object { Write-Host "  $_" }
    & $adb.FullName version | Select-Object -First 1 | ForEach-Object { Write-Host "  $_" }

    if ($SkipAutostart) {
        Write-Step "Skipping Windows startup shortcut; the caller will choose whether to enable it."
    }
    else {
        Write-Step "Installing per-user Windows startup shortcut..."
        & (Join-Path $Root "Install-Autostart.ps1")
    }

    $rexBootstrap = Join-Path $Root "Bootstrap-RexCli.ps1"
    if (Test-Path $rexBootstrap) {
        try {
            Write-Step "Preparing the REX command-line app..."
            & $rexBootstrap
        }
        catch {
            Write-Warning ("REX CLI bootstrap was not available yet: " + $_.Exception.Message)
            Write-Warning "The classic launchers remain usable. REX.bat can retry the CLI bootstrap later."
        }
    }

    $rexShortcut = Join-Path $Root "Install-Rex-Shortcut.ps1"
    if (Test-Path $rexShortcut) {
        Write-Step "Installing the REX desktop shortcut..."
        & $rexShortcut
    }

    Write-Step "Setup complete."
}
finally {
    if (Test-Path $TempDir) {
        Remove-Item -Recurse -Force $TempDir -ErrorAction SilentlyContinue
    }
}
