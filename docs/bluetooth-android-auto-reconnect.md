# Android Bluetooth automatic reconnect runtime

## Purpose

The Android foreground controller service keeps Bluetooth available as a warm
candidate without resetting the logical controller session or the global
realtime sequence.

This runtime is intentionally separate from Smart Connection authority.
Android publishes the same full-state envelope to every ready/warm transport;
Windows remains the authority that decides which transport may drive the
virtual controller.

## Candidate routing

Bluetooth routing starts from Android devices that are already OS-bonded.

For each candidate, Android attempts the fixed NATSX RFCOMM service UUID:

```text
65dbf3c2-1b88-4ac8-9a1d-3b7c9f5f6e11
```

A Bluetooth device address is only a route identifier. It is not trusted
identity.

Candidates are accepted only after NATSX authentication succeeds.

## Session selection

For the selected trusted Windows Peer ID:

1. if `TrustedSessionRegistry` already contains an active logical session,
   Bluetooth performs the secondary-session join;
2. otherwise Bluetooth performs the full long-term-trust reconnect;
3. successful full reconnect publishes the new session into the shared
   registry;
4. the resulting `BluetoothTrustedSession` is used by
   `BluetoothRealtimeSender`.

This allows Bluetooth to become a warm path for an existing Wi-Fi controller
session without creating a second Session ID.

## Reconnect lifecycle

`BluetoothAutoReconnectRuntime` owns one worker thread and one active
`BluetoothRealtimeLink`.

```text
IDLE
  -> CONNECTING
  -> AWAITING_HEARTBEAT
  -> ACTIVE
       |
       | heartbeat stale / stream closes
       v
   RECONNECTING
       |
       +----> CONNECTING
```

Backoff:

```text
0 ms
250 ms
500 ms
1 s
2 s
4 s
8 s
15 s maximum
```

When a link is attached, it is registered as a sink in the shared
`RealtimeStateBroadcaster`.

When the link becomes stale:

- the sink is removed;
- the link/socket is closed;
- the global `SessionSequence` is not reset;
- reconnect starts automatically.

## Heartbeat rule

A new link must receive its first authenticated heartbeat within 1.5 seconds.

After heartbeat has been observed, a gap greater than 2 seconds causes the link
to be replaced.

These Android reconnect deadlines are link-lifecycle safeguards. Windows
competitive health/failover thresholds remain governed separately by
`ConnectionPolicy`.

## Foreground-service startup

Until the Trusted PC chooser exists, `ControllerService` automatically starts
this runtime only when there is exactly one trusted PC.

- zero trusted PCs -> no Bluetooth reconnect target;
- exactly one -> safe unambiguous target;
- more than one -> do not guess.

The future trusted-device UI may explicitly select the target relationship.

## Current scope boundary

Android automatic RFCOMM reconnect is implemented here.

M8 `trusted auto-reconnect` remains incomplete until the Windows RFCOMM
service-host path automatically authenticates/joins incoming sockets and
attaches them to the stable Bluetooth transport instance.
