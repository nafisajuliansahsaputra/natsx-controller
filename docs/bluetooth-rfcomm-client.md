# Android Bluetooth RFCOMM client

## Purpose

The Android controller now has a production RFCOMM connector for the NATSX
Bluetooth service UUID:

```text
65dbf3c2-1b88-4ac8-9a1d-3b7c9f5f6e11
```

The connector opens an Android `BluetoothSocket`, joins the already-active
logical controller session using `BluetoothSecondarySessionJoinClient`, then
constructs a `BluetoothRealtimeSender` over the same bidirectional RFCOMM
stream.

## Trust boundary

Opening an RFCOMM socket is not authentication.

The link becomes usable only after:

1. the current receiver entry exists in `TrustedSessionRegistry`;
2. authenticated `SESSION_READY` succeeds with the current Session ID/key;
3. both peers confirm `TRANSPORT_READY(Bluetooth)`.

Only then is a realtime sink returned.

## Realtime behavior

The returned `BluetoothRfcommRealtimeLink` implements
`RealtimeStateSink`, so it may be added directly to
`RealtimeStateBroadcaster`.

This preserves the existing rule that Wi-Fi and Bluetooth receive the exact
same global `RealtimeStateEnvelope` sequence while both transports are warm.

The sender retains latest-state queueing and heartbeat ACK behavior already
implemented by `BluetoothRealtimeSender`.

## Resource ownership

The returned link owns:

- the RFCOMM socket;
- the Bluetooth trusted-session wrapper created for the secondary join;
- the realtime sender.

Closing the link closes all three exactly once.

## Connection prerequisites

`BluetoothRfcommConnector.forDevice(...)` requires:

- Bluetooth hardware support;
- required Android runtime permissions;
- Bluetooth enabled;
- a selected receiver `BluetoothDevice`;
- an already-active trusted logical session in the registry.

OS pairing / bond acquisition remains a separate user-flow milestone.
