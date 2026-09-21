[CmdletBinding()]
param(
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$CliDir = Join-Path $Root "tools\rex"
$CliExe = Join-Path $CliDir "rex.exe"
$Project = Join-Path $Root "src\Rex.AndroidMirror.Cli\Rex.AndroidMirror.Cli.csproj"
$TempDir = Join-Path $env:TEMP ("rex-cli-bootstrap-" + [guid]::NewGuid().ToString("N"))

function Write-RexBootstrap([string]$Message) {
    Write-Host ("[rex] " + $Message)
}

if ((Test-Path $CliExe) -and -not $Force) {
    Write-RexBootstrap "CLI already available at $CliExe"
    exit 0
}

New-Item -ItemType Directory -Force -Path $CliDir | Out-Null
New-Item -ItemType Directory -Force -Path $TempDir | Out-Null

try {
    $downloaded = $false

    try {
        Write-RexBootstrap "Checking the latest Android Headless Mirror release for the REX CLI..."
        $headers = @{ "User-Agent" = "Android-Headless-Mirror-REX-CLI" }
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/tochi-mba/Android_Headless_Mirror/releases/latest" -Headers $headers -TimeoutSec 20

        $zipAsset = $release.assets | Where-Object { $_.name -eq "rex-win-x64.zip" } | Select-Object -First 1
        $sumAsset = $release.assets | Where-Object { $_.name -eq "rex-win-x64.zip.sha256" } | Select-Object -First 1

        if ($zipAsset -and $sumAsset) {
            $zipPath = Join-Path $TempDir "rex-win-x64.zip"
            $sumPath = Join-Path $TempDir "rex-win-x64.zip.sha256"

            Invoke-WebRequest -Uri $zipAsset.browser_download_url -OutFile $zipPath -Headers $headers -TimeoutSec 120
            Invoke-WebRequest -Uri $sumAsset.browser_download_url -OutFile $sumPath -Headers $headers -TimeoutSec 30

            $expected = ((Get-Content $sumPath -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
            $actual = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()

            if ($expected -ne $actual) {
                throw "REX CLI checksum verification failed. Expected $expected but got $actual."
            }

            $extract = Join-Path $TempDir "extract"
            Expand-Archive -Path $zipPath -DestinationPath $extract -Force

            $candidate = Get-ChildItem $extract -Filter "rex.exe" -File -Recurse | Select-Object -First 1
            if (-not $candidate) {
                throw "The REX CLI release archive did not contain rex.exe."
            }

            Copy-Item $candidate.FullName $CliExe -Force
            $downloaded = $true
            Write-RexBootstrap "Downloaded and checksum-verified the release payload."
        }
    }
    catch {
        Write-RexBootstrap ("Release download unavailable: " + $_.Exception.Message)
    }

    if (-not $downloaded) {
        $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
        if (-not $dotnet) {
            throw "REX CLI is not in the current release and the .NET SDK is not installed. Use the classic .bat launchers for now, or install the .NET 8 SDK and rerun REX.bat."
        }

        if (-not (Test-Path $Project)) {
            throw "REX CLI source project is missing: $Project"
        }

        Write-RexBootstrap "Building the REX CLI locally as a self-contained Windows executable..."
        & $dotnet.Source publish $Project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $CliDir

        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $CliExe)) {
            throw "dotnet publish did not produce rex.exe."
        }

        Write-RexBootstrap "Local CLI build complete."
    }

    Write-RexBootstrap "REX CLI ready."
}
finally {
    if (Test-Path $TempDir) {
        Remove-Item -Recurse -Force $TempDir -ErrorAction SilentlyContinue
    }
}
