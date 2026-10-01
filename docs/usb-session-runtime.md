# USB trusted session runtime

## Scope

This layer sits above the physical Windows USB backend.

It assumes a bidirectional byte stream already exists between:

- the Android Open Accessory file descriptor; and
- the Windows USB host bulk endpoints.

It does **not** decide how Windows safely acquires those endpoints.

## Session join

USB reuses the active logical controller session.

The secondary join flow is:

```text
Android -> authenticated SESSION_READY
Windows -> authenticated SESSION_READY
Android -> authenticated TRANSPORT_READY(USB_DIRECT)
Windows -> authenticated TRANSPORT_READY(USB_DIRECT)
transport lifecycle -> STABILIZING
```

The incoming Session ID is only a registry lookup hint until HMAC validation
succeeds with the active session key and the expected trusted Peer ID.

USB does not create another long-term trust relationship.

## Realtime path

After join:

```text
UsbStreamFrameCodec
 -> authenticated ProtocolFrame
 -> global sequence validation
 -> bounded latest-state channel
 -> UsbControllerTransport
 -> ControllerTransportRuntime
 -> InputSafetyEngine
 -> stable virtual controller
```

The Windows receiver rejects malformed, wrong-session, unauthenticated,
duplicate, and stale realtime frames.

The Android sender uses latest-state semantics instead of queueing an unbounded
backlog.

## Lifecycle

`UsbControllerTransport` follows the shared transport lifecycle.

```text
AVAILABLE
 -> AUTHENTICATING
 -> STABILIZING
 -> READY
 -> ACTIVE
```

The transport becomes `READY` only after a fresh authenticated full-state frame
has already been published to `ControllerTransportRuntime`.

Authority remains owned by Smart Connection. Packet arrival alone does not
promote USB to `ACTIVE`.

## Health

The USB runtime measures:

- RTT through authenticated heartbeat/ACK;
- jitter;
- realtime silence;
- failed stream/EOF state.

Those metrics are fed into the shared `TransportHealthEvaluator` using USB RTT
bands from `ConnectionPolicy`.

## Smart Auto

When Wi-Fi is healthy and USB becomes READY, USB does not immediately steal
authority.

The candidate must remain healthy for `UsbRecoveryStability`, currently
500 ms in the Competitive policy.

After stabilization, normal preference is:

```text
USB > Wi-Fi > Bluetooth
```

If active USB fails and a warm Wi-Fi/Bluetooth candidate has a newer global
state sequence, emergency failover can switch authority immediately without
recreating the virtual controller.

## Physical backend remains open

This runtime is intentionally independent from the Windows native USB driver
implementation.

The repository currently does **not** claim that the physical Windows USB host
backend is release-ready. In particular, no UsbDk driver installer/runtime is
added by this work.

M9 physical validation still has to prove:

- safe device enumeration/control transfer on supported Windows versions;
- AOA re-enumeration;
- bulk endpoint acquisition;
- charge-only cable classification;
- physical reconnect behavior;
- Ethernet/LAN route independence on a real machine.
