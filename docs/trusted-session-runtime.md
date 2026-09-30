# Shared trusted controller session runtime

## Purpose

Wi-Fi, Bluetooth, and USB are transport paths for one logical NATSX controller
session.

They must not independently own unrelated Session IDs or session keys while
participating in the same controller session.

The runtime therefore owns shared trusted-session material outside individual
transport adapters.

## Source of truth

Each trusted remote peer may have one current registry entry:

```text
trusted remote PeerId
        |
        v
TrustedSessionRegistry
        |
        +-- SessionId
        +-- 32-byte session key
```

A successful full trusted reconnect may replace the registry entry.

Transport-specific wrappers such as `WifiTrustedSession` and
`BluetoothTrustedSession` receive defensive copies of this material. Closing
one transport wrapper must not zero or invalidate another transport's copy.

## Full trusted reconnect

A full trusted reconnect is the path that proves the persisted long-term trust
secret through `AUTH_CHALLENGE / AUTH_RESPONSE` and derives a fresh session
key.

After authenticated `SESSION_READY` succeeds:

1. the trusted peer identity is confirmed;
2. the new Session ID/session key are copied into the shared registry;
3. the transport may continue toward `STABILIZING` / `READY`.

Both Wi-Fi and Bluetooth full trusted reconnect implementations publish the
resulting session into the same registry abstraction.

## Secondary transport join

A warm secondary transport must **not** derive an independent controller
session.

Instead it must:

1. locate the active registry entry for the trusted peer;
2. use the same Session ID and current session key;
3. prove possession of that current session material on the new transport;
4. match the trusted Peer IDs;
5. complete `TRANSPORT_READY` / stabilization;
6. preserve the same global `GamepadState` sequence.

The detailed Bluetooth secondary-join choreography is implemented separately,
but it must consume the registry rather than create a new long-term-trust
reconnect session.

## Secret ownership

The registry owns its own defensive copy of session-key bytes.

Rules:

- caller-owned source arrays may be zeroed immediately after `Replace`;
- `Get` returns a new disposable copy;
- replacing an entry zeroes the previous registry-owned key;
- removing/clearing an entry zeroes its key;
- disposing a transport adapter does not dispose the registry entry;
- ending/forgetting the logical trusted session must remove the registry entry.

Android owns one application-scoped `TrustedSessionRegistry` so all connection
runtimes can share the same source of truth.

Windows receiver composition must likewise use one registry instance across
Wi-Fi, Bluetooth, and future USB control paths.

## Security boundary

The registry is memory-only session material. It is not long-term trust
storage.

Long-term trust secrets continue to live in:

- Android Keystore-protected trusted-peer storage;
- Windows DPAPI-protected trusted-peer storage.

A process restart discards active session material and requires a fresh trusted
reconnect before controller traffic is accepted.
