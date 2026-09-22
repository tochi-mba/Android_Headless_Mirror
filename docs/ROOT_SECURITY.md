# Root security model

Root access turns an Android debugging utility into a highly privileged administration tool. REX therefore treats privileged execution as a separate trust boundary.

## Default policy

Root v1 defaults to:

```json
{
  "Enabled": true,
  "ProbeOnConnect": true,
  "RequestAutomatically": false,
  "AllowReadOnly": true,
  "AllowReversible": false,
  "AllowSystemChanges": false,
  "AllowDeviceCritical": false,
  "RawShellEnabled": false
}
```

REX never automatically requests superuser authorization.

## Risk classes

`ReadOnly` — inspection only. Root v1 feature commands use this class.

`Reversible` — state changes with a known inverse. Reserved for a later release and disabled by default.

`SystemChanging` — persistent system changes. Disabled by default.

`DeviceCritical` — operations that can affect bootability or partitions. Disabled by default.

The policy is enforced in the core privilege layer, not only by hiding UI controls.

## Shell injection boundary

Feature code passes an executable plus an argument array. `AndroidShellQuoting` creates the remote shell command and quotes values as single shell arguments.

Tests cover whitespace, quotes, command substitution syntax and command separators.

## Time and output bounds

Root requests and commands have cancellation/timeout handling. The process runner kills the process tree on cancellation.

Output is bounded to prevent unbounded device output from consuming arbitrary host memory. Individual operations can use a smaller limit.

## Filesystem protections

Root v1 filesystem access is read-only. Raw reads from device-critical paths such as block devices, `/dev/mem`, `/dev/kmem` and `/proc/kcore` are blocked by the feature layer.

## What Root v1 intentionally does not do

- install root;
- unlock bootloaders;
- patch or flash boot images;
- write partitions;
- alter verified boot / AVB;
- hide root or bypass device-integrity checks;
- provide an arbitrary root shell;
- bypass DRM or capture secure video surfaces.

Root support is independent of REX's protected-display architecture. A rooted device does not imply protected video can be captured.

## Agent rules

Coding agents must query `REX.bat agent root status` or `root capabilities` before relying on privileged functionality.

Agents must prefer structured REX operations over shell commands and must not infer that UID 0 grants every capability.
