# Protocol versions

## Current version

```text
Protocol Major: 1
Protocol Minor: 0
```

This document uses the notation `1.0`.

## Major version

Increment the major version when a peer cannot safely parse or correctly interpret the new protocol using the previous contract.

Examples:

- incompatible envelope layout;
- incompatible message framing;
- incompatible field semantics;
- incompatible authentication/session assumptions.

## Minor version

Increment the minor version for backward-compatible additions where an older peer can either ignore or negotiate the new capability safely.

Examples:

- optional capability;
- optional diagnostic field;
- new negotiated output feature.

## Negotiation

During handshake, each peer advertises:

- supported major version(s);
- highest supported minor version for the selected major;
- capability flags.

A session is established only when both peers agree on a compatible contract.

## Incompatible peers

If no compatible major version exists:

1. reject session establishment;
2. report a clear protocol incompatibility state;
3. do not accept realtime controller packets.

## Version location

Version information appears in the control handshake and in enough realtime framing context to prevent accidental interpretation of incompatible data.

Exact binary placement will be frozen in `messages.md`.
