# Display compatibility

This file is the hardware-validation ledger for display transports.

Do not convert a reported capability into a compatibility claim until the exact path has been tested.

## Status vocabulary

- **reported** — the operating system, driver or connected-device metadata reports the capability;
- **verified** — a human completed the documented hardware test successfully;
- **failed** — the documented hardware test did not succeed;
- **unknown** — not tested or inconclusive.

## Baseline device

The repository documents the Samsung Galaxy S21 Ultra / SM-G998B as tested project hardware for the existing ADB/scrcpy path.

Protected external-display playback must still be verified on the exact Windows machine used as the receiver.

## Validation matrix

| Path | Normal UI | Audio | ADB control in parallel | Protected playback |
| --- | --- | --- | --- | --- |
| scrcpy | expected | expected | yes | unsupported by capture design |
| Smart View -> Windows Wireless Display | manual test required | manual test required | manual test required | manual test required |
| Wireless DeX -> Windows Wireless Display | manual test required | manual test required | manual test required | manual test required |
| USB-C / DisplayPort -> compliant external display | manual test required | manual test required | expected over separate USB ADB | manual test required |

## Manual Windows receiver test

1. Run `rex display probe`.
2. Ensure Windows Wireless Display is installed and Miracast receive is not reported unsupported.
3. Run `rex display receiver open`.
4. On the phone, start Smart View or Wireless DeX.
5. Select the PC.
6. Verify normal UI and audio.
7. Verify that ADB commands still work over the existing USB connection.
8. Test the protected application.
9. Record the observed result with `rex display verify`.

Do not record `pass` based only on an OS capability string. Verify actual playback.

## Adding hardware results

When documenting another PC or Android device, include:

- Android model and software version;
- Windows version;
- Wi-Fi adapter/driver;
- GPU/driver;
- transport (Smart View, Wireless DeX, wired external display);
- normal video/audio result;
- ADB parallel-control result;
- protected playback result;
- date tested.

Application policy and driver behavior may change, so results are observations rather than permanent guarantees.
