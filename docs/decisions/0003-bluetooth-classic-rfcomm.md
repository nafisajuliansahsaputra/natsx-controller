# ADR 0003 — Bluetooth Classic RFCOMM transport

## Status

Accepted for v1 implementation.

## Context

NATSX Controller needs a resilient wireless fallback when Wi-Fi is unavailable or repeatedly unhealthy. The Bluetooth transport must preserve the same NXC1 protocol/session semantics and must not emulate keyboard input.

Android and Windows both provide Bluetooth Classic RFCOMM APIs. RFCOMM supplies an ordered byte stream, so it can carry the shared stream envelope defined in `protocol/stream-framing.md`.

## Decision

Use **Bluetooth Classic RFCOMM** with a dedicated NATSX service UUID:

`6f0f4f92-8d9d-4f7b-9bf0-1a6c4f6e4e36`

Initial topology:

- Android advertises/hosts the RFCOMM service while controller connectivity is enabled.
- Windows discovers the custom service on OS-paired Bluetooth devices and connects as the RFCOMM client.
- Android remains the logical NXC1 handshake initiator (sends HELLO first).
- Windows remains the NXC1 trust/authentication authority.
- Windows requires an OS-paired/encrypted Bluetooth connection where the platform supports it.
- NXC1 cryptographic authentication is still mandatory; Bluetooth pairing/encryption does not replace protocol authentication.

## Why Android hosts

Android provides a direct RFCOMM server API using a service record and UUID. Hosting there lets the Windows receiver search for exactly the NATSX controller service and avoids requiring the phone to know a Windows Bluetooth MAC address.

## Framing

RFCOMM carries:

```text
uint16 little-endian NXC1 frame length
NXC1 frame
```

No separate Bluetooth-specific gamepad format is introduced.

## Trust

First-time Bluetooth use may require normal OS pairing/confirmation.

After OS pairing:

- the existing NATSX trusted Device ID/root key remains the application identity;
- Windows accepts gameplay only after NXC1 trusted reconnect succeeds;
- an unknown Bluetooth device cannot gain controller authority simply because it can open RFCOMM.

## Smart Auto behavior

Bluetooth can remain READY/warm while Wi-Fi is authoritative.

Policy remains:

```text
USB > Wi-Fi > Bluetooth
```

Bluetooth takeover is allowed when Wi-Fi is lost/critical or remains degraded long enough according to `ConnectionPolicy`.

## Consequences

Advantages:

- works without the local Wi-Fi network;
- ordered reliable stream;
- reuses protocol/authentication/controller logic;
- suitable as a robust fallback transport.

Costs:

- first OS pairing can require user interaction;
- latency is usually higher than good Wi-Fi/USB;
- Windows RFCOMM discovery/consent behavior varies by OS/device and must be validated on physical hardware;
- actual radio behavior cannot be fully validated in CI.

## Sources used for the decision

Microsoft documents `Windows.Devices.Bluetooth.Rfcomm` for both Windows apps and desktop applications, including RFCOMM service discovery and `StreamSocket` connections. Android provides Bluetooth Classic RFCOMM server/client sockets through the platform Bluetooth APIs.
