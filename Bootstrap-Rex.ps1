<#
.SYNOPSIS
  Prepares tools\rex (RexMirror.exe + rex.exe) from the latest GitHub release, or builds it
  locally with the .NET SDK when no release is available. The release archive is verified
  against its SHA-256 before anything is extracted.
#>
[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$Quiet,
    [switch]$Source
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$ToolsDir = Join-Path $Root "tools\rex"
$AppExe = Join-Path $ToolsDir "RexMirror.exe"
$CliExe = Join-Path $ToolsDir "rex.exe"
$Repository = "tochi-mba/Android_Headless_Mirror"
$AssetName = "rex-win-x64.zip"
$TempDir = Join-Path $env:TEMP ("rex-bootstrap-" + [guid]::NewGuid().ToString("N"))
$StagingDir = Join-Path $Root ("tools\rex-staging-" + [guid]::NewGuid().ToString("N"))
$BackupDir = Join-Path $Root ("tools\rex-previous-" + [guid]::NewGuid().ToString("N"))

function Write-Step([string]$Message) {
    if (-not $Quiet) { Write-Host ("[rex] " + $Message) }
}

if ((Test-Path $AppExe) -and (Test-Path $CliExe) -and -not $Force -and -not $Source) {
    Write-Step "Ready: $ToolsDir"
    exit 0
}

New-Item -ItemType Directory -Force -Path $TempDir | Out-Null

function Remove-Staging {
    # Only remove this invocation's generated staging folder, never the live install.
    $parent = [IO.Path]::GetFullPath((Join-Path $Root "tools"))
    $resolved = [IO.Path]::GetFullPath($StagingDir)
    if ([IO.Path]::GetDirectoryName($resolved) -ne $parent -or
        [IO.Path]::GetFileName($resolved) -notlike 'rex-staging-*') {
        throw "Invalid bootstrap staging path."
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}

function Install-StagedPayload {
    foreach ($name in @("RexMirror.exe", "rex.exe")) {
        if (-not (Test-Path -LiteralPath (Join-Path $StagingDir $name) -PathType Leaf)) {
            throw "The replacement is incomplete: $name is missing. The installed app was preserved."
        }
    }

    # Rename on the same volume only after both builds/downloads have succeeded.
    # A locked installation fails here without deleting any of its files.
    $hadPrevious = Test-Path -LiteralPath $ToolsDir
    if ($hadPrevious) {
        foreach ($name in @("RexMirror.exe", "rex.exe")) {
            $existing = Join-Path $ToolsDir $name
            if (Test-Path -LiteralPath $existing) {
                try {
                    $handle = [IO.File]::Open($existing, 'Open', 'ReadWrite', 'None')
                    $handle.Dispose()
                }
                catch {
                    throw "Close Android Headless Mirror and any REX commands, then retry. The installed app was preserved."
                }
            }
        }
    }
    if ($hadPrevious) { Move-Item -LiteralPath $ToolsDir -Destination $BackupDir }
    try {
        Move-Item -LiteralPath $StagingDir -Destination $ToolsDir
    }
    catch {
        if ($hadPrevious) { Move-Item -LiteralPath $BackupDir -Destination $ToolsDir }
        throw
    }
    # Keep the previous payload for recovery; it may still have a running process.
    if ($hadPrevious) { Write-Step "Previous build kept at $BackupDir" }
}

function Install-FromRelease {
    Write-Step "Checking the latest release..."
    $headers = @{ "User-Agent" = "Android-Headless-Mirror-Bootstrap" }
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/releases/latest" -Headers $headers -TimeoutSec 20

    $zipAsset = $release.assets | Where-Object { $_.name -eq $AssetName } | Select-Object -First 1
    $sumAsset = $release.assets | Where-Object { $_.name -eq "$AssetName.sha256" } | Select-Object -First 1
    if (-not $zipAsset -or -not $sumAsset) {
        throw "The latest release has no $AssetName with a checksum."
    }

    $zipPath = Join-Path $TempDir $AssetName
    $sumPath = Join-Path $TempDir "$AssetName.sha256"
    Write-Step "Downloading $($release.tag_name)..."
    Invoke-WebRequest -Uri $zipAsset.browser_download_url -OutFile $zipPath -Headers $headers -TimeoutSec 300
    Invoke-WebRequest -Uri $sumAsset.browser_download_url -OutFile $sumPath -Headers $headers -TimeoutSec 30

    $expected = ((Get-Content $sumPath -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
    $actual = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expected -ne $actual) {
        throw "Checksum mismatch for $AssetName (expected $expected, got $actual). Nothing was installed."
    }

    $extract = Join-Path $TempDir "extract"
    Expand-Archive -Path $zipPath -DestinationPath $extract -Force
    $app = Get-ChildItem $extract -Filter "RexMirror.exe" -File -Recurse | Select-Object -First 1
    if (-not $app) { throw "The release archive did not contain RexMirror.exe." }

    Remove-Staging
    New-Item -ItemType Directory -Force -Path $StagingDir | Out-Null
    Copy-Item -Path (Join-Path $app.Directory.FullName "*") -Destination $StagingDir -Recurse -Force
    Install-StagedPayload
    Write-Step "Installed $($release.tag_name)."
}

function Install-FromSource {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        throw "The .NET SDK is required to build the current source. Install the .NET 10 SDK (https://dot.net) and try again."
    }

    Remove-Staging
    New-Item -ItemType Directory -Force -Path $StagingDir | Out-Null

    Write-Step "Building the current checkout from source..."
    $projects = @(
        (Join-Path $Root "src\Rex.Mirror\Rex.Mirror.csproj"),
        (Join-Path $Root "src\Rex.Cli\Rex.Cli.csproj")
    )
    foreach ($project in $projects) {
        & $dotnet.Source publish $project -c Release -r win-x64 --self-contained true -o $StagingDir -nologo -v quiet
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $project." }
    }

    Install-StagedPayload

    Write-Step "Local build complete."
}

try {
    if ($Source) {
        Install-FromSource
        Write-Step "REX is ready from the current source checkout."
    }
    else {
        $installed = $false
        try {
            Install-FromRelease
            $installed = $true
        }
        catch {
            Write-Step ("Release download unavailable: " + $_.Exception.Message)
        }

        if (-not $installed) {
            Install-FromSource
        }

        Write-Step "REX is ready."
    }
}
finally {
    Remove-Staging
    if (Test-Path $TempDir) {
        Remove-Item -Recurse -Force $TempDir -ErrorAction SilentlyContinue
    }
}
