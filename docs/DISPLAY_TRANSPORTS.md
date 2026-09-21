# Display transports

REX treats Android control and display presentation as separate concerns.

## Control plane

ADB remains the independent control plane for device discovery, settings, navigation, wake/sleep, diagnostics and other supported operations.

A display transport may change without replacing the ADB connection.

## scrcpy

ID: `scrcpy`

Use scrcpy for normal mirroring.

Capabilities:

- low-latency video and audio;
- Android input/control;
- screenshots and native recording;
- physical-screen-off integration;
- host zoom and touchpad integration.

Limitation:

- Android secure/protected surfaces are not available to the ordinary capture path. REX reports protected output as unsupported for scrcpy rather than treating a black secure surface as a REX capture bug.

## Windows Wireless Display

ID: `windows-miracast`

REX does not implement Miracast, Wi-Fi Direct or HDCP. It orchestrates the Windows receiver path and leaves protocol/security negotiation to Windows, the GPU/Wi-Fi drivers, Android and the application.

Use:

```text
rex display probe
rex display receiver open
rex display start --transport windows-miracast
```

Then start Smart View or Wireless DeX on the Android device and choose the PC.

ADB can remain connected over USB while the external-display path is active.

## Protected playback verification

REX deliberately does not infer protected playback support from the presence of Miracast or an HDCP-capable component.

The complete phone + Windows + GPU/Wi-Fi driver + application chain must be tested.

Record the result locally:

```text
rex display verify normal pass
rex display verify protected pass
rex display verify protected fail
rex display verify protected clear
```

The result is stored in `display-verification.json`, which is local runtime state and is ignored by Git.

A passed protected verification promotes the external-display protected-output status to `verified`. A failed verification records that the tested path did not work. Clearing verification returns the state to unknown.

## Samsung DeX

REX reports a connected Samsung device as a DeX candidate only. It does not claim every Samsung model/firmware combination supports DeX.

The user must verify the DeX option on the actual device.

## Transport policy

`config.json`:

```json
"Display": {
  "DefaultTransport": "scrcpy",
  "ProtectedContentPolicy": "prompt",
  "WindowsWirelessDisplay": {
    "Enabled": true,
    "AutoOpenReceiver": true
  },
  "SamsungDex": {
    "Enabled": true
  }
}
```

Protected-content policy values:

- `prompt` — default; present guidance without silently switching transports;
- `ignore` — never suggest the external-display route;
- `prefer-external` — user preference for the external-display route when protected content is expected.

REX does not automatically inspect black frames and declare DRM. Black output has many possible causes and is not a reliable protected-content detector.
