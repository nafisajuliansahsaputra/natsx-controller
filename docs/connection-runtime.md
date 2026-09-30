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

## Pre-active lifecycle

Smart Connection consumes transport lifecycle states before a candidate becomes
eligible:

```text
DISCOVERING
  -> CONNECTING
  -> AUTHENTICATING
  -> STABILIZING
  -> READY
```

The transport runtime receives state-change notifications immediately rather
than waiting only for the 25 ms health polling loop.

For Wi-Fi, the trusted control processor publishes `AUTHENTICATING` after a
trusted challenge has been accepted and `STABILIZING` after authenticated
`SESSION_READY` succeeds. The realtime transport reaches `READY` only after
the first authenticated full-state datagram arrives.

Pre-ready lifecycle reports are not added to health-history windows and are not
counted as hard failures. They are also never eligible for authoritative
selection.

A shared `TransportLifecycle` instance may bridge control/authentication and
realtime components so they expose one coherent state.

## Warm candidates

A non-authoritative transport may continue receiving fresh full-state packets.

The runtime caches only its latest state snapshot. It does not forward that standby state to the virtual controller.

This gives Smart Auto a warm candidate for make-before-break handover without creating multiple authoritative writers.

## Authority state

Transport runtime state and packet flow are separate concerns.

A transport becomes `ACTIVE` only when `ControllerTransportRuntime` grants it
controller authority. Receiving fresh packets while acting as a warm standby
must leave it `READY`; packet arrival alone is not permission to become active.

After a committed handover, the new authoritative transport is promoted to
`ACTIVE` and the previous authoritative transport is explicitly demoted to
`READY` while it remains connected as a standby candidate.

Authority-state updates are local and non-blocking. They must not perform
network/Bluetooth/USB I/O or couple socket lifecycle to virtual-controller
lifecycle.

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

## Health windows

The Smart Connection Manager records each transport health report into the
central policy horizons:

- `FastWindow` (500 ms) preserves a short `SUSPECT` memory after warning-level
  instability.
- `NormalWindow` (3 s) caps ordinary selection score with the recent average,
  making recovery progressive rather than trusting one isolated good sample.
- `LongWindow` (10 s) is used as the reliability tie-break when effective
  candidate scores are equal.

Critical/lost evaluation still uses the newest snapshot, so the rolling windows
do not delay emergency failover.

## Failure-history recovery

Hard failures reduce a transport's effective selection score. The penalty uses
the existing failure-count tiers, but the active tier now decays linearly over
the configured `FailurePenaltyWindow` from the latest hard failure.

A new hard failure immediately reapplies the appropriate full tier. If no new
failure occurs, the penalty steadily returns to zero instead of disappearing in
one step at the end of the window. Circuit-breaker eligibility remains a
separate rule.

## Transport adapter rule

Every production transport implements `IControllerTransport` and exposes:

- transport kind;
- runtime state;
- connect/disconnect;
- health snapshot;
- full-state event.

Transport implementations do not decide their own authority.

Authority belongs to the shared runtime and Smart Connection Manager.
