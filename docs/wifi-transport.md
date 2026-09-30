# Wi-Fi production transport

## Purpose

The Wi-Fi transport carries authenticated NATSX controller state over the local network without cloud relay.

This is a production transport, not a disposable prototype.

## Ports

Default ports:

- discovery UDP: `42160`
- realtime/control UDP: `42161`

Ports are configurable at the transport boundary.

## Discovery

Discovery is intentionally not authentication.

Android broadcasts the 8-byte request:

```text
NXCDISC1
```

A receiver responds with:

```text
offset  size  field
0       8     "NXCANN01"
8       1     discovery major version (1)
9       1     discovery minor version (0)
10      2     realtime UDP port, little-endian
12      16    receiver instance ID
28      1     UTF-8 receiver-name length (1..63)
29      N     receiver name
```

The discovered endpoint must still authenticate at the controller-protocol/session layer before it can control the virtual gamepad.

## Realtime state

Android sends complete `GAMEPAD_STATE` frames at the configured input rate.

Initial production default:

```text
120 Hz
```

The transport allows a configured range of 30..240 Hz.

Every realtime state frame:

- uses protocol v1 framing;
- uses the current trusted session ID;
- is marked authenticated;
- carries a truncated HMAC-SHA-256 authentication tag through the shared frame codec;
- increments the controller-session global sequence;
- carries the complete current gamepad state.

## Heartbeat

After a valid peer endpoint is known, the Windows receiver sends authenticated heartbeat frames approximately every 500 ms.

The Android transport echoes the 8-byte heartbeat nonce in an authenticated `HEARTBEAT_ACK`.

Windows uses this to estimate RTT.

## Health

The Windows transport collects:

- RTT from heartbeat round trips;
- inter-arrival jitter;
- recent sequence-gap packet loss;
- time since the most recent valid state packet.

These metrics feed the existing `TransportHealthEvaluator` and later Smart Connection orchestration.

Recent packet loss uses a short rolling window rather than lifetime loss so recovery can be detected.

## Trust boundary

Discovery packets do not grant input authority.

The production Wi-Fi state socket requires:

- trusted session ID;
- session authentication key;
- authenticated frame flag;
- valid HMAC;
- valid protocol frame;
- valid gamepad payload.

The actual first-pair provisioning and secure persistence of the session key belongs to the pairing/trust milestone.

No repository-wide default secret is permitted.

## Failure behavior

Android continuously sends fresh full state while the transport is active.

Windows rejects malformed, unauthenticated, wrong-session, stale, and duplicate state through the protocol/session layers.

The Windows safety engine remains responsible for neutralizing stale authoritative input when fresh state disappears.

## Next integration step

The transport is wired into runtime orchestration only after:

1. pairing/trust can provide a session ID and key;
2. the Windows receiver creates the Wi-Fi transport with those credentials;
3. Android resolves the trusted receiver through direct endpoint cache or discovery;
4. Smart Connection Manager owns authority selection and handover.
