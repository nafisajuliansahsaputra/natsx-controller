# Authoritative transport runtime

## Purpose

`ControllerTransportRuntime` is the runtime boundary between live transports and the Windows virtual gamepad path.

It exists so Wi-Fi, Bluetooth, and USB can all remain connected or recovering without allowing more than one of them to drive the virtual controller at the same time.

## Data path

```text
transport packet
 -> transport validation
 -> TransportGamepadStateEventArgs
 -> ControllerTransportRuntime
 -> ControllerSession authority + global sequence validation
 -> InputSafetyEngine
 -> IVirtualGamepadBackend
```

## Warm candidates

A non-authoritative transport may continue receiving fresh full-state packets.

The runtime caches only its latest state snapshot. It does not forward that standby state to the virtual controller.

This gives Smart Auto a warm candidate for make-before-break handover without creating multiple authoritative writers.

## Handover

A Smart Connection proposal is committed only when the target transport has a fresh state snapshot.

The candidate must also carry a session-global sequence number newer than the last state accepted from the current authoritative transport.

```text
old transport authoritative
        |
candidate already READY / warm
        |
candidate fresh full-state snapshot
        |
global sequence is newer
        |
session authority switches
        |
candidate state submitted immediately
        |
SmartConnectionManager commit
        |
old transport remains non-authoritative standby
```

A healthy handover does not intentionally submit a neutral frame between transports.

## Global sequence

Android owns a `SessionSequence` per logical controller session.

Every realtime transport must share that same instance.

Example:

```text
Wi-Fi      #1040
Wi-Fi      #1041
Bluetooth  #1042
Bluetooth  #1043
USB        #1044
```

A transport-specific sequence generator is forbidden because a late packet from the old path could otherwise become indistinguishable from a valid new-session state.

The uint32 sequence uses the serial-number comparison already implemented by `SequenceNumber`, including wraparound behavior.

## Safety

If all transports stop producing fresh authoritative state:

- the virtual controller stays present;
- `InputSafetyEngine` reaches the configured neutralization timeout;
- a neutral gamepad state is submitted;
- transport recovery continues independently.

This prevents a lost connection from leaving movement, trigger, or face-button input stuck.

## Evaluation cadence

Competitive policy evaluates transport health every 25 ms.

The actual health thresholds remain centralized in `ConnectionPolicy`; this cadence is not permission to switch every 25 ms. Hysteresis, score margin, recovery stability, cooldown, and circuit breaker still govern normal handover.

## Transport adapter rule

Every production transport implements `IControllerTransport` and exposes:

- transport kind;
- runtime state;
- connect/disconnect;
- health snapshot;
- full-state event.

Transport implementations do not decide their own authority.

Authority belongs to the shared runtime and Smart Connection Manager.
