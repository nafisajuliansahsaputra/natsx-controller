# Bluetooth trusted auto-reconnect and warm standby

## Goal

Bluetooth should remain a warm candidate while Wi-Fi is authoritative and
recover automatically when its RFCOMM link disappears.

## Runtime

`BluetoothAutoReconnectRuntime` owns the Android RFCOMM link lifecycle after
an active trusted controller session exists.

It repeatedly:

1. creates a trusted RFCOMM link through `BluetoothRealtimeLinkFactory`;
2. joins the current logical session;
3. adds that link to the shared `RealtimeStateBroadcaster`;
4. waits for authenticated heartbeat traffic;
5. keeps the link attached while heartbeat freshness is healthy;
6. removes/closes a stale link;
7. reconnects using bounded exponential backoff.

## Warm standby

The runtime does not decide transport authority.

While Wi-Fi and Bluetooth are both healthy, both are attached to the same
`RealtimeStateBroadcaster`. One publication therefore creates one global
`RealtimeStateEnvelope` and delivers the same sequence to both transports.

Windows Smart Connection still decides which READY transport is authoritative.

## Reconnect semantics

Reconnect does not create a new `SessionSequence`.

The active `TrustedSessionRegistry` entry is reused by the RFCOMM connector,
so the transport re-joins the current logical controller session rather than
creating a parallel session.

Backoff defaults:

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

A successfully observed heartbeat resets the retry escalation.

## Failure detection

Defaults:

- first heartbeat timeout: 1.5 s;
- heartbeat-loss timeout: 2 s;
- health poll interval: 250 ms.

These Android-side values govern RFCOMM link maintenance. Windows transport
health/authority still uses the centralized Smart Connection policy.
