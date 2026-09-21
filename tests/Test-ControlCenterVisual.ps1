[CmdletBinding()]
param(
    [string]$OutputDirectory = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$ControlCenterPath = Join-Path $Root "ControlCenter.ps1"
$ThresholdPath = Join-Path $Root "tests\visual-thresholds.json"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $Root "artifacts\control-center-visual"
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$Thresholds = Get-Content $ThresholdPath -Raw | ConvertFrom-Json

$script:Assertions = 0

function Assert-True {
    param([bool]$Condition, [string]$Message)
    $script:Assertions++
    if (-not $Condition) { throw "VISUAL ASSERTION FAILED: $Message" }
}

function Assert-Between {
    param([double]$Value, [double]$Min, [double]$Max, [string]$Message)
    Assert-True ($Value -ge $Min -and $Value -le $Max) "$Message Value=$Value Range=$Min..$Max"
}

function Get-ImageMetrics([string]$Path) {
    Add-Type -AssemblyName System.Drawing
    $bitmap = New-Object System.Drawing.Bitmap($Path)

    try {
        $step = 4
        $total = 0
        $dark = 0
        $signal = 0
        $text = 0
        $orange = 0
        $colors = New-Object 'System.Collections.Generic.HashSet[int]'

        for ($y = 0; $y -lt $bitmap.Height; $y += $step) {
            for ($x = 0; $x -lt $bitmap.Width; $x += $step) {
                $pixel = $bitmap.GetPixel($x, $y)
                $total++
                [void]$colors.Add($pixel.ToArgb())

                if ($pixel.R -le 45 -and $pixel.G -le 55 -and $pixel.B -le 48) {
                    $dark++
                }

                if (
                    [Math]::Abs([int]$pixel.R - 215) -le 35 -and
                    [Math]::Abs([int]$pixel.G - 255) -le 25 -and
                    [Math]::Abs([int]$pixel.B - 63) -le 45
                ) {
                    $signal++
                }

                if ($pixel.R -ge 210 -and $pixel.G -ge 210 -and $pixel.B -ge 205) {
                    $text++
                }

                if (
                    [Math]::Abs([int]$pixel.R - 255) -le 25 -and
                    [Math]::Abs([int]$pixel.G - 119) -le 35 -and
                    [Math]::Abs([int]$pixel.B - 77) -le 35
                ) {
                    $orange++
                }
            }
        }

        return [pscustomobject]@{
            Width = $bitmap.Width
            Height = $bitmap.Height
            TotalSamples = $total
            DarkRatio = if ($total -gt 0) { $dark / [double]$total } else { 0.0 }
            SignalPixels = $signal
            TextPixels = $text
            OrangePixels = $orange
            UniqueColors = $colors.Count
        }
    }
    finally {
        $bitmap.Dispose()
    }
}

function Assert-ElementInsideWindow([string]$Name) {
    $element = C $Name
    Assert-True ($element.ActualWidth -ge 0) "$Name should have non-negative width."
    Assert-True ($element.ActualHeight -ge 0) "$Name should have non-negative height."

    if ($element.ActualWidth -le 0 -or $element.ActualHeight -le 0) { return }

    $topLeft = $element.TranslatePoint((New-Object System.Windows.Point(0,0)), $Window)
    $right = $topLeft.X + $element.ActualWidth
    $bottom = $topLeft.Y + $element.ActualHeight

    Assert-True ($topLeft.X -ge -2) "$Name should not render left of the window."
    Assert-True ($topLeft.Y -ge -2) "$Name should not render above the window."
    Assert-True ($right -le $Window.ActualWidth + 2) "$Name should not render past the right edge."
    Assert-True ($bottom -le $Window.ActualHeight + 2) "$Name should not render past the bottom edge."
}

Write-Host "[visual] Loading real WPF window..."
. $ControlCenterPath -Serial "TEST123" -TestMode

$tabs = @(
    @{ Name="ControlsTab"; File="controls.png" },
    @{ Name="PcSettingsTab"; File="pc-settings.png" },
    @{ Name="DeviceSettingsTab"; File="device-settings.png" },
    @{ Name="AdvancedTab"; File="advanced-android.png" },
    @{ Name="DiagnosticsTab"; File="diagnostics.png" }
)

$metricsRows = @()

try {
    foreach ($tab in $tabs) {
        (C "MainTabs").SelectedItem = C $tab.Name
        [System.Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke(
            [Action]{},
            [System.Windows.Threading.DispatcherPriority]::Render
        )

        $path = Join-Path $OutputDirectory $tab.File
        $snapshot = Render-ControlCenterSnapshot $path

        Assert-True (Test-Path $path) "$($tab.Name) screenshot should be written."
        Assert-True ($snapshot.Width -ge [int]$Thresholds.windows.width) "$($tab.Name) width should meet fixed render target."
        Assert-True ($snapshot.Height -ge [int]$Thresholds.windows.height) "$($tab.Name) height should meet fixed render target."

        $metrics = Get-ImageMetrics $path

        $tabThresholds = [pscustomobject]@{
            darkRatioMin = [double]$Thresholds.windows.darkRatioMin
            darkRatioMax = [double]$Thresholds.windows.darkRatioMax
            signalPixelsMin = [int]$Thresholds.windows.signalPixelsMin
            textPixelsMin = [int]$Thresholds.windows.textPixelsMin
            orangePixelsMin = [int]$Thresholds.windows.orangePixelsMin
            uniqueColorsMin = [int]$Thresholds.windows.uniqueColorsMin
        }

        if (
            $null -ne $Thresholds.windows.PSObject.Properties["tabs"] -and
            $null -ne $Thresholds.windows.tabs.PSObject.Properties[$tab.Name]
        ) {
            $override = $Thresholds.windows.tabs.($tab.Name)
            foreach ($property in @(
                "darkRatioMin",
                "darkRatioMax",
                "signalPixelsMin",
                "textPixelsMin",
                "orangePixelsMin",
                "uniqueColorsMin"
            )) {
                if ($null -ne $override.PSObject.Properties[$property]) {
                    $tabThresholds.$property = $override.$property
                }
            }
        }

        $metricsRows += [pscustomobject]@{
            Tab = $tab.Name
            File = $tab.File
            Width = $metrics.Width
            Height = $metrics.Height
            DarkRatio = [Math]::Round($metrics.DarkRatio, 4)
            SignalPixels = $metrics.SignalPixels
            TextPixels = $metrics.TextPixels
            OrangePixels = $metrics.OrangePixels
            UniqueColors = $metrics.UniqueColors
        }

        # Persist diagnostic metrics before asserting so failed CI still retains
        # the exact pixel counts that triggered the regression.
        $metricsRows | ConvertTo-Json -Depth 5 |
            Set-Content -Path (Join-Path $OutputDirectory "metrics.json") -Encoding UTF8

        Assert-Between $metrics.DarkRatio ([double]$tabThresholds.darkRatioMin) ([double]$tabThresholds.darkRatioMax) "$($tab.Name) should preserve the REX dark visual hierarchy."
        Assert-True ($metrics.SignalPixels -ge [int]$tabThresholds.signalPixelsMin) "$($tab.Name) should retain enough REX signal-lime pixels."
        Assert-True ($metrics.TextPixels -ge [int]$tabThresholds.textPixelsMin) "$($tab.Name) should contain enough visible high-contrast text for that tab's content density."
        Assert-True ($metrics.OrangePixels -ge [int]$tabThresholds.orangePixelsMin) "$($tab.Name) orange/live accent threshold should pass."
        Assert-True ($metrics.UniqueColors -ge [int]$tabThresholds.uniqueColorsMin) "$($tab.Name) screenshot should not collapse into an empty/flat render."
    }

    Write-Host "[visual] Verifying key layout bounds..."
    foreach ($name in @(
        "DeviceNameText",
        "ConnectionBadge",
        "MainTabs",
        "StatusBarText"
    )) {
        Assert-ElementInsideWindow $name
    }

    (C "ControlsTab").IsSelected = $true
    [System.Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([Action]{}, [System.Windows.Threading.DispatcherPriority]::Render)

    foreach ($name in @(
        "FullscreenButton",
        "SleepButton",
        "ScreenshotButton"
    )) {
        Assert-ElementInsideWindow $name
    }

    $metricsPath = Join-Path $OutputDirectory "metrics.json"
    $metricsRows | ConvertTo-Json -Depth 5 | Set-Content -Path $metricsPath -Encoding UTF8
}
finally {
    try { $Window.Close() } catch {}
}

Write-Host ""
Write-Host ("Control Center visual regression passed: " + $script:Assertions + " assertions.") -ForegroundColor Green

exit 0
