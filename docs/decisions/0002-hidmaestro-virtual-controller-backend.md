# ADR 0002 — HIDMaestro virtual Xbox backend

**Status:** Accepted  
**Date:** 2026-09-30

## Context

NATSX Controller needs one stable Windows virtual controller that games can consume through the Xbox 360/XInput-compatible path.

The backend must remain isolated from:

- Android;
- transport implementations;
- Smart Connection policy;
- the application-level `GamepadState`.

## Decision

Use **HIDMaestro v1.9.2** as the first Windows virtual-controller backend.

Pinned Windows x64 release artifact:

```text
HIDMaestro-v1.9.2.zip
SHA-256:
1c19e9a34360652a8f98c595ff6c03c1f1fd4c027f180cc4d1149e3d352643d7
```

The dependency is downloaded by a repository bootstrap script and is not committed as a binary to this repository.

The adapter lives behind:

```text
IVirtualGamepadBackend
```

HIDMaestro-specific types must not leak into protocol, transport, Android, or controller-session abstractions.

## Profile

The production controller profile is:

```text
xbox-360-wired
```

## Driver lifecycle

Driver installation is **not** performed on the realtime input path.

Installation/elevation is treated as setup/installer work.

Normal controller startup assumes the driver dependency has already been prepared by the Windows setup flow.

Creating/removing the virtual controller remains a receiver lifecycle action and must not be tied to Wi-Fi/Bluetooth/USB socket reconnects.

## Input mapping

NATSX logical state maps to HIDMaestro as follows:

- buttons -> `HMButton`;
- D-pad -> `HMHat`;
- sticks -> normalized `[0,1]` axes with `0.5` center;
- triggers -> normalized `[0,1]`.

The adapter allocates the axis dictionary once per controller instance and mutates values on subsequent `Submit` calls.

## Rumble

For Xbox/XInput output, HIDMaestro surfaces `HMController.OutputReceived`.

For `HMOutputSource.XInput`, the SDK's current wire form exposes motor bytes in the 5-byte payload:

```text
data[2] = low-frequency motor
data[3] = high-frequency motor
```

The adapter converts those values to the application-level `RumbleState`.

The output callback runs off the UI thread and must remain short/non-blocking.

## Dependency integrity

The bootstrap script verifies SHA-256 before extracting the SDK DLL.

A dependency update requires:

1. explicit version bump;
2. explicit SHA-256 update;
3. adapter build/tests;
4. an ADR update or superseding ADR when API/behavior changes.

## License

HIDMaestro is licensed under the MIT License.

This does not decide the license of NATSX Controller itself.

## Consequences

Positive:

- current Xbox 360-compatible virtual-device path;
- XInput output/rumble available to the receiver;
- backend remains replaceable;
- no deprecated ViGEm dependency is required in the core design.

Costs:

- Windows setup requires the HIDMaestro driver;
- controller creation/setup may require elevation depending on HIDMaestro/Windows lifecycle;
- release packaging must include the dependency and its required license notices.
