<#
.SYNOPSIS
  Builds AndroidHeadlessMirror-Setup.exe: publishes the app and the CLI, bundles the latest
  verified scrcpy release and compiles the Inno Setup script. Used by CI and releases; works
  locally when the .NET 10 SDK and Inno Setup 6 are installed.

.PARAMETER Version
  The product version stamped into the executables and the installer (for example 2.0.1).
#>
[CmdletBinding()]
param(
    [string]$Version = "0.0.0"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($Version -notmatch '^\d+\.\d+\.\d+(?:[-.][0-9A-Za-z.-]+)?$') {
    throw "Version '$Version' is not a valid release version. Expected a value such as 2.0.1."
}

$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root "dist"
$rexOut = Join-Path $dist "rex"
$scrcpyOut = Join-Path $dist "scrcpy"

if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
New-Item -ItemType Directory -Force -Path $rexOut, $scrcpyOut | Out-Null

Write-Host "[installer] Publishing $Version"
foreach ($project in "src/Rex.Mirror/Rex.Mirror.csproj", "src/Rex.Cli/Rex.Cli.csproj") {
    dotnet publish (Join-Path $root $project) -c Release -r win-x64 --self-contained true -p:Version=$Version -o $rexOut -nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $project." }
}
foreach ($exe in "RexMirror.exe", "rex.exe") {
    if (-not (Test-Path (Join-Path $rexOut $exe))) { throw "$exe was not produced." }
}

Write-Host "[installer] Fetching the latest verified scrcpy release"
if (-not $env:GITHUB_TOKEN -and (Get-Command gh -ErrorAction SilentlyContinue)) {
    # Anonymous GitHub API calls are rate limited; borrow the developer's gh login when there is one.
    $env:GITHUB_TOKEN = (& gh auth token 2>$null)
}
$env:REX_ROOT = $root
& (Join-Path $rexOut "rex.exe") setup
if ($LASTEXITCODE -ne 0) { throw "rex setup failed." }

$versions = Get-ChildItem (Join-Path $root "tools/scrcpy") -Directory |
    Where-Object { $_.Name -notlike ".install-*" -and (Test-Path (Join-Path $_.FullName "scrcpy.exe")) -and (Test-Path (Join-Path $_.FullName "adb.exe")) }
$newest = $versions |
    Sort-Object { [version](([regex]::Match($_.Name, "\d+(\.\d+)+")).Value) } -Descending |
    Select-Object -First 1
if (-not $newest) { throw "No complete scrcpy folder under tools/scrcpy." }
$scrcpyVersion = [regex]::Match($newest.Name, "v?\d+(\.\d+)+").Value
Copy-Item -Recurse -Force $newest.FullName (Join-Path $scrcpyOut $scrcpyVersion)
Write-Host "[installer] Bundled scrcpy $scrcpyVersion"

$iscc = @(
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    $command = Get-Command iscc -ErrorAction SilentlyContinue
    if (-not $command) { throw "Inno Setup 6 (ISCC.exe) was not found. Install it from https://jrsoftware.org/isinfo.php." }
    $iscc = $command.Source
}

Write-Host "[installer] Compiling"
& $iscc "/DAppVersion=$Version" "/DSource=$dist" "/O$dist" "/Q" (Join-Path $PSScriptRoot "AndroidHeadlessMirror.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed." }

$setupName = "AndroidHeadlessMirror-Setup.exe"
$setup = Join-Path $dist $setupName
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $setupName" | Set-Content "$setup.sha256" -Encoding ASCII
Write-Host "[installer] $setup ($([math]::Round((Get-Item $setup).Length / 1MB)) MB)"
