# Protocol v1 messages

This document freezes the v1 frame layout used by Android and Windows.

## 1. Byte order

All multi-byte integers use **little-endian** byte order.

Do not serialize platform-native `Guid` values directly. Session IDs are exactly 16 opaque wire bytes.

## 2. Common frame

Every v1 frame has:

```text
40-byte fixed header
N-byte payload
4-byte CRC32C
optional 16-byte authentication tag
```

### Header layout

| Offset | Size | Field |
|---:|---:|---|
| 0 | 4 | Magic ASCII `NXC1` |
| 4 | 1 | Protocol major |
| 5 | 1 | Protocol minor |
| 6 | 1 | Message type |
| 7 | 1 | Flags |
| 8 | 2 | Header length, always 40 in v1 |
| 10 | 2 | Payload length |
| 12 | 16 | Session ID |
| 28 | 4 | Global session sequence |
| 32 | 8 | Monotonic timestamp in microseconds |

### Flags

```text
bit 0  AUTHENTICATED
bits 1..7 reserved, must be zero in v1
```

Unknown v1 flag bits must cause the frame to be rejected.

### Trailer

Immediately after the payload:

1. CRC32C, 4 bytes little-endian.
2. If `AUTHENTICATED` is set, a 16-byte authentication tag.

CRC32C uses the Castagnoli polynomial and is calculated over:

```text
header || payload
```

The authentication tag is the first 16 bytes of:

```text
HMAC-SHA-256(sessionKey, header || payload || crc32c)
```

CRC32C detects accidental corruption. It is **not** a substitute for authentication.

Post-authentication controller traffic such as `GAMEPAD_STATE` must use authenticated frames.

The exact trust/pairing mechanism that establishes the 32-byte session key is defined separately; custom cryptography must not be invented for it.

## 3. Session identifier

The Session ID is 16 cryptographically random opaque bytes for the active controller session.

It is not a textual UUID and has no platform-specific byte-order transformation.

A zero session ID is permitted only for pre-session negotiation messages where explicitly allowed.

## 4. Sequence

The realtime sequence is an unsigned 32-bit integer.

It is global to the controller session and does not restart when changing transports.

Comparison uses serial-number arithmetic:

```text
candidate is newer than reference when:
candidate != reference
and
(int32)(candidate - reference) > 0
```

This gives a half-range ordering and supports wrap-around.

A peer must never intentionally advance the sequence by 2^31 or more in one logical step.

## 5. Timestamp

`monotonicTimestampMicros` is an unsigned 64-bit count of microseconds from a local monotonic clock origin.

It is used for measurement and diagnostics.

Receiver safety deadlines are always based on the receiver's own monotonic clock, not on trusting the remote timestamp.

## 6. Message types

| Value | Name |
|---:|---|
| 1 | HELLO |
| 2 | AUTH_CHALLENGE |
| 3 | AUTH_RESPONSE |
| 4 | SESSION_READY |
| 5 | HEARTBEAT |
| 6 | HEARTBEAT_ACK |
| 7 | GAMEPAD_STATE |
| 8 | TRANSPORT_READY |
| 9 | HANDOVER_PREPARE |
| 10 | HANDOVER_COMMIT |
| 11 | RUMBLE |
| 12 | DISCONNECT |

Values 13..255 are currently unassigned.

Unknown message types are rejected by v1 unless a later negotiated minor-version rule explicitly defines otherwise.

---

## 7. GAMEPAD_STATE payload

Payload length is exactly **16 bytes**.

| Offset | Size | Field |
|---:|---:|---|
| 0 | 2 | Button bitset |
| 2 | 1 | D-pad bitset |
| 3 | 1 | Reserved, zero |
| 4 | 2 | LX, signed int16 |
| 6 | 2 | LY, signed int16 |
| 8 | 2 | RX, signed int16 |
| 10 | 2 | RY, signed int16 |
| 12 | 1 | LT, uint8 |
| 13 | 1 | RT, uint8 |
| 14 | 2 | Reserved, zero |

### Button bits

| Bit | Control |
|---:|---|
| 0 | A |
| 1 | B |
| 2 | X |
| 3 | Y |
| 4 | LB |
| 5 | RB |
| 6 | L3 |
| 7 | R3 |
| 8 | Back / View |
| 9 | Start / Menu |
| 10 | Guide / Home |
| 11..15 | Reserved |

Reserved button bits must be zero.

### D-pad bits

| Bit | Direction |
|---:|---|
| 0 | Up |
| 1 | Down |
| 2 | Left |
| 3 | Right |
| 4..7 | Reserved |

Opposite pairs (`Up+Down`, `Left+Right`) are invalid on the wire.

Diagonals such as `Up+Left` are valid.

---

## 8. Control message responsibilities

### HELLO

Advertises:

- application role;
- supported protocol version;
- transport capabilities;
- pairing/trust status.

### AUTH_CHALLENGE / AUTH_RESPONSE

Proves possession of trust material and establishes/binds the current connection to the session.

The exact pairing/session-key establishment design must use established cryptographic primitives and is not frozen by this frame document.

### SESSION_READY

Confirms:

- negotiated version;
- current session ID;
- negotiated capabilities;
- authenticated session readiness.

### HEARTBEAT / HEARTBEAT_ACK

Both messages are authenticated once a trusted session exists.

`HEARTBEAT` uses an empty payload. Its header `monotonicTimestampMicros`
contains the sender's local probe timestamp.

`HEARTBEAT_ACK` has an 8-byte little-endian payload containing the exact
`monotonicTimestampMicros` value from the heartbeat being acknowledged.
The ACK header contains the responder's own local monotonic timestamp.

This allows the heartbeat originator to calculate RTT using only its own
monotonic clock:

```text
RTT = localNowMicros - echoedProbeTimestampMicros
```

No clock synchronization between Android and Windows is required.

For v1 heartbeat/control frames, the common header sequence field may be zero.
The global freshness sequence requirement applies to `GAMEPAD_STATE` and
state synchronization; heartbeat traffic must not make a later controller
state appear stale.

Provides:

- liveness;
- RTT sampling;
- connection-health sampling.

### TRANSPORT_READY

Marks an authenticated secondary transport eligible for stabilization and candidate selection.

### HANDOVER_PREPARE

Coordinates state synchronization toward a candidate transport.

### HANDOVER_COMMIT

Confirms authoritative transport transfer after a valid newer synchronized state has been observed.

### RUMBLE

Carries virtual-controller output toward Android.

### DISCONNECT

Provides graceful shutdown. Absence of this message does not prevent timeout-based failure detection.

---

## 9. Realtime acceptance rules

A `GAMEPAD_STATE` frame is accepted only when:

1. magic and frame lengths are valid;
2. protocol version is compatible;
3. message type is known;
4. CRC32C is valid;
5. authenticated-session traffic has a valid HMAC tag;
6. session ID matches;
7. transport is authenticated;
8. transport is authoritative, or explicitly participating in handover synchronization;
9. sequence is newer than the last accepted sequence;
10. payload reserved bits/bytes and values are valid.

Only then may the state reach the virtual-controller backend.

---

## 10. Queueing rule

Realtime state is replaceable latest-value data.

Implementations must not replay an unbounded backlog of old controller states after a stall.

Reliable control messages and realtime state may use different delivery strategies while sharing the same logical protocol contract.

---

## 11. Canonical v1 authenticated test vector

This vector is shared between Kotlin and C# implementations.

### Inputs

```text
version       = 1.0
message       = GAMEPAD_STATE (7)
flags         = AUTHENTICATED (1)
session bytes = 00112233445566778899aabbccddeeff
sequence      = 0x01020304
timestamp     = 0x0102030405060708 microseconds

buttons       = A | Y | RB | Start = 0x0229
dpad          = Up | Left = 0x05
LX            = -32768
LY            = 32767
RX            = -12345
RY            = 12345
LT            = 17
RT            = 250

session key   = 000102030405060708090a0b0c0d0e0f
                101112131415161718191a1b1c1d1e1f
```

### Payload hex

```text
290205000080ff7fc7cf393011fa0000
```

### CRC32C

```text
0x7cd00b20
wire: 200bd07c
```

### Authentication tag

```text
33ca7384f4d02a0c8dfad9ba21f4d6be
```

### Full frame hex

```text
4e584331010007012800100000112233445566778899aabbccddeeff040302010807060504030201290205000080ff7fc7cf393011fa0000200bd07c33ca7384f4d02a0c8dfad9ba21f4d6be
```

Total frame size: **76 bytes**.

Any conforming v1 implementation must reproduce this vector exactly.
