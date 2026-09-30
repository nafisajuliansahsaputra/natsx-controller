# Bluetooth trusted reconnect handshake

## Purpose

Bluetooth RFCOMM reuses the existing NATSX trusted reconnect contract.

OS Bluetooth pairing protects the link layer, but it does not authorize
controller input. The Android controller must still prove possession of the
persisted NATSX trust secret before Bluetooth realtime state can be accepted.

## Flow

```text
Android RFCOMM client                         Windows RFCOMM host
        |                                             |
        | framed AUTH_CHALLENGE                       |
        |-------------------------------------------->|
        |                                             | trust lookup
        |                                             | HMAC proof
        |                                             | derive session key
        | framed AUTH_RESPONSE                        |
        |<--------------------------------------------|
        | verify proof                                |
        | derive same session key                     |
        |                                             |
        | authenticated framed SESSION_READY          |
        |-------------------------------------------->|
        |                                             | validate peer/role/
        |                                             | Bluetooth capability
        | authenticated framed SESSION_READY          |
        |<--------------------------------------------|
        |                                             |
        | transport enters STABILIZING                |
```

The existing protocol primitives are unchanged:

- fresh non-zero Session ID;
- 32-byte random challenge;
- HMAC-SHA-256 proof using the stored trust secret;
- HKDF-SHA-256 session-key derivation;
- authenticated `SESSION_READY`;
- session-global realtime sequence.

RFCOMM adds only the two-byte stream length prefix already frozen by
`docs/bluetooth-stream-framing.md`.

## Lifecycle

The Windows handshake layer may drive the shared transport lifecycle:

```text
CONNECTING
    -> AUTHENTICATING
    -> STABILIZING
```

Handshake completion does **not** mark the transport `READY`.

The first fresh authenticated `GAMEPAD_STATE` received by
`BluetoothRealtimeStreamReceiver` promotes the transport from
`STABILIZING` to `READY`.

Only `ControllerTransportRuntime` may later promote `READY` to `ACTIVE`.

## Security

A handshake is rejected when:

- the target Peer ID is not this Windows receiver;
- the Android Peer ID is not trusted;
- the stored trust secret has an invalid size;
- proof verification fails;
- `SESSION_READY` is not authenticated by the derived session key;
- the peer identity or role changes during the exchange;
- the remote capabilities do not include Bluetooth.

Temporary trust-secret copies, proof material, challenge nonces, and derived
session-key scratch buffers are zeroed when their scope ends.

## Scope

This slice establishes authenticated Bluetooth protocol sessions over an
already-open RFCOMM stream.

It does not yet implement:

- first-time OS Bluetooth pairing UI;
- automatic RFCOMM device/service discovery and reconnect;
- Bluetooth heartbeat RTT/jitter sampling;
- full Smart Connection wiring from the service host.

Those remain separate M8 items.
