# ADR 0003 — Trusted reconnect authentication

**Status:** Accepted  
**Date:** 2026-10-01

## Context

After first-time pairing, NATSX Controller must reconnect automatically over Wi-Fi, Bluetooth, or USB without asking the user to pair again.

A cached IP address, Bluetooth endpoint, or USB path is not proof of peer identity.

The reconnect protocol therefore needs to:

- prove possession of previously established trust material;
- bind authentication to a fresh controller session;
- resist replay of an old reconnect exchange;
- derive a fresh per-session key;
- work identically regardless of transport.

First-time pairing is intentionally a separate concern and is not solved by this ADR.

## Decision

Each trusted peer relationship stores a 32-byte long-term trust key.

On reconnect:

1. the challenger generates a new 16-byte Session ID;
2. the challenger generates a fresh 32-byte random nonce;
3. `AUTH_CHALLENGE` identifies challenger and intended target;
4. the responder proves knowledge of the trust key with HMAC-SHA-256 over a domain-separated transcript;
5. the challenger verifies the proof using constant-time comparison;
6. both peers derive a fresh 32-byte session key using HKDF-SHA-256;
7. `SESSION_READY` and later trusted frames use the common authenticated-frame tag with the derived session key.

Domain labels:

```text
NATSX-AUTH-V1
NATSX-SESSION-V1
```

The proof is bound to:

- Session ID;
- fresh challenge;
- challenger Peer ID;
- responder Peer ID.

The HKDF salt is:

```text
challenge || sessionId
```

The HKDF info is:

```text
NATSX-SESSION-V1 || challengerPeerId || responderPeerId
```

## Security properties

This design uses established HMAC-SHA-256 and HKDF-SHA-256 constructions.

An old `AUTH_RESPONSE` cannot authenticate a newly generated session because the proof is bound to the fresh challenge and Session ID.

The long-term trust key is never sent over the wire.

The derived session key is fresh for each challenge/session pair.

## First-time pairing

First-time trust establishment remains a separate milestone.

It must provide authenticated user confirmation before the long-term trust key is persisted.

The eventual pairing implementation must not weaken this reconnect protocol or transmit the trust key in plaintext.

## Consequences

Positive:

- automatic reconnect can prove peer identity;
- the same mechanism works across all three transports;
- session keys rotate;
- reconnect does not depend on cloud identity.

Costs:

- both platforms need protected trust-key storage;
- first-time pairing still needs its own authenticated key-establishment UX;
- session setup requires a short challenge/response exchange before realtime traffic becomes authoritative.
