# AGENTS.md

Android Headless Mirror mirrors an Android phone into one Windows window (WPF) around scrcpy and
ADB. This file is the contract for coding agents and automation working in or against this repository.

## Machine interface

Use the CLI in machine mode. All three spellings are equivalent and select the same protocol:

```bat
rex agent <command> [args]
rex --json <command> [args]
rex <command> --json
```

`rex` is on the PATH after installing; in a checkout use `REX.bat agent <command>` instead.

The machine interface never prompts, writes exactly one JSON document to stdout, contains no ANSI
sequences, exits 0 on success and 1 on failure, and reports `protocolVersion` 2:

```json
{"ok":true,"protocolVersion":2,"command":"status","data":{...}}
{"ok":false,"protocolVersion":2,"command":"action","error":{"type":"InvalidOperationException","message":"..."}}
```

Discover the live contract first:

```bat
REX.bat agent capabilities
```

Commands: `capabilities`, `status`, `devices`, `diagnostics`, `open`, `stop`, `quit`, `action <id>`,
`zoom <in|out|reset>`, `screenshot`, `phone get|set <setting> <value>`,
`android list|get|set|delete <system|secure|global> [key] [value] [--filter text]`,
`config list|get|set|restore`, `autostart on|off`, `lock-mode <serial> <pattern|other|none>`,
`reset-lock [serial|ALL]`.

Rules:

- Omit `--serial` only when exactly one authorised phone is connected; otherwise pass it. The CLI
  never guesses between phones.
- ADB-backed actions (home, back, volume, notifications...) work without the app. scrcpy-backed
  actions (screen off, rotate, pause, clipboard) and `zoom` need the app running; use `open` first.
- `android set` refuses the protected keys that could cut off ADB (`adb_enabled`,
  `development_settings_enabled`, `android_id`, `bluetooth_address`, `adb_wifi_enabled`).
- `config set` keeps the type of the existing value and writes `config.json.rex-backup` first;
  `config restore` swaps them back.

## Layout and paths

Two layouts exist and `AppPaths` resolves them:

- **Installed** (`installer/`): executables in `%LocalAppData%\Programs\Android Headless Mirror`
  with scrcpy under `scrcpy\`; data (`config.json`, `state.json`, `logs`, `captures`, app-installed
  scrcpy updates under `tools\scrcpy`) in `%LocalAppData%\REX\Android Headless Mirror`.
- **Checkout**: the repository folder (config.json next to REX.bat) is the root for both.

`REX_ROOT` overrides the root; `REX_ADB_PATH` + `REX_SCRCPY_PATH` override tool discovery.

## Architecture

```
REX.bat                 developer launcher: builds tools\rex with the .NET SDK, opens the app or runs rex.exe
installer/              Inno Setup script + build.ps1 (publish, bundle verified scrcpy, compile, checksum)
src/Rex.Core            UI-free library shared by the app and the CLI
  AppPaths              root discovery (checkout vs installed) and every derived path
  RexConfig/ConfigFile  typed config.json (schema 2) with migration from the schema-1 keys
  ConfigStore           dotted-path access to config.json for the CLI
  AdbClient/AdbParsing  every ADB call, quoting, output parsing, friendly settings
  ScrcpyArguments       the scrcpy command line for an embedded session
  ScrcpyInstaller       verified download of the official scrcpy release; ToolLocator finds the newest copy
  StateStore            state.json: phones, lock-screen answers, calibration, UI state
  PatternGeometry       pattern-guide geometry (pure functions)
  ZoomMath              zoom/pan geometry (pure functions)
  Ipc                   pipe protocol between rex.exe and the app
  MirrorActions         the single list of user actions and their scrcpy shortcuts
src/Rex.Mirror          WPF app (RexMirror.exe)
  Mirror/MirrorHost     HwndHost that embeds scrcpy and scales it for zoom
  Mirror/OverlayWindow  transparent layer: pattern guide, navigator, touchpad receiver
  Mirror/FullscreenHud* the compact fullscreen HUD in its own non-activating window
  Mirror/TouchpadBridge Precision Touchpad contacts → phone touch (or Alt → host zoom/pan)
  Mirror/PatternGuide   keyguard polling, geometry discovery, calibration
  Session/*             supervisor: device watching, scrcpy lifecycle, actions
  Services/*            composition root, pipe server, command router, tray icon
  Views/*               the side-panel tabs and the guided first run (OnboardingView)
src/Rex.Cli             rex.exe: human commands and MachineMode
tests/Rex.Tests         xUnit: unit, contract and end-to-end tests
tests/Rex.FakeAdb       deterministic adb.exe stand-in (scenario JSON, call log)
tests/Rex.FakeScrcpy    scrcpy.exe stand-in: a real window the app embeds
docs/                   GitHub Pages site; its download button points at the latest release asset
```

## Invariants

- One window. Nothing floats over the mirror except the transparent overlay it owns.
- The first run is guided inside the window (OnboardingView) until a phone has been mirrored once;
  it never blocks the mirror from opening.
- F11 uses the full monitor bounds. Its compact HUD lives in the owned overlay and fades when idle;
  leaving fullscreen restores window placement. Phone orientation actions are distinct from PC view rotation.
- scrcpy is launched with `--shortcut-mod=rctrl+ralt`, `--mouse=sdk`, `--keyboard=sdk`,
  `--window-borderless` and `--no-window-aspect-ratio-lock`; `Mirror.ExtraArgs` cannot override these
  and is validated wherever it is written (settings panel, `config set`, `Normalize`).
- Zoom scales the embedded surface. Never reintroduce a magnifier or a second window for zoom.
- Plain two-finger touchpad gestures go to the phone as real touch. Alt is the only host modifier.
- Any authorised phone can be used. A preferred serial is a preference, never a lock.
- The app never calls `adb kill-server`, never stores or injects unlock credentials, never needs admin.
- Wireless ADB stays opt-in.
- scrcpy installs are never deleted while a copy may be running; a new version goes into a new folder
  and discovery picks the newest complete one.
- Persistence is transactional across processes: every config/state read-modify-write mutation must hold
  `CrossProcessFileLock`. `AtomicFile` provides crash-safe replacement, not concurrency control.
- Only REX-owned mirror/session processes belong to the kill-on-close Job Object. Never assign the shared
  ADB server to it.
- Low-level keyboard/mouse hooks may classify input and enqueue work only. Do not perform file I/O, ADB,
  process work, native resizing, or substantial layout/render work synchronously inside a hook callback.
- `app.manifest` is the source of truth for PerMonitorV2 DPI awareness. Code crossing WPF/Win32 geometry
  boundaries must be explicit about DIPs versus physical pixels.
- IPC requests stay current-user-only, size-bounded and deadline-controlled. Fire-and-forget server handlers
  must observe and log their own unexpected exceptions.
- Tests wait for observable state rather than fixed sleeps, except where elapsed time itself is the
  behaviour under test or Windows input/compositor APIs expose no better signal.
- No file over 1,000 lines. No dead code. Warnings are errors. Scripts are limited to `REX.bat`,
  `assets/make-icon.ps1` and `installer/build.ps1`.

## Testing

```powershell
dotnet build Rex.sln
dotnet test --project tests/Rex.Tests/Rex.Tests.csproj
npm ci; npm run test:pages
./installer/build.ps1 -Version 0.0.0
```

The end-to-end tests launch `RexMirror.exe` with `--root <temp package>` against `tests/Rex.FakeAdb`
and `tests/Rex.FakeScrcpy`, drive it over the pipe (`REX_PIPE_NAME`), and save screenshots to
`artifacts/screens`. Add a test for every user-visible change; CI is the release gate and also
compiles the installer. Tagging `v*` publishes `AndroidHeadlessMirror-Setup.exe` plus its SHA-256.

## Generated files (never commit)

`tools/`, `dist/`, `artifacts/`, `state.json`, `logs/`, `captures/`, `config.json.rex-backup`, build output.
