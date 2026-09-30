# ADR 0001 — Core platform and transport baseline

**Status:** Accepted  
**Date:** 2026-09-30

## Context

NATSX Controller must turn an Android phone into a reliable Xbox 360-compatible Windows controller with three connection methods and automatic failover.

The application is intended to prioritize eFootball input quality and must not become dependent on cloud services.

## Decision

### Platforms

- Android client: Kotlin native.
- Windows receiver: C# on .NET 10.
- Windows desktop UI: WPF.

### Virtual controller

Use a virtual-controller backend behind an application-owned interface.

HIDMaestro is the planned first backend for Xbox 360-compatible virtualization, subject to integration validation.

The rest of the codebase must not depend directly on HIDMaestro-specific types outside the backend adapter.

### Transport model

All transports implement the same logical controller protocol.

Planned transports:

- USB Direct;
- Wi-Fi;
- Bluetooth Classic/RFCOMM.

### USB

USB is a direct controller data path.

It must not require:

- USB tethering;
- ADB;
- Android USB debugging.

The PC may continue using Ethernet/LAN normally for internet access.

### Wi-Fi

Realtime controller state uses local-network UDP semantics with separate control/session responsibilities as required by the final protocol.

No cloud relay is required.

### Bluetooth

Bluetooth Classic RFCOMM is the planned fallback/wireless transport.

Initial OS pairing may require user confirmation. Trusted reconnection afterward should be automatic.

### Connection authority

Windows maintains one stable virtual controller and one authoritative transport at a time.

Transport changes do not normally recreate the virtual device.

### Connection preference

When candidate health is comparable:

```text
USB Direct > Wi-Fi > Bluetooth
```

Health, hysteresis, cooldown, and circuit-breaker rules may keep a lower-priority but healthier transport active.

## Consequences

Positive:

- consistent gamepad semantics across transports;
- LAN internet route remains independent of USB controller traffic;
- failover can occur without changing the game's controller identity;
- no cloud outage can disable local gameplay.

Costs:

- three transport implementations must be maintained;
- USB Direct requires platform-specific engineering;
- Smart Connection Manager must coordinate multiple live candidates safely;
- pairing/security must be implemented locally.

## Rejected approaches

### USB tethering as the primary USB mode

Rejected because controller traffic should not require or alter the PC's network-routing setup.

### ADB as a production dependency

Rejected because it requires developer settings/debug authorization and is inappropriate for normal users.

### Separate controller engines per transport

Rejected because it would create divergent behavior, mappings, and failure modes.

### Cloud-mediated controller traffic

Rejected because it adds latency, external dependency, and failure modes without helping local gameplay.
