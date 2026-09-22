Set-StrictMode -Version Latest

function Clamp-InteractionValue {
    param(
        [double]$Value,
        [double]$Minimum,
        [double]$Maximum
    )

    if ($Maximum -lt $Minimum) {
        throw "Maximum must be greater than or equal to minimum."
    }

    return [Math]::Max($Minimum, [Math]::Min($Maximum, $Value))
}

function Get-SensitivityAdjustedScale {
    param(
        [double]$RawScale,
        [double]$Sensitivity
    )

    if ($RawScale -le 0.0) {
        throw "RawScale must be greater than zero."
    }
    if ($Sensitivity -le 0.0) {
        throw "Sensitivity must be greater than zero."
    }

    # Exponential scaling keeps zoom-in and zoom-out mathematically symmetric:
    # adjusted(1/x) == 1 / adjusted(x).
    return [Math]::Exp([Math]::Log($RawScale) * $Sensitivity)
}

function Get-InteractionPinchMagnitude {
    param([double]$Scale)

    if ($Scale -le 0.0) {
        return [double]::PositiveInfinity
    }

    return [Math]::Abs([Math]::Log($Scale))
}

function Get-InteractionGestureKind {
    param(
        [double]$StartDistance,
        [double]$CurrentDistance,
        [double]$CenterDeltaX,
        [double]$CenterDeltaY,
        [double]$PinchThreshold,
        [double]$PinchDominanceRatio,
        [double]$ScrollThresholdNormalized = 0.015
    )

    if ($StartDistance -le 0.0001 -or $CurrentDistance -le 0.0001) {
        return "none"
    }

    $scale = $CurrentDistance / $StartDistance
    $pinch = Get-InteractionPinchMagnitude $scale

    $centerMovement = [Math]::Sqrt(
        ($CenterDeltaX * $CenterDeltaX) +
        ($CenterDeltaY * $CenterDeltaY)
    )
    $normalizedMovement = $centerMovement / [Math]::Max(1.0, $StartDistance)

    if (
        $pinch -ge $PinchThreshold -and
        $pinch -ge ($normalizedMovement * $PinchDominanceRatio)
    ) {
        return "pinch"
    }

    if ($normalizedMovement -ge $ScrollThresholdNormalized) {
        return "scroll"
    }

    return "none"
}

function Get-HostZoomFromGesture {
    param(
        [double]$StartZoom,
        [double]$RawScale,
        [double]$Sensitivity,
        [double]$MinZoom,
        [double]$MaxZoom
    )

    $adjusted = Get-SensitivityAdjustedScale $RawScale $Sensitivity
    $next = $StartZoom * $adjusted
    $clamped = Clamp-InteractionValue $next $MinZoom $MaxZoom

    if ([Math]::Abs($clamped - 1.0) -lt 0.001) {
        return 1.0
    }

    return $clamped
}

function Get-AndroidPinchScale {
    param(
        [double]$RawScale,
        [double]$Sensitivity,
        [double]$MinimumScale = 0.25,
        [double]$MaximumScale = 4.0
    )

    $adjusted = Get-SensitivityAdjustedScale $RawScale $Sensitivity
    return Clamp-InteractionValue $adjusted $MinimumScale $MaximumScale
}

function Get-ScaledScrollDelta {
    param(
        [double]$RawDelta,
        [double]$Sensitivity,
        [double]$Deadzone,
        [double]$MaxDeltaPerSample
    )

    if ($Sensitivity -lt 0.0) {
        throw "Sensitivity cannot be negative."
    }
    if ($Deadzone -lt 0.0) {
        throw "Deadzone cannot be negative."
    }
    if ($MaxDeltaPerSample -le 0.0) {
        throw "MaxDeltaPerSample must be greater than zero."
    }

    if ([Math]::Abs($RawDelta) -le $Deadzone) {
        return 0.0
    }

    $scaled = $RawDelta * $Sensitivity
    return Clamp-InteractionValue $scaled (-$MaxDeltaPerSample) $MaxDeltaPerSample
}

function Get-SmoothedInteractionValue {
    param(
        [double]$Previous,
        [double]$Current,
        [double]$Smoothing
    )

    $factor = Clamp-InteractionValue $Smoothing 0.0 1.0
    return $Previous + (($Current - $Previous) * $factor)
}

function Get-NormalizedPanAnchor {
    param(
        [double]$AnchorX,
        [double]$AnchorY,
        [double]$DeltaX,
        [double]$DeltaY,
        [double]$ViewportWidth,
        [double]$ViewportHeight,
        [double]$Zoom,
        [double]$Sensitivity
    )

    if ($ViewportWidth -le 0 -or $ViewportHeight -le 0) {
        throw "Viewport dimensions must be positive."
    }

    $effectiveZoom = [Math]::Max(1.0, $Zoom)
    $nextX = $AnchorX - (($DeltaX / $ViewportWidth) * $Sensitivity / $effectiveZoom)
    $nextY = $AnchorY - (($DeltaY / $ViewportHeight) * $Sensitivity / $effectiveZoom)

    return [pscustomobject]@{
        X = Clamp-InteractionValue $nextX 0.0 1.0
        Y = Clamp-InteractionValue $nextY 0.0 1.0
    }
}

function Get-HostZoomSourceGeometry {
    param(
        [double]$Left,
        [double]$Top,
        [double]$Width,
        [double]$Height,
        [double]$Zoom,
        [double]$AnchorX,
        [double]$AnchorY
    )

    if ($Width -le 0 -or $Height -le 0) {
        throw "Source dimensions must be positive."
    }

    $safeZoom = [Math]::Max(1.0, $Zoom)
    $safeAnchorX = Clamp-InteractionValue $AnchorX 0.0 1.0
    $safeAnchorY = Clamp-InteractionValue $AnchorY 0.0 1.0

    $sourceWidth = [Math]::Max(1.0, $Width / $safeZoom)
    $sourceHeight = [Math]::Max(1.0, $Height / $safeZoom)

    $centerX = $Left + ($Width * $safeAnchorX)
    $centerY = $Top + ($Height * $safeAnchorY)

    $sourceLeft = $centerX - ($sourceWidth * $safeAnchorX)
    $sourceTop = $centerY - ($sourceHeight * $safeAnchorY)

    $sourceLeft = Clamp-InteractionValue $sourceLeft $Left ($Left + $Width - $sourceWidth)
    $sourceTop = Clamp-InteractionValue $sourceTop $Top ($Top + $Height - $sourceHeight)

    return [pscustomobject]@{
        Left = $sourceLeft
        Top = $sourceTop
        Width = $sourceWidth
        Height = $sourceHeight
        Right = $sourceLeft + $sourceWidth
        Bottom = $sourceTop + $sourceHeight
        NormalizedLeft = ($sourceLeft - $Left) / $Width
        NormalizedTop = ($sourceTop - $Top) / $Height
        NormalizedWidth = $sourceWidth / $Width
        NormalizedHeight = $sourceHeight / $Height
    }
}

function Test-HostZoomActive {
    param([double]$Zoom)
    return $Zoom -gt 1.001
}

function Get-ForcedRotationDegrees {
    param([int]$QuarterTurns)

    $normalized = (($QuarterTurns % 4) + 4) % 4
    return $normalized * 90
}
