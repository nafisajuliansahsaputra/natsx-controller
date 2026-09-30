# Bluetooth RFCOMM stream framing

## Purpose

RFCOMM presents an ordered byte stream while the NATSX protocol is defined as
discrete frames.

Bluetooth therefore adds a transport-only framing layer around each complete
NATSX `NXC1` frame.

This framing does not replace protocol CRC, authentication, session identity,
sequence validation, or message semantics.

## Wire format

Each RFCOMM message is:

```text
uint16 frameLengthLittleEndian
frameBytes[frameLength]
```

The 2-byte prefix is not included in `frameLength`.

Allowed frame length:

```text
minimum = 40-byte header + 4-byte CRC
        = 44 bytes

maximum = 40-byte header
        + 4096-byte maximum payload
        + 4-byte CRC
        + 16-byte authentication tag
        = 4156 bytes
```

A length outside `44..4156` is rejected before allocating or reading the
declared frame body.

## Canonical fixture

The existing authenticated `GAMEPAD_STATE` protocol fixture is 76 bytes.

Its Bluetooth stream representation starts with:

```text
4c00
```

because:

```text
0x004c = 76
```

Full fixture:

```text
4c00
4e584331010007012800100000112233445566778899aabbccddeeff
040302010807060504030201290205000080ff7fc7cf393011fa0000
200bd07c33ca7384f4d02a0c8dfad9ba21f4d6be
```

Kotlin and C# tests must reproduce the same bytes.

## Stream behavior

Readers must:

1. read exactly two prefix bytes;
2. decode the unsigned little-endian length;
3. reject an out-of-range length;
4. read exactly the declared body length;
5. pass only the body bytes to `ProtocolFrameCodec`;
6. treat EOF before a complete prefix/body as a broken transport.

The reader must not create an unbounded backlog of realtime controller states.

Once authenticated realtime transport is wired, normal latest-state semantics
still apply after stream frames are decoded.

## Security boundary

The length prefix is not authenticated independently.

Authentication covers the complete NATSX frame body through the existing
protocol HMAC tag.

A framed `GAMEPAD_STATE` must not reach controller authority until the normal
session ID, HMAC, global sequence, transport authority, and payload validation
rules have passed.
