# AGENTS.md

## Purpose

This repository is **Android Headless Mirror**, a Windows-first Android mirroring/control package made by **REX Technologies**.

The project is designed for Android devices generally through standard ADB + scrcpy behavior. The Samsung Galaxy S21 Ultra / SM-G998B is documented as tested hardware only; do not turn device-specific observations into global assumptions.

Coding agents should prefer the repository's machine interface over scraping human UI output.

## Agent entry point

Use:

```bat
REX_AGENT.bat <command> [args...]
```

This bootstraps the self-contained REX CLI when needed and invokes:

```text
rex.exe --json <command> [args...]
```

The machine interface:

- never prompts;
- emits exactly one JSON document to stdout after the CLI is running;
- emits no Spectre/ANSI decoration;
- uses protocol version `1`;
- returns exit code `0` on success and non-zero on failure;
- returns `{"ok":true,...}` or `{"ok":false,...}`.

Discover the current machine contract before assuming capabilities:

```bat
REX_AGENT.bat capabilities
```

## Common agent commands

Inspect the package:

```bat
REX_AGENT.bat status
REX_AGENT.bat devices
REX_AGENT.bat diagnostics
```

Inspect or change PC / mirror configuration:

```bat
REX_AGENT.bat config list
REX_AGENT.bat config list --filter MirrorChrome
REX_AGENT.bat config get MaxFps
REX_AGENT.bat config set MaxFps 90
REX_AGENT.bat config restore
```

Control the mirror:

```bat
REX_AGENT.bat action sleep --serial USB123
REX_AGENT.bat action wake --serial USB123
REX_AGENT.bat action home --serial USB123
REX_AGENT.bat mirror zoom-in --serial USB123
REX_AGENT.bat mirror zoom-out --serial USB123
REX_AGENT.bat mirror reset-zoom --serial USB123
```

Change friendly Android settings:

```bat
REX_AGENT.bat device set brightness 180 --serial USB123
REX_AGENT.bat device set screen-timeout-ms 60000 --serial USB123
REX_AGENT.bat device set auto-rotate 1 --serial USB123
REX_AGENT.bat device set animation-scale 0.5 --serial USB123
REX_AGENT.bat device set wifi enable --serial USB123
```

Inspect live Android Settings Provider keys:

```bat
REX_AGENT.bat android list system --serial USB123
REX_AGENT.bat android list secure --filter lock --serial USB123
REX_AGENT.bat android list global --filter animation --serial USB123
REX_AGENT.bat android get system font_scale --serial USB123
```

Explicit writes/deletes:

```bat
REX_AGENT.bat android set system font_scale 1.15 --serial USB123
REX_AGENT.bat android delete system some_key --serial USB123
```

The backend blocks a protected set of keys that could break ADB recovery or mutate device identity.

Other lifecycle commands:

```bat
REX_AGENT.bat start
REX_AGENT.bat stop
REX_AGENT.bat autostart on
REX_AGENT.bat autostart off
REX_AGENT.bat setup --skip-autostart
REX_AGENT.bat repair
REX_AGENT.bat screenshot --serial USB123
REX_AGENT.bat lock-mode USB123 pattern
REX_AGENT.bat reset-lock USB123
```

If exactly one authorized Android device is connected, `--serial` may be omitted. If multiple authorized devices are present, machine mode must not guess; pass an explicit serial.

## Architecture

Keep these layers separate:

- `src/Rex.AndroidMirror.Cli/` — Spectre interactive CLI and prompt-free JSON agent interface.
- `RexBridge.ps1` — machine-readable bridge over the existing PowerShell/ADB/scrcpy backend.
- `DeviceControl.ps1` — friendly Android settings, live Settings Provider access, screenshots and Android command-service probing.
- `ScrcpyControl.ps1` — scrcpy runtime shortcut adapter.
- `Start-PhoneMirror.ps1` — supervisor, device selection, scrcpy launch arguments and session lifecycle.
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

### User-facing parity

If a user-facing capability is added or changed, consider all relevant surfaces:

1. runtime/backend;
2. REX interactive CLI;
3. JSON agent CLI;
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

## Generated/runtime files

Do not commit runtime state or local tools:

- `tools/`
- `logs/`
- `runtime/`
- `captures/`
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
