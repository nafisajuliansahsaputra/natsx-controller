# Stream transport envelope v1

Bluetooth RFCOMM and USB Direct are byte streams/bulk pipes rather than UDP datagrams. They carry the exact same NXC1 protocol frames by adding a small transport-only length prefix.

## Envelope

```text
2-byte little-endian frameLength
frameLength bytes of one complete NXC1 frame
```

`frameLength` excludes the 2-byte prefix.

Valid v1 frame length:

- minimum: 44 bytes (40-byte header + 4-byte CRC);
- maximum: 4156 bytes (40-byte header + 4096-byte payload + 4-byte CRC + 16-byte HMAC).

The receiver must read exactly the announced frame length before decoding the NXC1 frame.

## Rules

- zero length is invalid;
- lengths outside the v1 frame bounds are rejected before allocation;
- EOF in the middle of a prefix/frame is a transport failure;
- the stream envelope is not authentication;
- protocol CRC/HMAC rules remain unchanged;
- one transport may not concatenate or split logical frames without preserving this envelope;
- realtime input still uses latest-state semantics at the application layer and must not build an unbounded queue.

This framing is shared by Bluetooth RFCOMM and USB Direct so protocol/session logic does not change between transports.
