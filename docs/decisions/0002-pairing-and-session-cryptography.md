# ADR 0002 — Pairing and session cryptography

**Status:** Accepted  
**Date:** 2026-09-30

## Context

NATSX Controller accepts realtime input over a local network and later over Bluetooth/USB. LAN discovery alone must never authorize a device to control the Windows virtual gamepad.

The product also requires “pair once, reconnect automatically”, so a first-pair bootstrap must establish durable trust and each gameplay connection must derive fresh session credentials.

A short six-digit code is convenient for human confirmation but is not sufficient as the sole cryptographic secret for a serious controller product.

## Decision

### First-pair bootstrap

Windows generates a one-time pairing invitation containing:

- receiver `PeerId`;
- 256-bit random pairing secret;
- expiry time.

Canonical invitation form:

```text
natsx://pair/v1?peer=<32 hex chars>&secret=<base64url secret>&expires=<unix seconds>
```

The intended primary UX is a QR code. A future manual fallback must preserve comparable entropy; a six-digit value may be used only as a visual confirmation, not as the cryptographic root secret.

### Pairing proof

The Android controller proves knowledge of the invitation secret using HMAC-SHA-256 over a domain-separated transcript containing:

- receiver peer ID;
- controller peer ID;
- client nonce;
- invitation expiry.

Proofs are truncated to 128 bits.

The receiver creates a server nonce.

### Long-term trust key

Both peers derive a 256-bit trust key using HKDF-SHA-256 with:

- input key material: pairing secret;
- salt: client nonce + server nonce;
- domain-separated info: receiver/controller peer identities.

The pairing invitation is one-time and the secret is zeroed/discarded after successful use.

The resulting trust key is the credential persisted in platform-protected storage.

### Per-session authentication

Every controller session uses:

- a fresh 128-bit `SessionId`;
- fresh client nonce;
- fresh server nonce;
- HMAC proof using the long-term trust key.

Both peers derive a fresh 256-bit session key with HKDF-SHA-256.

Realtime/control protocol frame authentication uses the session key rather than the long-term trust key.

### Algorithms

Only established primitives are used:

- HMAC-SHA-256;
- HKDF-SHA-256 (RFC 5869 construction);
- cryptographically secure random bytes;
- constant-time proof comparison.

No custom cipher or custom hash construction is introduced.

### Domain separation

Distinct ASCII labels are used for:

- pair request;
- trust-key derivation;
- pair response;
- auth challenge;
- auth response;
- session-key derivation;
- session-ready proof.

This prevents a valid proof from one protocol stage being reused as another type of proof.

### Secret handling

- invitation objects redact secrets from `ToString` / `toString`;
- temporary key material should be zeroed when practical;
- secrets must never be logged;
- secrets must never be committed;
- transport objects do not own long-term trust lifetime;
- secure persistence is implemented separately using platform facilities.

## Consequences

Positive:

- passive LAN discovery cannot authorize control;
- reconnect can be automatic after one secure pair;
- compromise of one session key does not directly reveal another session key;
- Wi-Fi/Bluetooth/USB can share the same trusted logical peer model;
- invitation QR flow can remain local-only.

Costs:

- pairing UX must support QR or another high-entropy out-of-band transfer;
- both platforms need secure trust storage;
- pairing/session state machines must enforce one-time use, expiry, and replay resistance.

## Rejected alternatives

### Six-digit code as the only secret

Rejected because the entropy is too low for the cryptographic root of trust.

### Shared/default application key

Rejected because one leaked application secret would authorize unrelated devices.

### Unauthenticated LAN input

Rejected because any device on the same network could inject controller state.

### Reusing the long-term trust key directly for realtime frames

Rejected because fresh per-session keys provide cleaner key separation and reduce exposure.
