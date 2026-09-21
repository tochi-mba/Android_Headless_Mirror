[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Source = Join-Path $Root "Bootstrap-RexCli.ps1"
$Temp = Join-Path ([System.IO.Path]::GetTempPath()) ("rex-bootstrap-tests-" + [guid]::NewGuid().ToString("N"))

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "ASSERTION FAILED: $Message" }
}

try {
    New-Item -ItemType Directory -Force -Path $Temp | Out-Null
    Copy-Item $Source (Join-Path $Temp "Bootstrap-RexCli.ps1")

    $cliDir = Join-Path $Temp "tools\rex"
    New-Item -ItemType Directory -Force -Path $cliDir | Out-Null
    Set-Content -Path (Join-Path $cliDir "rex.exe") -Value "placeholder" -Encoding ASCII

    $outputFile = Join-Path $Temp "stdout.txt"
    $errorFile = Join-Path $Temp "stderr.txt"

    $arguments = @(
        "-NoLogo",
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", (Join-Path $Temp "Bootstrap-RexCli.ps1")
    )

    $process = Start-Process -FilePath "powershell.exe" -ArgumentList $arguments -WorkingDirectory $Temp -Wait -PassThru -RedirectStandardOutput $outputFile -RedirectStandardError $errorFile

    Assert-True ($process.ExitCode -eq 0) "Bootstrap should be idempotent when rex.exe already exists."

    $output = Get-Content $outputFile -Raw
    Assert-True ($output -match "CLI already available") "Bootstrap should report the existing CLI."
    Assert-True (Test-Path (Join-Path $cliDir "rex.exe")) "Existing rex.exe must not be removed."

    Write-Host "REX CLI bootstrap behavior passed." -ForegroundColor Green
}
finally {
    if (Test-Path $Temp) {
        Remove-Item -Recurse -Force $Temp -ErrorAction SilentlyContinue
    }
}
