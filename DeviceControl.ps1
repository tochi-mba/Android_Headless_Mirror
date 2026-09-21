Set-StrictMode -Version Latest

function Quote-AdbShellArgument([string]$Value) {
    if ($null -eq $Value) { return "''" }
    return "'" + ($Value -replace "'", "'\"'\"'") + "'"
}

function Test-AndroidSettingKey([string]$Key) {
    return (
        -not [string]::IsNullOrWhiteSpace($Key) -and
        $Key -match '^[A-Za-z0-9._:-]+$'
    )
}

function Get-AndroidSettingRisk([string]$Namespace, [string]$Key) {
    $normalized = ([string]$Key).ToLowerInvariant()

    $protected = @(
        "adb_enabled",
        "development_settings_enabled",
        "android_id",
        "bluetooth_address"
    )

    if ($protected -contains $normalized) {
        return "protected"
    }

    if (
        $normalized -match '(^|_)(accessibility|install|unknown|verifier|mock|location|vpn|proxy|dns|airplane|data|wifi|bluetooth|package|device_provisioned|user_setup_complete)'
    ) {
        return "sensitive"
    }

    if ($Namespace -eq "secure" -or $Namespace -eq "global") {
        return "advanced"
    }

    return "normal"
}

function Invoke-AdbText {
    param(
        [Parameter(Mandatory = $true)][string]$AdbPath,
        [Parameter(Mandatory = $true)][string]$Serial,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    try {
        $output = & $AdbPath -s $Serial @Arguments 2>&1
        $exitCode = $LASTEXITCODE
        $text = (($output | ForEach-Object { [string]$_ }) -join [Environment]::NewLine).Trim()

        return [pscustomobject]@{
            Ok = ($exitCode -eq 0)
            ExitCode = $exitCode
            Text = $text
        }
    }
    catch {
        return [pscustomobject]@{
            Ok = $false
            ExitCode = -1
            Text = $_.Exception.Message
        }
    }
}

function Get-AndroidDeviceIdentity {
    param(
        [Parameter(Mandatory = $true)][string]$AdbPath,
        [Parameter(Mandatory = $true)][string]$Serial
    )

    $manufacturer = (Invoke-AdbText $AdbPath $Serial @("shell","getprop","ro.product.manufacturer")).Text
    $model = (Invoke-AdbText $AdbPath $Serial @("shell","getprop","ro.product.model")).Text
    $release = (Invoke-AdbText $AdbPath $Serial @("shell","getprop","ro.build.version.release")).Text
    $sdk = (Invoke-AdbText $AdbPath $Serial @("shell","getprop","ro.build.version.sdk")).Text

    return [pscustomobject]@{
        Manufacturer = $manufacturer
        Model = $model
        AndroidVersion = $release
        ApiLevel = $sdk
        Serial = $Serial
        DisplayName = ((@($manufacturer, $model) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join " ").Trim()
    }
}

function Get-AndroidSettingsNamespace {
    param(
        [Parameter(Mandatory = $true)][string]$AdbPath,
        [Parameter(Mandatory = $true)][string]$Serial,
        [Parameter(Mandatory = $true)][ValidateSet("system","secure","global")][string]$Namespace
    )

    $result = Invoke-AdbText $AdbPath $Serial @("shell","settings","list",$Namespace)
    if (-not $result.Ok) {
        return [pscustomobject]@{ Ok = $false; Error = $result.Text; Rows = @() }
    }

    $rows = @()
    foreach ($line in ($result.Text -split "\r?\n")) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }

        $equals = $line.IndexOf("=")
        if ($equals -lt 1) { continue }

        $key = $line.Substring(0, $equals)
        $value = $line.Substring($equals + 1)
        $rows += [pscustomobject]@{
            Namespace = $Namespace
            Key = $key
            Value = $value
            Risk = Get-AndroidSettingRisk $Namespace $key
        }
    }

    return [pscustomobject]@{
        Ok = $true
        Error = ""
        Rows = @($rows | Sort-Object Key)
    }
}

function Get-AndroidSetting {
    param(
        [Parameter(Mandatory = $true)][string]$AdbPath,
        [Parameter(Mandatory = $true)][string]$Serial,
        [Parameter(Mandatory = $true)][ValidateSet("system","secure","global")][string]$Namespace,
        [Parameter(Mandatory = $true)][string]$Key
    )

    if (-not (Test-AndroidSettingKey $Key)) {
        return [pscustomobject]@{ Ok = $false; Text = "Invalid settings key." }
    }

    return Invoke-AdbText $AdbPath $Serial @("shell","settings","get",$Namespace,$Key)
}

function Set-AndroidSetting {
    param(
        [Parameter(Mandatory = $true)][string]$AdbPath,
        [Parameter(Mandatory = $true)][string]$Serial,
        [Parameter(Mandatory = $true)][ValidateSet("system","secure","global")][string]$Namespace,
        [Parameter(Mandatory = $true)][string]$Key,
        [AllowEmptyString()][string]$Value,
        [switch]$AllowProtected
    )

    if (-not (Test-AndroidSettingKey $Key)) {
        return [pscustomobject]@{ Ok = $false; ExitCode = -1; Text = "Invalid settings key." }
    }

    $risk = Get-AndroidSettingRisk $Namespace $Key
    if ($risk -eq "protected" -and -not $AllowProtected) {
        return [pscustomobject]@{
            Ok = $false
            ExitCode = -1
            Text = "Android Headless Mirror protects '$Key' because changing it could sever ADB access or alter device identity."
        }
    }

    if ($null -eq $Value) { $Value = "" }
    if ($Value.Length -gt 8192 -or $Value -match '[\r\n\x00]') {
        return [pscustomobject]@{ Ok = $false; ExitCode = -1; Text = "Unsupported settings value." }
    }

    $quoted = Quote-AdbShellArgument $Value
    return Invoke-AdbText $AdbPath $Serial @("shell","settings","put",$Namespace,$Key,$quoted)
}

function Remove-AndroidSetting {
    param(
        [Parameter(Mandatory = $true)][string]$AdbPath,
        [Parameter(Mandatory = $true)][string]$Serial,
        [Parameter(Mandatory = $true)][ValidateSet("system","secure","global")][string]$Namespace,
        [Parameter(Mandatory = $true)][string]$Key
    )

    if (-not (Test-AndroidSettingKey $Key)) {
        return [pscustomobject]@{ Ok = $false; ExitCode = -1; Text = "Invalid settings key." }
    }

    if ((Get-AndroidSettingRisk $Namespace $Key) -eq "protected") {
        return [pscustomobject]@{
            Ok = $false
            ExitCode = -1
            Text = "Android Headless Mirror protects '$Key' from deletion."
        }
    }

    return Invoke-AdbText $AdbPath $Serial @("shell","settings","delete",$Namespace,$Key)
}

function Get-AndroidCommandServices {
    param(
        [Parameter(Mandatory = $true)][string]$AdbPath,
        [Parameter(Mandatory = $true)][string]$Serial
    )

    $result = Invoke-AdbText $AdbPath $Serial @("shell","cmd","-l")
    if (-not $result.Ok) { return @() }

    return @(
        ($result.Text -split "\r?\n") |
            ForEach-Object { $_.Trim() } |
            Where-Object { $_ -and $_ -notmatch '^Currently running services' } |
            Sort-Object -Unique
    )
}

function Test-RangeValue {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][double]$Min,
        [Parameter(Mandatory = $true)][double]$Max
    )

    $number = 0.0
    if (-not [double]::TryParse([string]$Value, [ref]$number)) { return $false }
    return ($number -ge $Min -and $number -le $Max)
}

function Set-FriendlyAndroidSetting {
    param(
        [Parameter(Mandatory = $true)][string]$AdbPath,
        [Parameter(Mandatory = $true)][string]$Serial,
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)]$Value
    )

    switch ($Id) {
        "brightness" {
            if (-not (Test-RangeValue $Value 1 255)) { return [pscustomobject]@{ Ok=$false; Text="Brightness must be between 1 and 255." } }
            return Set-AndroidSetting $AdbPath $Serial system screen_brightness ([int]$Value)
        }
        "brightness-mode" {
            if ([string]$Value -notin @("0","1")) { return [pscustomobject]@{ Ok=$false; Text="Brightness mode must be 0 or 1." } }
            return Set-AndroidSetting $AdbPath $Serial system screen_brightness_mode ([string]$Value)
        }
        "screen-timeout-ms" {
            if (-not (Test-RangeValue $Value 5000 86400000)) { return [pscustomobject]@{ Ok=$false; Text="Screen timeout must be 5 seconds to 24 hours." } }
            return Set-AndroidSetting $AdbPath $Serial system screen_off_timeout ([int64]$Value)
        }
        "auto-rotate" {
            if ([string]$Value -notin @("0","1")) { return [pscustomobject]@{ Ok=$false; Text="Auto rotate must be 0 or 1." } }
            return Set-AndroidSetting $AdbPath $Serial system accelerometer_rotation ([string]$Value)
        }
        "user-rotation" {
            if ([string]$Value -notin @("0","1","2","3")) { return [pscustomobject]@{ Ok=$false; Text="Rotation must be 0, 1, 2 or 3." } }
            return Set-AndroidSetting $AdbPath $Serial system user_rotation ([string]$Value)
        }
        "font-scale" {
            if (-not (Test-RangeValue $Value 0.5 2.0)) { return [pscustomobject]@{ Ok=$false; Text="Font scale must be between 0.5 and 2.0." } }
            return Set-AndroidSetting $AdbPath $Serial system font_scale ([string]$Value)
        }
        "show-touches" {
            if ([string]$Value -notin @("0","1")) { return [pscustomobject]@{ Ok=$false; Text="Show touches must be 0 or 1." } }
            return Set-AndroidSetting $AdbPath $Serial system show_touches ([string]$Value)
        }
        "stay-awake" {
            if ([string]$Value -notin @("0","1","2","4","7")) { return [pscustomobject]@{ Ok=$false; Text="Stay-awake mask must be 0, 1, 2, 4 or 7." } }
            return Set-AndroidSetting $AdbPath $Serial global stay_on_while_plugged_in ([string]$Value)
        }
        "animation-window" {
            if (-not (Test-RangeValue $Value 0 10)) { return [pscustomobject]@{ Ok=$false; Text="Animation scale must be between 0 and 10." } }
            return Set-AndroidSetting $AdbPath $Serial global window_animation_scale ([string]$Value)
        }
        "animation-transition" {
            if (-not (Test-RangeValue $Value 0 10)) { return [pscustomobject]@{ Ok=$false; Text="Animation scale must be between 0 and 10." } }
            return Set-AndroidSetting $AdbPath $Serial global transition_animation_scale ([string]$Value)
        }
        "animation-duration" {
            if (-not (Test-RangeValue $Value 0 10)) { return [pscustomobject]@{ Ok=$false; Text="Animation scale must be between 0 and 10." } }
            return Set-AndroidSetting $AdbPath $Serial global animator_duration_scale ([string]$Value)
        }
        "dark-mode" {
            if ([string]$Value -notin @("yes","no","auto")) { return [pscustomobject]@{ Ok=$false; Text="Dark mode must be yes, no or auto." } }
            return Invoke-AdbText $AdbPath $Serial @("shell","cmd","uimode","night",[string]$Value)
        }
        "wifi" {
            if ([string]$Value -notin @("enable","disable")) { return [pscustomobject]@{ Ok=$false; Text="Wi-Fi action must be enable or disable." } }
            return Invoke-AdbText $AdbPath $Serial @("shell","svc","wifi",[string]$Value)
        }
        "mobile-data" {
            if ([string]$Value -notin @("enable","disable")) { return [pscustomobject]@{ Ok=$false; Text="Mobile data action must be enable or disable." } }
            return Invoke-AdbText $AdbPath $Serial @("shell","svc","data",[string]$Value)
        }
        "airplane-mode" {
            if ([string]$Value -notin @("enable","disable")) { return [pscustomobject]@{ Ok=$false; Text="Airplane mode action must be enable or disable." } }
            return Invoke-AdbText $AdbPath $Serial @("shell","cmd","connectivity","airplane-mode",[string]$Value)
        }
        "wm-size" {
            if ([string]$Value -eq "reset") {
                return Invoke-AdbText $AdbPath $Serial @("shell","wm","size","reset")
            }
            if ([string]$Value -notmatch '^\d{3,5}x\d{3,5}$') { return [pscustomobject]@{ Ok=$false; Text="Display size must look like 1080x2400 or 'reset'." } }
            return Invoke-AdbText $AdbPath $Serial @("shell","wm","size",[string]$Value)
        }
        "wm-density" {
            if ([string]$Value -eq "reset") {
                return Invoke-AdbText $AdbPath $Serial @("shell","wm","density","reset")
            }
            if (-not (Test-RangeValue $Value 120 1000)) { return [pscustomobject]@{ Ok=$false; Text="Display density must be 120–1000 or 'reset'." } }
            return Invoke-AdbText $AdbPath $Serial @("shell","wm","density",[string]$Value)
        }
        default {
            return [pscustomobject]@{ Ok=$false; Text="Unknown friendly Android setting '$Id'." }
        }
    }
}

function Get-FriendlyAndroidState {
    param(
        [Parameter(Mandatory = $true)][string]$AdbPath,
        [Parameter(Mandatory = $true)][string]$Serial
    )

    $keys = @(
        @{ Name="Brightness"; Namespace="system"; Key="screen_brightness" },
        @{ Name="BrightnessMode"; Namespace="system"; Key="screen_brightness_mode" },
        @{ Name="ScreenTimeoutMs"; Namespace="system"; Key="screen_off_timeout" },
        @{ Name="AutoRotate"; Namespace="system"; Key="accelerometer_rotation" },
        @{ Name="UserRotation"; Namespace="system"; Key="user_rotation" },
        @{ Name="FontScale"; Namespace="system"; Key="font_scale" },
        @{ Name="ShowTouches"; Namespace="system"; Key="show_touches" },
        @{ Name="StayAwake"; Namespace="global"; Key="stay_on_while_plugged_in" },
        @{ Name="WindowAnimation"; Namespace="global"; Key="window_animation_scale" },
        @{ Name="TransitionAnimation"; Namespace="global"; Key="transition_animation_scale" },
        @{ Name="AnimatorDuration"; Namespace="global"; Key="animator_duration_scale" }
    )

    $state = [ordered]@{}
    foreach ($entry in $keys) {
        $result = Get-AndroidSetting $AdbPath $Serial $entry.Namespace $entry.Key
        $state[$entry.Name] = if ($result.Ok) { $result.Text } else { "" }
    }

    $state["WmSize"] = (Invoke-AdbText $AdbPath $Serial @("shell","wm","size")).Text
    $state["WmDensity"] = (Invoke-AdbText $AdbPath $Serial @("shell","wm","density")).Text
    $state["UiMode"] = (Invoke-AdbText $AdbPath $Serial @("shell","cmd","uimode","night")).Text

    return [pscustomobject]$state
}

function Save-AndroidScreenshot {
    param(
        [Parameter(Mandatory = $true)][string]$AdbPath,
        [Parameter(Mandatory = $true)][string]$Serial,
        [Parameter(Mandatory = $true)][string]$Directory
    )

    New-Item -ItemType Directory -Force -Path $Directory | Out-Null
    $file = Join-Path $Directory ("android-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".png")

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $AdbPath
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $psi.Arguments = "-s " + [char]34 + $Serial + [char]34 + " exec-out screencap -p"

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $psi

    try {
        if (-not $process.Start()) {
            return [pscustomobject]@{ Ok=$false; Text="Could not start adb screenshot capture."; Path="" }
        }

        $stream = [System.IO.File]::Open($file, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
        try {
            $process.StandardOutput.BaseStream.CopyTo($stream)
        }
        finally {
            $stream.Dispose()
        }

        $errorText = $process.StandardError.ReadToEnd()
        $process.WaitForExit()

        if ($process.ExitCode -ne 0 -or -not (Test-Path $file) -or (Get-Item $file).Length -lt 8) {
            Remove-Item -Force $file -ErrorAction SilentlyContinue
            return [pscustomobject]@{ Ok=$false; Text=$errorText.Trim(); Path="" }
        }

        return [pscustomobject]@{ Ok=$true; Text="Screenshot saved."; Path=$file }
    }
    catch {
        Remove-Item -Force $file -ErrorAction SilentlyContinue
        return [pscustomobject]@{ Ok=$false; Text=$_.Exception.Message; Path="" }
    }
    finally {
        $process.Dispose()
    }
}
