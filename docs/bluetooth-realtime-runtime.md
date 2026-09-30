# Bluetooth authenticated realtime runtime

## Scope

This layer carries already-authenticated NATSX realtime controller state over an
RFCOMM byte stream.

It deliberately does not establish trust by itself.

A Bluetooth stream may be attached to the realtime transport only after the
caller has obtained:

- the active non-zero NATSX Session ID;
- the current 32-byte session key;
- an RFCOMM stream/socket associated with the intended trusted peer.

The M8 trusted reconnect / secondary-transport join flow is responsible for
producing that authenticated material.

## Android send path

```text
ControllerRealtimePublisher
        |
        v
RealtimeStateBroadcaster
        |
        v
BluetoothRealtimeSender
        |
        v
BluetoothRealtimeStreamEncoder
        |
        | full authenticated NXC1 GAMEPAD_STATE
        v
BluetoothStreamFrameCodec
        |
        | uint16 LE length + frame
        v
RFCOMM OutputStream
```

`BluetoothRealtimeSender` uses the same replaceable latest-envelope pattern as
Wi-Fi:

- only the newest pending full-state snapshot is retained;
- it does not allocate a Bluetooth-specific realtime sequence;
- the envelope sequence remains session-global;
- transport close does not destroy the logical trusted session.

## Windows receive path

```text
RFCOMM Stream / StreamSocket.InputStream
        |
        v
BluetoothStreamFrameCodec
        |
        v
BluetoothRealtimeFrameCodec
        |
        | CRC + HMAC + SessionId + GAMEPAD_STATE validation
        v
BluetoothRealtimeStreamReceiver
        |
        | bounded latest-state channel (capacity 1 / DropOldest)
        v
BluetoothControllerTransport
        |
        v
ControllerTransportRuntime
        |
        v
ControllerSession / InputSafetyEngine
        |
        v
virtual Xbox controller
```

The transport lifecycle is:

```text
UNAVAILABLE
   -> AVAILABLE
   -> [authentication occurs outside this layer]
   -> STABILIZING
   -> READY     (first authenticated fresh state)
   -> ACTIVE    (only when Smart Connection grants authority)
```

A non-authoritative Bluetooth transport may remain READY and consume fresh
full-state frames as a warm standby.

## Validation

A realtime frame is accepted only when:

1. RFCOMM framing length is valid;
2. the NATSX frame itself parses;
3. CRC32C is valid;
4. the frame carries the AUTHENTICATED flag;
5. HMAC validates using the current session key;
6. Session ID matches the current logical controller session;
7. message type is GAMEPAD_STATE;
8. payload is a valid full GamepadState;
9. sequence is newer than the last Bluetooth frame observed.

Final cross-transport authority and global freshness are still enforced by
`ControllerTransportRuntime` / `ControllerSession`.

## Failure behavior

- EOF / I/O loss marks the Bluetooth transport FAILED.
- An invalid stream-length prefix is fail-closed because the byte stream can no
  longer be safely resynchronized.
- A bounded, correctly framed but invalid authenticated NATSX frame is rejected
  without being forwarded.
- Disconnect cancels the receive/pump loops and returns the transport to
  UNAVAILABLE.

## Health status

Bluetooth uses authenticated framed heartbeat traffic on the same RFCOMM
session.

Windows sends `HEARTBEAT` frames every 500 ms. Android validates the session
and HMAC, records liveness, and replies with an authenticated
`HEARTBEAT_ACK` that echoes the Windows probe timestamp.

Windows computes RTT only from its own monotonic clock:

```text
RTT = localNowMicros - echoedProbeTimestampMicros
```

Jitter is maintained as a light exponentially weighted deviation of consecutive
RTT samples. RFCOMM is a reliable ordered stream, so Bluetooth packet-loss
percentage is not inferred from application sequence gaps; the health evaluator
uses RTT, jitter, realtime-state silence, and runtime failure state.

Realtime-state silence remains the liveness/safety signal for controller input.
Heartbeat frames measure link quality and do not consume the global gamepad
sequence.

Android serializes realtime frames and heartbeat ACK writes through one output
lock so independent producers cannot interleave bytes on the RFCOMM stream.
