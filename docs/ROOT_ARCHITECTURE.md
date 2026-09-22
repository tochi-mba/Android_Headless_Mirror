# Root / Privileged Android architecture

REX treats root as a capability source, not a boolean device property.

## Invariants

- Normal ADB remains the default Android control plane.
- `root status` and `root probe` are passive and never invoke `su`.
- `root request` is the only Root v1 command that intentionally requests superuser authorization.
- A visible `su` command does not mean authorization was granted.
- UID 0 does not imply every privileged capability is usable.
- Root verification is cached only for the same Android serial and boot ID.
- All Root v1 feature commands are read-only.
- Root v1 has no arbitrary root shell, flashing, boot-image patching, AVB modification, root hiding or DRM bypass.

## State model

`Unknown` — root has not been classified.

`AdbdRoot` — the ADB daemon shell itself is UID 0.

`Unavailable` — no usable elevation route is visible to the current ADB shell.

`SuDetected` — a `su` command is visible but REX has not requested authorization.

`AuthorizationPending` — the explicit request did not complete before the bounded timeout; the phone may be waiting on a root-manager prompt.

`Denied` — the root provider rejected or failed the request.

`GrantedRestricted` — REX obtained an elevated process but one or more expected capability probes are restricted.

`Granted` — REX obtained root and the core capability probes succeeded.

## Provider model

Provider detection is metadata. Generic root remains usable even when the provider is unknown.

REX currently recognizes best-effort signals for:

- Magisk
- KernelSU
- APatch
- other / unknown `su` implementations
- root `adbd` environments

Provider recognition never substitutes for an actual privilege probe.

## Execution boundary

Privileged commands are represented as structured `PrivilegedCommand` objects and run through `IAndroidShellRunner`.

User-controlled values are shell-quoted by one implementation boundary. Feature code must not concatenate user values into raw `su -c` strings.

Every command has:

- an operation ID;
- a fixed executable;
- structured arguments;
- a risk class;
- a timeout;
- a bounded output limit.

## Boot-scoped verification

Successful verification is stored locally in `root-state.json` using the Android serial and `/proc/sys/kernel/random/boot_id`.

A reboot changes the boot ID, so a previous verification cannot silently remain valid for the next boot.

`root-state.json` is runtime state and is ignored by Git.

## Capability probes

After explicit authorization REX probes individual capabilities, including:

- private application data;
- process inspection;
- kernel logs;
- protected Android system files;
- hardware/sysfs telemetry;
- kernel network diagnostics;
- system-property inspection.

Features consume these capabilities rather than assuming `uid=0` means unrestricted root.

## Root v1 features

Human CLI and machine mode expose:

```text
rex root status
rex root probe
rex root request
rex root capabilities
rex root clear
rex root diagnostics
rex root files list <absolute-path>
rex root files stat <absolute-path>
rex root files read <absolute-path>
rex root processes
rex root process <pid>
rex root app <package>
rex root hardware
rex root network
rex root logs kernel
rex root properties
```

The same commands are available under `REX.bat agent root ...` for coding agents.
