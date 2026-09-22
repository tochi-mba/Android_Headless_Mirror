# Root capability reference

REX separates root-provider visibility from verified privileged capabilities.

## Capability states

- `unknown` — support has not been established;
- `unsupported` — the current verified privilege profile failed the operation-specific probe;
- `reported` — a mechanism is visible but not yet verified;
- `verified` — the operation-specific probe succeeded.

## Core capability IDs

| Capability ID | Probe intent | Root v1 use |
| --- | --- | --- |
| `device.root` | usable root state for the current device/boot | top-level privilege gate |
| `root.private-app-data` | private Android package data is readable | app/private-data inspection |
| `root.process-inspection` | privileged process information is readable | process explorer |
| `root.kernel-logs` | kernel log access succeeds | bounded kernel logs |
| `root.system-files` | Android system-data files are visible | diagnostics |
| `root.hardware-telemetry` | kernel/sysfs telemetry is visible | hardware inspection |
| `root.network-diagnostics` | kernel networking state is readable | network inspection |
| `root.system-properties` | Android property inspection works | property browser |

Provider identity never substitutes for these probes. A provider may expose UID 0 while deliberately restricting capabilities, groups, files or SELinux access.

## Passive commands

These commands never invoke `su`:

```text
rex root status [--serial S]
rex root probe [--serial S]
rex root capabilities [--serial S]
```

## Explicit authorization

```text
rex root request [--serial S]
```

This is the Root v1 command that may cause the phone's root manager to display an authorization prompt.

## Local verification cache

```text
rex root clear [--serial S]
```

`clear` removes only REX's local verification for the selected serial. It does not change the root provider or its own authorization database.

## Read-only feature commands

```text
rex root diagnostics
rex root files list /absolute/path
rex root files stat /absolute/path
rex root files read /absolute/path
rex root processes
rex root process 1234
rex root app com.example.app
rex root hardware
rex root network
rex root logs kernel
rex root properties
```

Pass `--serial <SERIAL>` when more than one authorized Android device is connected.

## Filesystem constraints

Root v1 file inspection:

- requires an absolute Android path;
- rejects control characters;
- bounds file output;
- blocks raw reads from `/dev/block`, `/dev/mem`, `/dev/kmem` and `/proc/kcore`.

## Machine/agent interface

The same operations are available through the prompt-free JSON protocol:

```text
REX.bat agent root status
REX.bat agent root request --serial USB123
REX.bat agent root diagnostics --serial USB123
```

Machine mode emits one JSON document, no ANSI, and no interactive prompts.
