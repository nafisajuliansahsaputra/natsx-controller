# NATSX Controller Protocol

This directory is the wire-contract source of truth shared by Android and Windows.

Protocol v1 is transport-independent. USB Direct, Wi-Fi, and Bluetooth carry the same logical messages and controller state.

## Design goals

- compact realtime state;
- deterministic parsing;
- global session sequence numbers;
- full-state semantics;
- stale/duplicate rejection;
- safe handover between transports;
- version negotiation;
- explicit trust/session boundaries;
- no dependency on cloud services.

## Logical channels

### Control channel

Used for:

- hello/version negotiation;
- authentication;
- capability exchange;
- health probes;
- handover coordination;
- rumble/output;
- graceful disconnect.

### Realtime channel

Used for the latest complete `GamepadState`.

Realtime packets are replaceable latest-value data. A receiver must not build an unbounded queue of old input.

## Source files

- `versions.md` — protocol version rules.
- `gamepad-state.md` — logical state and ranges.
- `messages.md` — v1 message families and shared envelope.

## Compatibility rule

An implementation must not silently interpret an unknown incompatible protocol version.

If a protocol change alters parsing or semantics incompatibly, the version must change and the rejection/compatibility behavior must be documented.
