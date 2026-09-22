[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
. (Join-Path $Root "MirrorInteraction.ps1")

$script:Assertions = 0

function Assert-True([bool]$Condition, [string]$Message) {
    $script:Assertions++
    if (-not $Condition) { throw "ASSERTION FAILED: $Message" }
}

function Assert-Equal($Expected, $Actual, [string]$Message) {
    $script:Assertions++
    if ($Expected -is [double] -or $Actual -is [double]) {
        if ([Math]::Abs(([double]$Expected) - ([double]$Actual)) -gt 0.000001) {
            throw "ASSERTION FAILED: $Message Expected=$Expected Actual=$Actual"
        }
        return
    }

    if ($Expected -ne $Actual) {
        throw "ASSERTION FAILED: $Message Expected=$Expected Actual=$Actual"
    }
}

function Assert-Near([double]$Expected, [double]$Actual, [double]$Tolerance, [string]$Message) {
    $script:Assertions++
    if ([Math]::Abs($Expected - $Actual) -gt $Tolerance) {
        throw "ASSERTION FAILED: $Message Expected=$Expected Actual=$Actual Tolerance=$Tolerance"
    }
}

Write-Host "[mirror-interaction] Testing sensitivity-adjusted pinch scaling..."
Assert-Equal 1.0 (Get-SensitivityAdjustedScale 1.0 0.6) "Scale 1 must remain exactly neutral."
Assert-True ((Get-SensitivityAdjustedScale 1.5 0.5) -lt 1.5) "Sensitivity below 1 must soften zoom-in."
Assert-True ((Get-SensitivityAdjustedScale 0.5 0.5) -gt 0.5) "Sensitivity below 1 must soften zoom-out."
$forward = Get-SensitivityAdjustedScale 1.8 0.65
$inverse = Get-SensitivityAdjustedScale (1.0 / 1.8) 0.65
Assert-Near 1.0 ($forward * $inverse) 0.000001 "Zoom sensitivity must remain symmetric in/out."

Write-Host "[mirror-interaction] Testing host zoom clamping and reset threshold..."
Assert-Equal 1.0 (Get-HostZoomFromGesture 1.0 0.1 1.0 1.0 4.0) "Host zoom may not go below 100%."
Assert-Equal 4.0 (Get-HostZoomFromGesture 3.5 2.0 1.0 1.0 4.0) "Host zoom must respect configured maximum."
Assert-True (-not (Test-HostZoomActive 1.0)) "Reset/minimap must be inactive at exactly 100%."
Assert-True (-not (Test-HostZoomActive 1.0005)) "Tiny numerical noise must not activate reset/minimap."
Assert-True (Test-HostZoomActive 1.01) "Zoomed view must activate reset/minimap."

Write-Host "[mirror-interaction] Testing Android pinch sensitivity independent of host zoom..."
Assert-Equal 1.0 (Get-AndroidPinchScale 1.0 0.7) "Neutral Android pinch must remain neutral."
Assert-True ((Get-AndroidPinchScale 1.8 0.5) -lt 1.8) "Android pinch sensitivity must soften large spread."
Assert-True ((Get-AndroidPinchScale 0.4 0.5) -gt 0.4) "Android pinch sensitivity must soften large contraction."
Assert-Equal 4.0 (Get-AndroidPinchScale 100.0 1.0 0.25 4.0) "Android pinch output must be bounded."
Assert-Equal 0.25 (Get-AndroidPinchScale 0.001 1.0 0.25 4.0) "Android pinch contraction must be bounded."

Write-Host "[mirror-interaction] Testing pinch-vs-scroll intent classification..."
Assert-Equal "none" (Get-InteractionGestureKind 100 100 0 0 0.02 1.25) "Stationary two-finger contact must remain neutral."
Assert-Equal "pinch" (Get-InteractionGestureKind 100 120 1 1 0.02 1.25) "Distance-dominant movement must classify as pinch."
Assert-Equal "pinch" (Get-InteractionGestureKind 100 80 1 1 0.02 1.25) "Contracting distance must classify as pinch."
Assert-Equal "scroll" (Get-InteractionGestureKind 100 101 0 24 0.02 1.25) "Center-dominant movement must classify as scroll, not pinch."
Assert-Equal "scroll" (Get-InteractionGestureKind 100 103 0 40 0.02 1.25) "Small distance noise during scrolling must not become pinch."
Assert-Equal "none" (Get-InteractionGestureKind 0 100 10 10 0.02 1.25) "Invalid baseline distance must remain neutral."

Write-Host "[mirror-interaction] Testing scroll sensitivity, deadzone and per-sample cap..."
Assert-Equal 0.0 (Get-ScaledScrollDelta 0.5 0.35 1.0 30.0) "Sub-deadzone scroll noise must be suppressed."
Assert-Equal 7.0 (Get-ScaledScrollDelta 20 0.35 1.0 30.0) "Scroll sensitivity must scale movement predictably."
Assert-Equal -7.0 (Get-ScaledScrollDelta -20 0.35 1.0 30.0) "Scroll scaling must preserve direction."
Assert-Equal 30.0 (Get-ScaledScrollDelta 1000 1.0 1.0 30.0) "One touchpad sample may not fling multiple screens."
Assert-Equal -30.0 (Get-ScaledScrollDelta -1000 1.0 1.0 30.0) "Negative fling must be capped symmetrically."

Write-Host "[mirror-interaction] Testing smoothing..."
Assert-Equal 0.0 (Get-SmoothedInteractionValue 0 100 0.0) "Zero smoothing factor must retain previous sample."
Assert-Equal 100.0 (Get-SmoothedInteractionValue 0 100 1.0) "Full smoothing factor must use current sample."
Assert-Equal 25.0 (Get-SmoothedInteractionValue 0 100 0.25) "EMA interpolation must be deterministic."

Write-Host "[mirror-interaction] Testing zoom source geometry/minimap viewport..."
$center = Get-HostZoomSourceGeometry 100 200 1000 500 2.0 0.5 0.5
Assert-Equal 500.0 $center.Width "2x zoom must sample half the source width."
Assert-Equal 250.0 $center.Height "2x zoom must sample half the source height."
Assert-Near 0.25 $center.NormalizedLeft 0.000001 "Centered 2x viewport should begin at normalized 0.25."
Assert-Near 0.25 $center.NormalizedTop 0.000001 "Centered 2x viewport should begin at normalized 0.25."
Assert-Near 0.5 $center.NormalizedWidth 0.000001 "Minimap viewport width should reflect visible fraction."
Assert-Near 0.5 $center.NormalizedHeight 0.000001 "Minimap viewport height should reflect visible fraction."

$edge = Get-HostZoomSourceGeometry 0 0 1000 500 4.0 1.0 1.0
Assert-Equal 750.0 $edge.Left "Bottom-right anchor must clamp source rectangle to right edge."
Assert-Equal 375.0 $edge.Top "Bottom-right anchor must clamp source rectangle to bottom edge."
Assert-Equal 1000.0 $edge.Right "Viewport may not pan beyond source right edge."
Assert-Equal 500.0 $edge.Bottom "Viewport may not pan beyond source bottom edge."

Write-Host "[mirror-interaction] Testing pan anchoring..."
$pan = Get-NormalizedPanAnchor 0.5 0.5 100 50 1000 500 2.0 1.0
Assert-Near 0.45 $pan.X 0.000001 "Dragging right should move the viewed source left proportionally."
Assert-Near 0.45 $pan.Y 0.000001 "Dragging down should move the viewed source up proportionally."
$panClamp = Get-NormalizedPanAnchor 0.01 0.99 99999 -99999 1000 500 2.0 1.0
Assert-Equal 0.0 $panClamp.X "Pan X must clamp to source bounds."
Assert-Equal 1.0 $panClamp.Y "Pan Y must clamp to source bounds."

Write-Host "[mirror-interaction] Testing forced-rotation normalization..."
Assert-Equal 0 (Get-ForcedRotationDegrees 0) "0 quarter turns = 0 degrees."
Assert-Equal 90 (Get-ForcedRotationDegrees 1) "1 quarter turn = 90 degrees."
Assert-Equal 270 (Get-ForcedRotationDegrees -1) "Negative quarter turn must wrap."
Assert-Equal 0 (Get-ForcedRotationDegrees 4) "Four quarter turns must wrap to zero."
Assert-Equal 180 (Get-ForcedRotationDegrees 6) "Rotation must normalize beyond one cycle."

Write-Host ""
Write-Host "Mirror interaction tests passed: $script:Assertions assertions." -ForegroundColor Green
