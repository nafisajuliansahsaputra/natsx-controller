# Protocol v1 messages

This file defines the v1 logical message families. Exact byte offsets are intentionally not considered frozen until the binary-layout section is completed and cross-language fixtures exist.

## 1. Shared concepts

Every established-session message must be attributable to:

- a negotiated protocol version;
- a controller session;
- a trusted peer;
- a message type.

Realtime state additionally carries:

- global session sequence;
- monotonic send timestamp.

## 2. Message families

### HELLO

Purpose:

- identify application role;
- advertise protocol versions;
- advertise transport/capabilities;
- begin negotiation.

### AUTH_CHALLENGE / AUTH_RESPONSE

Purpose:

- prove possession of trusted pairing material;
- bind the current connection to the intended session.

The final cryptographic primitive will be recorded before shipping and must use established platform/cryptographic libraries rather than custom cryptography.

### SESSION_READY

Purpose:

- confirm negotiated version;
- confirm active session identity;
- confirm agreed capabilities.

### HEARTBEAT / HEARTBEAT_ACK

Purpose:

- liveness;
- RTT measurement;
- health sampling.

### GAMEPAD_STATE

Purpose:

- carry the latest complete `GamepadState`.

Required logical fields:

```text
sessionId
sequence
monotonicTimestamp
buttonBitset
dpad
lx
ly
rx
ry
lt
rt
```

### TRANSPORT_READY

Purpose:

- tell the peer a secondary transport is authenticated and eligible to enter stabilization/READY state.

### HANDOVER_PREPARE

Purpose:

- coordinate transfer toward a candidate transport.

### HANDOVER_COMMIT

Purpose:

- establish the selected transport as authoritative after valid synchronized state is observed.

### RUMBLE

Purpose:

- carry virtual-controller output toward Android.

Logical payload:

- low-frequency motor intensity;
- high-frequency motor intensity;
- optional duration/sequence semantics if required by the final backend.

### DISCONNECT

Purpose:

- graceful session/transport shutdown.

A transport disappearing without this message is handled by timeout/failure logic.

## 3. Realtime acceptance rules

A `GAMEPAD_STATE` packet is accepted only when:

1. framing is valid;
2. protocol contract is compatible;
3. session identity matches;
4. peer/transport is authenticated;
5. transport is authoritative, or is explicitly participating in an allowed handover synchronization step;
6. sequence is newer than the last accepted sequence;
7. payload values are valid.

Invalid realtime packets must never update the virtual controller.

## 4. Stale and duplicate packets

The receiver tracks the last accepted global sequence.

Packets that are duplicate or older are discarded.

This is particularly important after handover because a packet from the old transport can arrive late.

## 5. Packet integrity

The final binary frame will define:

- magic/preamble if needed;
- header length;
- payload length;
- message type;
- version;
- session identifier representation;
- sequence width;
- integrity/authentication mechanism.

A simple CRC may detect accidental corruption, but CRC is not authentication. LAN control must still require authenticated session trust.

## 6. Timing

Timeout decisions use local monotonic clocks.

A remote timestamp is useful for diagnostics/measurement but must not be trusted as a wall-clock source for local safety deadlines.

## 7. Realtime queueing

Consumers should prefer latest-state/bounded buffering.

Do not replay a long queue of old input after a scheduling or network stall.

## 8. Binary layout freeze checklist

Before the binary layout is considered frozen:

- [ ] choose byte order;
- [ ] choose fixed header;
- [ ] choose message-type width;
- [ ] choose session ID representation;
- [ ] choose sequence width;
- [ ] define sequence wrap comparison;
- [ ] assign digital button bits;
- [ ] define D-pad wire encoding;
- [ ] define signed integer encoding;
- [ ] define heartbeat fields;
- [ ] define authentication envelope;
- [ ] define integrity mechanism;
- [ ] publish hex test vectors;
- [ ] implement C# parser/serializer;
- [ ] implement Kotlin parser/serializer;
- [ ] verify identical fixtures.
