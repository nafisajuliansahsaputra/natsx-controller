# Peer identity and Wi-Fi discovery

## Peer ID

Each installed NATSX Controller endpoint owns a stable **16-byte opaque Peer ID**.

Examples:

- Android controller installation -> Android Peer ID.
- Windows receiver installation -> Windows Peer ID.

The Peer ID:

- is generated randomly on first installation;
- is stored locally;
- remains stable across normal app restarts;
- is **not a secret**;
- is different from the per-session `SessionId`;
- is never sufficient by itself to authorize controller input.

Long-term trust material is stored separately and is used to authenticate a session.

## HELLO payload v1

`HELLO` is also used for local Wi-Fi discovery before an authenticated session exists.

The common protocol header uses:

```text
MessageType = HELLO
Flags       = NONE
SessionId   = Zero
Sequence    = 0
```

Payload length: **24 bytes**.

| Offset | Size | Field |
|---:|---:|---|
| 0 | 1 | Peer role |
| 1 | 1 | Transport capability flags |
| 2 | 16 | Peer ID |
| 18 | 2 | Realtime UDP port, little-endian |
| 20 | 4 | Discovery nonce, little-endian |

### Peer role

| Value | Meaning |
|---:|---|
| 1 | Android controller |
| 2 | Windows receiver |

### Transport capability flags

| Bit | Capability |
|---:|---|
| 0 | Wi-Fi |
| 1 | Bluetooth |
| 2 | USB Direct |
| 3..7 | Reserved |

Unknown/reserved bits must be zero in v1.

### Discovery request

Android broadcasts a HELLO containing:

- Android role;
- Android Peer ID;
- Wi-Fi capability;
- realtime port = 0;
- fresh random discovery nonce.

### Discovery response

Windows responds **unicast** to the request sender with:

- Windows receiver role;
- Windows Peer ID;
- supported transport flags;
- Windows realtime UDP port;
- the exact discovery nonce from the request.

The nonce only correlates request/response. It is not authentication.

## Security rule

Discovery may reveal that a NATSX receiver exists on the LAN. It does not grant control.

After discovery, controller traffic is accepted only after the discovered Peer ID maps to a trusted peer and an authenticated session is established.

An attacker spoofing discovery must therefore be unable to inject authenticated `GAMEPAD_STATE` traffic.
