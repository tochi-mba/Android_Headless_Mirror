# AGENTS.md

## Purpose

This repository is **Android Headless Mirror**, a Windows-first Android mirroring/control package made by **REX Technologies**.

The project is designed for Android devices generally through standard ADB + scrcpy behavior. The Samsung Galaxy S21 Ultra / SM-G998B is documented as tested hardware only; do not turn device-specific observations into global assumptions.

Coding agents and automation should prefer the repository's machine interface over scraping human UI output.

## Machine-readable entry point

For coding agents, prefer the explicit `agent` alias:

```bat
REX.bat agent <command> [args...]
```

The following are equivalent machine-mode spellings:

```bat
REX.bat agent status
REX.bat --json status
REX.bat status --json
REX.bat --plain status
```

`agent`, `--plain`, and `--json` all select the same versioned, non-interactive JSON protocol. The `--plain` / `--json` flags may appear before or after the command. The batch bootstrap is quiet in machine mode so it does not contaminate stdout.

This bootstraps the self-contained REX CLI when needed and invokes the same `MachineMode` contract inside `rex.exe`.

The machine interface:

- never prompts;
- emits exactly one JSON document to stdout after the CLI is running;
- emits no Spectre/ANSI decoration;
- uses protocol version `1`;
- returns exit code `0` on success and non-zero on failure;
- returns `{"ok":true,...}` or `{"ok":false,...}`.

Discover the current machine contract before assuming capabilities. These are equivalent:

```bat
REX.bat agent capabilities
REX.bat --json capabilities
REX.bat --plain capabilities
```

If the executable is already bootstrapped, agents may call it directly:

```bat
tools\rex\rex.exe agent capabilities
```

## Common shell commands

Inspect the package:

```bat
REX.bat agent status
REX.bat agent devices
REX.bat agent diagnostics
```

Inspect display capabilities before assuming transport support:

```bat
REX.bat agent display probe
REX.bat agent display capabilities
REX.bat agent display start --transport scrcpy
REX.bat agent display receiver open
REX.bat agent display verify protected pass
```

Display invariants:

- ADB is the independent control plane; a display transport does not own device control.
- `scrcpy` is the default capture transport and must report Android secure/protected surfaces as unavailable.
- `windows-miracast` is orchestration around the Windows receiver, not a REX Miracast/HDCP implementation.
- Do not infer protected playback from Miracast or HDCP reporting. Keep it unknown until the exact hardware/application path is manually verified.
- Do not infer DRM from black frames.
- A Samsung manufacturer match means only "DeX candidate"; runtime verification is still required.
Inspect privileged Android capability before assuming root support:

```bat
REX.bat agent root status
REX.bat agent root probe
REX.bat agent root capabilities
```

Only request authorization when the user/task explicitly needs privileged access:

```bat
REX.bat agent root request
```

Root invariants:

- `root status` / `root probe` must remain passive and must not invoke `su`.
- A visible `su` command is only a reported route, not proof that access is granted.
- UID 0 does not imply every capability is usable; check the returned capability states.
- Root verification is scoped to device serial + Android boot ID.
- Root v1 feature commands are read-only.
- Prefer structured `root diagnostics`, `root files`, `root processes`, `root app`, `root hardware`, `root network`, `root logs` and `root properties` operations.
- Do not add or use an arbitrary agent-facing root shell when a structured operation can exist.
- Do not infer DRM capture capability from root access.
- Do not use root-provider detection as a substitute for real capability probes.
Inspect or change PC / mirror configuration:

```bat
REX.bat --json config list
REX.bat --json config list --filter MirrorChrome
REX.bat --json config get MaxFps
REX.bat --json config set MaxFps 90
REX.bat --json config restore
```

Control the mirror:

```bat
REX.bat --json action sleep --serial USB123
REX.bat --json action wake --serial USB123
REX.bat --json action home --serial USB123
REX.bat --json mirror zoom-in --serial USB123
REX.bat --json mirror zoom-out --serial USB123
REX.bat --json mirror reset-zoom --serial USB123
```

Change friendly Android settings:

```bat
REX.bat --json device set brightness 180 --serial USB123
REX.bat --json device set screen-timeout-ms 60000 --serial USB123
REX.bat --json device set auto-rotate 1 --serial USB123
REX.bat --json device set animation-scale 0.5 --serial USB123
REX.bat --json device set wifi enable --serial USB123
```

Inspect live Android Settings Provider keys:

```bat
REX.bat --json android list system --serial USB123
REX.bat --json android list secure --filter lock --serial USB123
REX.bat --json android list global --filter animation --serial USB123
REX.bat --json android get system font_scale --serial USB123
```

Explicit writes/deletes:

```bat
REX.bat --json android set system font_scale 1.15 --serial USB123
REX.bat --json android delete system some_key --serial USB123
```

The backend blocks a protected set of keys that could break ADB recovery or mutate device identity.

Smart human-entry state can also be inspected/executed without prompts:

```bat
REX.bat --plain smart
REX.bat --plain shortcut install
REX.bat --plain shortcut remove
```

`smart` never invents a device choice. It reports whether setup is required, focuses an existing mirror, starts a ready mirror, or starts/keeps the supervisor waiting for an authorized device.

Other lifecycle commands:

```bat
REX.bat --json start
REX.bat --json stop
REX.bat --json autostart on
REX.bat --json autostart off
REX.bat --json setup --skip-autostart
REX.bat --json repair
REX.bat --json screenshot --serial USB123
REX.bat --json lock-mode USB123 pattern
REX.bat --json reset-lock USB123
```

If exactly one authorized Android device is connected, `--serial` may be omitted. If multiple authorized devices are present, machine mode must not guess; pass an explicit serial.

## Architecture

Keep these layers separate:

- `src/Rex.AndroidMirror.Cli/` — Spectre interactive CLI and prompt-free JSON machine interface.
- `src/Rex.AndroidMirror.Cli/MachineMode.cs` — the single stable agent/plain JSON contract; do not create a second competing machine CLI.
- `RexBridge.ps1` — machine-readable bridge over the existing PowerShell/ADB/scrcpy backend.
- `DeviceControl.ps1` — friendly Android settings, live Settings Provider access, screenshots and Android command-service probing.
- `ScrcpyControl.ps1` — scrcpy runtime shortcut adapter.
- `Start-PhoneMirror.ps1` — supervisor, device selection, scrcpy launch arguments and session lifecycle.
- `DisplayManager.cs` / `WindowsDisplayHostProbe.cs` — display transport capability model, Windows receiver orchestration and verification state.
- `RootManager.cs` / `AndroidShellRunner.cs` — passive/explicit root discovery, boot-scoped verification, provider metadata and the single privileged command boundary.
- `RootFeatureService.cs` — structured read-only privileged diagnostics/files/process/apps/hardware/network/log/property operations.
- `RootPolicy.cs` — core risk-policy enforcement; UI visibility is not a security boundary.
- `MirrorChrome.ps1` — always-on mirror toolbar, host zoom and touchpad bridge.
- `PatternOverlay.ps1` — pattern-lock geometry discovery/calibration/overlay.
- `ControlCenter.ps1` + `ControlCenter.xaml` — WPF GUI.
- `docs/` — GitHub Pages product site.
- `tests/` — repository, PowerShell, CLI, WPF, bridge, browser and visual regression tests.

The interactive CLI and WPF Control Center should call the same backend. Do not reimplement Android behavior independently in C# when a shared backend function already exists.

## Important invariants

### Device selection

The project should work with **any authorized Android device** the user chooses or connects.

Do not introduce a permanent learned-device lock that prevents another authorized Android phone from being used.

### STOP semantics

`STOP.bat` / persistent OFF means exactly that:

- create/preserve `stop.flag`;
- stop this package's scrcpy/supervisor/control-center processes;
- do not resurrect automatically;
- only an explicit start removes the stop condition.

### ADB safety

Do **not** call global `adb kill-server` as part of normal stop/cleanup. The ADB server may be shared with Android Studio or other tools.

Do not claim to bypass Android's secure first unlock after a reboot. A full reboot may still require the user's PIN/password once.

### Privileged Android

Root support must remain capability-driven.

Never auto-request superuser authorization merely because `su` is visible. Passive probes and explicit authorization are separate operations.

Root v1 is an inspection release. Keep reversible/system/device-critical writes disabled by policy and do not add flashing, boot-image patching, AVB manipulation, root hiding, raw root shell or DRM-circumvention behavior.
### Mirror interaction

Keep Android gestures and Windows-only magnification separate:

- natural two-finger pinch/spread -> Android pinch/zoom;
- natural two-finger slide -> Android scroll through the bounded REX touchpad bridge;
- Alt + pinch -> PC-only host magnification;
- Alt + two-finger slide while host zoom is active -> pan the host magnified viewport;
- Alt + wheel -> PC-only host zoom fallback.

Do not assign Ctrl or Shift to REX host zoom. scrcpy uses Ctrl for Android pinch/rotate simulation, Shift for vertical two-finger tilt, and Ctrl+Shift for horizontal tilt.

The scrcpy launch contract pins `--shortcut-mod=lalt`; do not allow `ExtraScrcpyArgs` to override it because Control Center runtime actions depend on one deterministic MOD.

Touchpad sensitivity/deadzone/fling-cap settings must affect runtime behavior, not only the UI. Keep gesture math in `MirrorInteraction.ps1` so it remains deterministic and unit-testable.

Host zoom state is published under the per-device runtime directory. Reset host zoom must be disabled at 100%, and the zoom navigator/minimap must only appear while host zoom is active.

Android forced rotation is a best-effort, reversible device setting. Keep it separate from scrcpy's display-rotation shortcut and always expose an Automatic restore path.

### Wireless ADB

Wireless ADB is opt-in and disabled by default. USB remains the recovery path.

### Runtime Android settings

OEMs and Android versions differ. Prefer runtime capability discovery and live `system` / `secure` / `global` enumeration over a hard-coded "all Android settings" catalog.

Surface real permission failures instead of pretending unsupported writes succeeded.

### Config safety

The CLI's config writer preserves JSON types and writes a one-step rollback copy at:

```text
config.json.rex-backup
```

Use `config restore` rather than hand-editing after a bad machine-driven config change when possible.

### Smart REX desktop entry

Setup installs a per-user Desktop shortcut named `REX`. It launches `REX.bat smart`.

Expected semantics:

- setup incomplete -> guided terminal setup;
- mirror already running -> focus the existing mirror, do not start a duplicate;
- authorized Android device present and mirror stopped -> explicit start;
- no authorized device and supervisor stopped -> start supervisor and show waiting/status UX;
- no authorized device and supervisor already running -> do not start a duplicate supervisor;
- persistent OFF is cleared only because clicking the REX shortcut is itself an explicit start request.

### User-facing parity

If a user-facing capability is added or changed, consider all relevant surfaces:

1. runtime/backend;
2. REX interactive CLI;
3. JSON machine mode;
4. WPF Control Center;
5. README;
6. GitHub Pages;
7. tests.

Do not let the CLI, GUI and documentation silently drift.

## Testing requirements

Before considering a change complete, run the relevant tests. CI is the final release gate.

Repository contracts:

```powershell
python tests\test_package.py
```

PowerShell behavior:

```powershell
pwsh -File tests\Test-PowerShellBehavior.ps1
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File tests\Test-PowerShellBehavior.ps1
```

REX bridge:

```powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File tests\Test-RexBridgeE2E.ps1
```

REX CLI:

```powershell
dotnet restore tests\Rex.AndroidMirror.Cli.Tests\Rex.AndroidMirror.Cli.Tests.csproj
dotnet build tests\Rex.AndroidMirror.Cli.Tests\Rex.AndroidMirror.Cli.Tests.csproj -c Release --no-restore
dotnet test tests\Rex.AndroidMirror.Cli.Tests\Rex.AndroidMirror.Cli.Tests.csproj -c Release --no-build
```

WPF:

```powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File tests\Test-ControlCenterE2E.ps1
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File tests\Test-ControlCenterVisual.ps1
```

Pages:

```powershell
npm ci
npm run test:pages
```

Self-contained CLI publish:

```powershell
dotnet publish src\Rex.AndroidMirror.Cli\Rex.AndroidMirror.Cli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## Test expectations

Do not weaken tests simply to make CI green.

When a visual threshold fails, determine whether:

- the UI genuinely regressed;
- the metric is measuring the wrong property;
- the threshold was calibrated against an unrealistic palette assumption.

When a behavior test fails, fix the implementation unless the test is demonstrably asserting the wrong contract.

New user-visible flows should receive executable coverage. New terminal flows should receive deterministic CLI tests. New Pages/UI layouts should receive responsive/accessibility/visual coverage when appropriate.

For machine-mode changes, test all relevant invocation forms (`agent`, prefix flag, suffix flag) and assert the output remains exactly one parseable JSON document with no ANSI/bootstrap chatter.

## Generated/runtime files

Do not commit runtime state or local tools:

- `tools/`
- `logs/`
- `runtime/`
- `captures/`
- `display-verification.json`
- `root-state.json`
- `state.json`
- `stop.flag`
- `pattern-calibration/`
- `config.json.rex-backup`
- test/visual artifacts

## Git / PR workflow

Keep changes on the active feature branch and open/update a PR.

Before merging:

- verify the **latest PR head**, not an older workflow run;
- require Windows package/behavior/CLI/WPF checks to pass;
- require Pages browser/accessibility/visual checks to pass;
- do not merge based on a previously green commit if newer commits have not passed.

For this project, GitHub Pages should remain synchronized with major user-facing capabilities.
