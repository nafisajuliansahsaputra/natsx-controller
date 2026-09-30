# ADR 0005 — Bluetooth Classic RFCOMM baseline

**Status:** Accepted  
**Date:** 2026-10-01

## Context

NATSX Controller needs a wireless fallback when Wi-Fi becomes unstable or
unavailable.

The Bluetooth transport must preserve the existing product invariants:

- one logical controller protocol across all transports;
- one global realtime sequence;
- one Windows virtual controller;
- trusted automatic reconnect after first setup;
- make-before-break handover when a warm path exists;
- no cloud dependency.

## Decision

Use **Bluetooth Classic / BR-EDR RFCOMM** for the v1 Bluetooth transport.

### Roles

Windows is the RFCOMM service host.

Android is the connecting RFCOMM client.

This matches the existing receiver/controller model: the Windows process stays
available while the phone reconnects automatically.

### Service identity

NATSX Controller owns a dedicated 128-bit RFCOMM service UUID:

```text
65dbf3c2-1b88-4ac8-9a1d-3b7c9f5f6e11
```

The service label is:

```text
NATSX Controller
```

The UUID is protocol infrastructure and must not change casually. A future
incompatible Bluetooth service change requires an ADR update.

### Windows API

Use Windows Runtime Bluetooth/RFCOMM APIs:

- `Windows.Devices.Bluetooth.Rfcomm.RfcommServiceProvider`;
- `Windows.Networking.Sockets.StreamSocketListener`;
- `Windows.Networking.Sockets.StreamSocket`.

The Bluetooth transport project targets:

```text
net10.0-windows10.0.19041.0
```

so the .NET desktop build receives Windows SDK projections for the WinRT
Bluetooth APIs.

Normal advertising should not force the PC radio into discoverable mode.
Discoverability is a first-pair/setup concern.

### Android API

Use Android Bluetooth Classic APIs.

Android 12 / API 31 and newer require runtime Bluetooth permissions used by the
transport:

- `BLUETOOTH_CONNECT`;
- `BLUETOOTH_SCAN`.

Legacy `BLUETOOTH` and `BLUETOOTH_ADMIN` declarations remain scoped to API
30 and older.

The app declares Bluetooth scan as not being used to derive physical location.

### Pairing vs NATSX trust

OS Bluetooth pairing does **not** replace NATSX application trust.

The layers remain separate:

```text
Bluetooth OS pairing / link protection
        |
        v
RFCOMM byte stream
        |
        v
NATSX trusted session authentication
        |
        v
NATSX protocol frames + global sequence
```

An OS-paired device must still prove the NATSX trust/session material before
controller state can become authoritative.

### Framing

RFCOMM is a byte stream.

The same NATSX protocol message semantics used by Wi-Fi will be carried over the
stream. Stream framing, trusted session orchestration, health sampling, and
reconnect behavior are implemented in later M8 slices.

### Windows packaging

The WinRT Bluetooth API surface is available to desktop applications through a
Windows-specific target framework.

Release packaging/capability behavior must still be validated before shipping.
If NATSX Controller is packaged, the required Bluetooth device capability must
be declared in the package manifest. This remains part of release engineering
and real-device validation.

## Consequences

Positive:

- no separate controller semantics for Bluetooth;
- standard OS Bluetooth stack on both platforms;
- Android can automatically reconnect to a persistent Windows service;
- Bluetooth can remain a warm Smart Connection candidate;
- no third-party Bluetooth library is introduced.

Costs:

- Windows transport code becomes Windows-specific;
- first-time OS pairing may require explicit user interaction;
- RFCOMM is stream-oriented, so message framing/backpressure must be handled;
- release packaging and real hardware behavior require Windows validation.
