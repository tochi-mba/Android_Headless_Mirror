[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$InstallDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try {
    $installRoot = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
    if (-not [IO.Directory]::Exists($installRoot)) { exit 0 }
    $prefix = $installRoot + '\'

    function Get-InstalledProcesses {
        Get-Process -Name RexMirror, scrcpy, adb -ErrorAction SilentlyContinue | Where-Object {
            # Match the executable location, never just the name. Other ADB installations
            # and development checkouts must survive an installed-app upgrade.
            $exe = $_.Path
            $exe -and $exe.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
        }
    }

    $cli = Join-Path $installRoot 'rex.exe'
    if (Test-Path -LiteralPath $cli) {
        $quit = Start-Process -FilePath $cli -ArgumentList 'quit' -WindowStyle Hidden -PassThru
        if (-not $quit.WaitForExit(10000)) { $quit.Kill(); $quit.WaitForExit() }
    }

    # Quit is acknowledged before WPF has finished tearing down the mirror.
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (@(Get-InstalledProcesses | Where-Object ProcessName -ne 'adb').Count -gt 0 -and
           [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }

    foreach ($process in @(Get-InstalledProcesses)) {
        try {
            if (-not $process.HasExited) { $process.Kill() }
            if (-not $process.WaitForExit(10000)) { throw 'Process did not exit.' }
        }
        catch {
            if (-not $process.HasExited) { throw }
        }
        finally { $process.Dispose() }
    }
    if (@(Get-InstalledProcesses).Count -gt 0) { throw 'An installed process restarted during shutdown.' }
    exit 0
}
catch {
    Write-Error -ErrorAction Continue ("Could not prepare REX for upgrade: " + $_.Exception.Message)
    exit 1
}
