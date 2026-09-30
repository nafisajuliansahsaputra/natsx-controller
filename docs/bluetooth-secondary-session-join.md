# Bluetooth secondary transport session join

## Purpose

A Bluetooth warm standby must join the logical controller session that is
already active on Wi-Fi (or another transport).

It must not perform a second long-term-trust reconnect that creates an
independent Session ID/session key.

## Prerequisite

Both peers have a matching active `TrustedSessionRegistry` entry for the
trusted relationship:

```text
trusted PeerId
current SessionId
current 32-byte session key
```

The registry entry was created by a successful full trusted reconnect.

## Choreography

Android initiates the RFCOMM secondary join:

```text
Android                                         Windows
   |                                               |
   | authenticated SESSION_READY                   |
   |  current SessionId/current session key        |
   |---------------------------------------------->|
   |                                               | read SessionId hint
   |                                               | registry lookup
   |                                               | verify HMAC
   |                                               | verify Android PeerId
   |                                               |
   | authenticated SESSION_READY                   |
   |<----------------------------------------------|
   | verify Windows PeerId + Bluetooth capability  |
   |                                               |
   | authenticated TRANSPORT_READY(Bluetooth)      |
   |---------------------------------------------->|
   |                                               | verify current session
   |                                               |
   | authenticated TRANSPORT_READY(Bluetooth)      |
   |<----------------------------------------------|
   |                                               |
   |         transport enters STABILIZING          |
```

The first fresh authenticated Bluetooth `GAMEPAD_STATE` may then promote the
transport from `STABILIZING` to `READY`.

Smart Connection remains the only component allowed to promote `READY` to
`ACTIVE`.

## SessionId lookup rule

The Session ID in the first frame header is an **untrusted lookup hint**.

Windows may use it to locate a candidate registry key, but no identity or
controller traffic is trusted until:

1. the frame HMAC verifies with that key;
2. the authenticated `SESSION_READY` Peer ID matches the peer associated with
   the registry entry;
3. the role is Android controller;
4. Bluetooth capability is present.

This avoids treating a Bluetooth MAC address, socket endpoint, or clear header
field as authentication.

## Global sequence

Secondary join does not reset `SessionSequence`.

After the join, Bluetooth receives the same session-global realtime envelopes
as the other warm transports.

A later handover therefore remains:

```text
Wi-Fi      #1841
Wi-Fi      #1842
Bluetooth  #1843
Bluetooth  #1844
```

instead of starting a Bluetooth-specific sequence space.

## Failure behavior

The join fails closed when:

- Session ID is not present in the active registry;
- HMAC validation fails;
- Peer ID/role/capability mismatches;
- either `TRANSPORT_READY` references another transport;
- stream framing is invalid or the RFCOMM stream closes.

A failed join never creates a new trust relationship and never modifies the
active registry entry.

## Scope

This layer freezes and implements the authenticated secondary-session join.

Production RFCOMM discovery/reconnect and service-host orchestration still need
to call this layer before attaching the socket to
`BluetoothControllerTransport`.
