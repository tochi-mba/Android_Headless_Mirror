<#
.SYNOPSIS
  Prepares tools\rex (RexMirror.exe + rex.exe) from the latest GitHub release, or builds it
  locally with the .NET SDK when no release is available. The release archive is verified
  against its SHA-256 before anything is extracted.
#>
[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$Quiet
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

function Write-Step([string]$Message) {
    if (-not $Quiet) { Write-Host ("[rex] " + $Message) }
}

if ((Test-Path $AppExe) -and (Test-Path $CliExe) -and -not $Force) {
    Write-Step "Ready: $ToolsDir"
    exit 0
}

New-Item -ItemType Directory -Force -Path $TempDir | Out-Null

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

    if (Test-Path $ToolsDir) { Remove-Item -Recurse -Force $ToolsDir }
    New-Item -ItemType Directory -Force -Path $ToolsDir | Out-Null
    Copy-Item -Path (Join-Path $app.Directory.FullName "*") -Destination $ToolsDir -Recurse -Force
    Write-Step "Installed $($release.tag_name)."
}

function Install-FromSource {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        throw "No release is available and the .NET SDK is not installed. Install the .NET 10 SDK (https://dot.net) and run REX.bat again."
    }

    Write-Step "Building from source (first time only; this takes a minute)..."
    $projects = @(
        (Join-Path $Root "src\Rex.Mirror\Rex.Mirror.csproj"),
        (Join-Path $Root "src\Rex.Cli\Rex.Cli.csproj")
    )
    foreach ($project in $projects) {
        & $dotnet.Source publish $project -c Release -r win-x64 --self-contained true -o $ToolsDir -nologo -v quiet
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $project." }
    }

    if (-not (Test-Path $AppExe) -or -not (Test-Path $CliExe)) {
        throw "The build did not produce RexMirror.exe and rex.exe."
    }

    Write-Step "Local build complete."
}

try {
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
finally {
    if (Test-Path $TempDir) {
        Remove-Item -Recurse -Force $TempDir -ErrorAction SilentlyContinue
    }
}
