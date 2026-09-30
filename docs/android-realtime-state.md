# Android realtime state publication

## Goal

Every ready transport must see the same logical controller revision.

A warm Wi-Fi, Bluetooth, and USB path must **not** each allocate its own sequence number. Doing that would make healthy sequence gaps look like packet loss and would weaken handover ordering.

## Ownership

The Android process owns:

- one `GamepadStateStore`;
- one `SessionSequence`;
- one `RealtimeStateBroadcaster`.

The foreground `ControllerService` owns the scheduling lifecycle through `ControllerRealtimePublisher`.

The Activity only edits the shared `GamepadStateStore` through the touch surface.

## Flow

```text
ControllerSurfaceView
        |
        v
shared GamepadStateStore
        |
        v
ControllerRealtimePublisher
        |
        | creates one revision:
        | sequence + timestamp + full state
        v
RealtimeStateBroadcaster
      /      |       \
     v       v        v
 Wi-Fi   Bluetooth   USB
```

Each sink receives the **same** `RealtimeStateEnvelope` for one emission.

Example:

```text
revision #500
  -> Wi-Fi      #500
  -> Bluetooth  #500
  -> USB        #500

revision #501
  -> Wi-Fi      #501
  -> Bluetooth  #501
  -> USB        #501
```

This preserves both global handover ordering and meaningful per-transport packet-loss measurement.

## Cadence

Default competitive publication is 120 Hz.

The publisher also requests an immediate emission after a real state change so touch input does not need to wait for the next periodic tick.

Supported policy range is 30–240 Hz.

The scheduler is centralized. Individual transports must not invent their own independent gamepad-state keepalive cadence.

Transport-specific control heartbeats remain independent because they do not consume the gamepad realtime sequence.

## Latest-state semantics

The touch store publishes only actual state changes.

Transport sinks may internally collapse pending work to the newest envelope if their socket temporarily cannot keep up.

Old input must never form an unbounded queue.

## Lifecycle

`NatsxControllerApplication` owns session-scoped state.

`ControllerService` starts/stops the realtime publisher.

Closing a transport does not destroy the logical controller state or the trusted session key. This is required for automatic reconnection and handover.

## Safety

Android publication does not replace Windows safety authority.

The Windows receiver still:

- validates authentication;
- validates session identity;
- validates global sequence freshness;
- allows only one authoritative transport to update the virtual controller;
- neutralizes stale authoritative input through `InputSafetyEngine`.
