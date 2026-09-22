[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
. (Join-Path $Root "ScrcpyControl.ps1")

$script:Assertions = 0

function Assert-True([bool]$Condition, [string]$Message) {
    $script:Assertions++
    if (-not $Condition) { throw "ASSERTION FAILED: $Message" }
}

function Assert-False([bool]$Condition, [string]$Message) {
    Assert-True (-not $Condition) $Message
}

function Assert-Equal($Expected, $Actual, [string]$Message) {
    $script:Assertions++
    if ($Expected -ne $Actual) {
        throw "ASSERTION FAILED: $Message Expected=$Expected Actual=$Actual"
    }
}

function Assert-Shortcut(
    [hashtable]$Map,
    [string]$Name,
    [int]$Key,
    [bool]$Alt,
    [bool]$Shift,
    [int]$Repeat = 1
) {
    Assert-True $Map.ContainsKey($Name) "Shortcut map must contain '$Name'."
    $spec = $Map[$Name]
    Assert-Equal $Key ([int]$spec.Key) "$Name key must match scrcpy."
    Assert-Equal $Alt ([bool]$spec.Alt) "$Name Alt modifier must match scrcpy."
    Assert-Equal $Shift ([bool]$spec.Shift) "$Name Shift modifier must match scrcpy."
    Assert-Equal $Repeat ([int]$spec.Repeat) "$Name repeat count must match scrcpy."
}

Write-Host "[scrcpy-control] Loading native shortcut implementation..."
$map = Get-ScrcpyShortcutMap

Write-Host "[scrcpy-control] Verifying current scrcpy shortcut contract..."
Assert-Shortcut $map "fullscreen" 0x7A $false $false
Assert-Shortcut $map "fit" ([int][char]'W') $true $false
Assert-Shortcut $map "pixel-perfect" ([int][char]'G') $true $false
Assert-Shortcut $map "rotate-left" 0x25 $true $false
Assert-Shortcut $map "rotate-right" 0x27 $true $false
Assert-Shortcut $map "flip-horizontal" 0x25 $true $true
Assert-Shortcut $map "flip-vertical" 0x26 $true $true
Assert-Shortcut $map "pause" ([int][char]'Z') $true $false
Assert-Shortcut $map "resume" ([int][char]'Z') $true $true
Assert-Shortcut $map "reset-capture" ([int][char]'R') $true $true
Assert-Shortcut $map "fps" ([int][char]'I') $true $false
Assert-Shortcut $map "home" ([int][char]'H') $true $false
Assert-Shortcut $map "back" ([int][char]'B') $true $false
Assert-Shortcut $map "apps" ([int][char]'S') $true $false
Assert-Shortcut $map "menu" ([int][char]'M') $true $false
Assert-Shortcut $map "power" ([int][char]'P') $true $false
Assert-Shortcut $map "sleep" ([int][char]'O') $true $false
Assert-Shortcut $map "wake" ([int][char]'O') $true $true
Assert-Shortcut $map "rotate-device" ([int][char]'R') $true $false
Assert-Shortcut $map "notifications" ([int][char]'N') $true $false
Assert-Shortcut $map "quick-settings" ([int][char]'N') $true $false 2
Assert-Shortcut $map "collapse-panels" ([int][char]'N') $true $true
Assert-Shortcut $map "volume-down" 0x28 $true $false
Assert-Shortcut $map "volume-up" 0x26 $true $false
Assert-Shortcut $map "copy" ([int][char]'C') $true $false
Assert-Shortcut $map "cut" ([int][char]'X') $true $false
Assert-Shortcut $map "paste-sync" ([int][char]'V') $true $false
Assert-Shortcut $map "paste-inject" ([int][char]'V') $true $true
Assert-Shortcut $map "keyboard-settings" ([int][char]'K') $true $false

Write-Host "[scrcpy-control] Verifying repeated shortcut input construction..."
$flags = [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Static
$builder = [AHMScrcpyControlNative].GetMethod("BuildShortcutInputs", $flags)
Assert-True ($null -ne $builder) "BuildShortcutInputs should remain testable through reflection."

$args = [object[]]@($true, $false, [uint16][char]'N', 2)
$inputs = @($builder.Invoke($null, $args))
Assert-Equal 6 $inputs.Count "MOD+n+n should be Alt down, N down/up twice, Alt up."
Assert-Equal ([int][AHMScrcpyControlNative]::VK_LMENU) ([int]$inputs[0].U.ki.wVk) "Alt must go down first."
Assert-Equal 0 ([int]$inputs[0].U.ki.dwFlags) "First Alt event must be key-down."
Assert-Equal ([int][char]'N') ([int]$inputs[1].U.ki.wVk) "First repeated key must be N."
Assert-Equal ([int][char]'N') ([int]$inputs[3].U.ki.wVk) "Second repeated key must be N without releasing MOD."
Assert-Equal ([int][AHMScrcpyControlNative]::VK_LMENU) ([int]$inputs[5].U.ki.wVk) "Alt must be released last."
Assert-True (([int]$inputs[5].U.ki.dwFlags -band [int][AHMScrcpyControlNative]::KEYEVENTF_KEYUP) -ne 0) "Final Alt event must be key-up."

Write-Host "[scrcpy-control] Verifying test-mode routing for every supported action..."
foreach ($name in $map.Keys) {
    $result = Invoke-ScrcpyNamedShortcut -WindowTitle "TEST" -Name $name -TestMode
    Assert-True $result.Ok "Test mode should accept known shortcut '$name'."
    Assert-True ($result.Text -match [regex]::Escape($name)) "Test response should identify '$name'."
}

$unknown = Invoke-ScrcpyNamedShortcut -WindowTitle "TEST" -Name "definitely-not-real" -TestMode
Assert-False $unknown.Ok "Unknown shortcut must be rejected before input injection."
Assert-True ($unknown.Text -match "Unknown scrcpy shortcut") "Unknown shortcut error should be deterministic."

Write-Host "[scrcpy-control] Verifying zero target cannot be reported as delivered..."
$zeroSend = [AHMScrcpyControlNative]::SendShortcut(
    [IntPtr]::Zero,
    $true,
    $false,
    [uint16][char]'H',
    1
)
Assert-False $zeroSend "A null HWND must never report successful shortcut delivery."

Write-Host ""
Write-Host "Scrcpy control tests passed: $script:Assertions assertions." -ForegroundColor Green
