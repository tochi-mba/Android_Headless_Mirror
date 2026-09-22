# AGENTS.md

Android Headless Mirror mirrors an Android phone into one Windows window (WPF) around scrcpy and
ADB. This file is the contract for coding agents and automation working in or against this repository.

## Machine interface

Use the CLI in machine mode. All three spellings are equivalent and select the same protocol:

```bat
REX.bat agent <command> [args]
REX.bat --json <command> [args]
REX.bat <command> --json
```

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

## Architecture

```
REX.bat                 opens the app (no args) or runs rex.exe <args>; bootstraps tools/rex
Bootstrap-Rex.ps1       installs tools/rex from the latest release (SHA-256 verified) or builds it
src/Rex.Core            UI-free library shared by the app and the CLI
  AppPaths              package root discovery and paths
  RexConfig/ConfigFile  typed config.json (schema 2) with migration from the schema-1 keys
  ConfigStore           dotted-path access to config.json for the CLI
  AdbClient/AdbParsing  every ADB call, quoting, output parsing, friendly settings
  ScrcpyArguments       the scrcpy command line for an embedded session
  ScrcpyInstaller       verified download of the official scrcpy release
  StateStore            state.json: phones, lock-screen answers, calibration, UI state
  PatternGeometry       pattern-guide geometry (pure functions)
  ZoomMath              zoom/pan geometry (pure functions)
  Ipc                   pipe protocol between rex.exe and the app
  MirrorActions         the single list of user actions and their scrcpy shortcuts
src/Rex.Mirror          WPF app (RexMirror.exe)
  Mirror/MirrorHost     HwndHost that embeds scrcpy and scales it for zoom
  Mirror/OverlayWindow  transparent layer: pattern guide, navigator, touchpad receiver
  Mirror/TouchpadBridge Precision Touchpad contacts → phone touch (or Alt → host zoom/pan)
  Mirror/PatternGuide   keyguard polling, geometry discovery, calibration
  Session/*             supervisor: device watching, scrcpy lifecycle, actions
  Services/*            composition root, pipe server, command router, tray icon
  Views/*               the side-panel tabs and the first-run view
src/Rex.Cli             rex.exe: human commands and MachineMode
tests/Rex.Tests         xUnit: unit, contract and end-to-end tests
tests/Rex.FakeAdb       deterministic adb.exe stand-in (scenario JSON, call log)
tests/Rex.FakeScrcpy    scrcpy.exe stand-in: a real window the app embeds
docs/                   GitHub Pages site
```

## Invariants

- One window. Nothing floats over the mirror except the transparent overlay it owns.
- F11 uses the full monitor bounds. Its compact HUD lives in the owned overlay and fades when idle;
  leaving fullscreen restores window placement. Phone orientation actions are distinct from PC view rotation.
- scrcpy is launched with `--shortcut-mod=rctrl+ralt`, `--mouse=sdk`, `--keyboard=sdk`,
  `--window-borderless` and `--no-window-aspect-ratio-lock`; `Mirror.ExtraArgs` cannot override these.
- Zoom scales the embedded surface. Never reintroduce a magnifier or a second window for zoom.
- Plain two-finger touchpad gestures go to the phone as real touch. Alt is the only host modifier.
- Any authorised phone can be used. A preferred serial is a preference, never a lock.
- The app never calls `adb kill-server`, never stores or injects unlock credentials, never needs admin.
- Wireless ADB stays opt-in.
- No file over 1,000 lines. No dead code. Warnings are errors.

## Testing

```powershell
dotnet build Rex.sln
dotnet test --project tests/Rex.Tests/Rex.Tests.csproj
npm ci; npm run test:pages
```

The end-to-end tests launch `RexMirror.exe` against `tests/Rex.FakeAdb` and `tests/Rex.FakeScrcpy`
via the `REX_ADB_PATH` / `REX_SCRCPY_PATH` overrides, drive it over the pipe, and save screenshots to
`artifacts/screens`. Add a test for every user-visible change; CI is the release gate.

## Generated files (never commit)

`tools/`, `state.json`, `logs/`, `captures/`, `config.json.rex-backup`, build output.
