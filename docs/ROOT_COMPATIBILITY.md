# Root compatibility ledger

This file records hardware/provider observations. Root support is capability-tested at runtime; provider names alone are not compatibility guarantees.

## Validation vocabulary

- **visible** — `su` is visible to the ADB shell;
- **granted** — explicit authorization returned an elevated process;
- **restricted** — elevation exists but capability probes are constrained;
- **verified** — the relevant REX feature was tested successfully;
- **unknown** — not yet tested.

## Validation matrix

| Environment | Passive probe | Authorization | Provider metadata | Capability probes | Root v1 features |
| --- | --- | --- | --- | --- | --- |
| Magisk | manual hardware test required | manual test required | best effort | runtime | manual test required |
| KernelSU | manual hardware test required | manual test required | best effort | runtime | manual test required |
| APatch | manual hardware test required | manual test required | best effort | runtime | manual test required |
| generic `su` | manual hardware test required | manual test required | may be `Other` | runtime | manual test required |
| root `adbd` / userdebug | runtime | no `su` required | `None` | runtime | manual test required |

## Hardware test procedure

1. Connect and authorize the Android device over ADB.
2. Run `rex root status --serial <SERIAL>`.
3. Confirm the passive command did not create a root-manager authorization prompt.
4. Run `rex root request --serial <SERIAL>`.
5. Approve or deny the root-manager prompt deliberately.
6. Run `rex root capabilities --serial <SERIAL>`.
7. Exercise diagnostics, processes, hardware, network, properties and kernel logs.
8. Test a known private application data directory using the read-only file commands.
9. Reboot Android and confirm the previous boot's cached verification is not reused.
10. Record provider/version, Android version, kernel and any restricted capabilities below.

## Tested hardware

The repository's normal ADB/scrcpy baseline includes the Samsung Galaxy S21 Ultra / SM-G998B. Root-provider behavior has not been claimed for that device until a rooted hardware configuration is actually tested and recorded here.

## Results

Add dated results here after real-device validation. Do not promote CI fake-root coverage into a hardware compatibility claim.
