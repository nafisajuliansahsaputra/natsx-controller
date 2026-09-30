# ADR 0004 — First-time pairing with P-256 numeric comparison

**Status:** Accepted  
**Date:** 2026-10-01

## Context

NATSX Controller must pair a phone and PC once, then reconnect automatically without cloud accounts.

First-time pairing happens before a long-term trust key exists, so the reconnect HMAC flow cannot authenticate the first exchange by itself.

The first-pair flow must:

- work locally;
- avoid transmitting a long-term trust key;
- detect a man-in-the-middle when the user compares the displayed code;
- bind trust to the intended Peer IDs;
- use established cryptographic primitives;
- remain transport-independent.

## Decision

First-time pairing uses ephemeral **ECDH P-256** plus a **six-digit numeric comparison**.

### Pairing exchange

The initiator sends:

- initiator Peer ID;
- fresh 32-byte nonce;
- ephemeral P-256 public key.

The responder sends:

- responder Peer ID;
- fresh 32-byte nonce;
- ephemeral P-256 public key.

P-256 public keys use the standard 65-byte uncompressed point representation:

```text
04 || X(32 bytes) || Y(32 bytes)
```

Both peers derive the same 32-byte raw ECDH shared secret.

### Transcript

The pairing transcript is:

```text
ASCII("NATSX-PAIRING-V1") ||
PAIRING_OFFER payload ||
PAIRING_RESPONSE payload
```

Both peers compute:

```text
transcriptHash = SHA-256(transcript)
```

### Numeric comparison

Both sides derive:

```text
sasKey = HKDF-SHA-256(
    IKM  = sharedSecret,
    salt = transcriptHash,
    info = ASCII("NATSX-PAIRING-SAS-V1"),
    L    = 32
)
```

The first four bytes are interpreted as an unsigned big-endian integer and reduced modulo 1,000,000.

The result is shown as a zero-padded six-digit code on both Android and Windows.

Example:

```text
694336
```

The user confirms pairing only when both screens show the same code.

A mismatch aborts pairing.

### Trust key

The long-term 32-byte trust key is:

```text
trustKey = HKDF-SHA-256(
    IKM  = sharedSecret,
    salt = transcriptHash,
    info = ASCII("NATSX-PAIRING-TRUST-V1"),
    L    = 32
)
```

### Confirmation

After the user confirms the code, each peer sends a role-bound proof:

```text
HMAC-SHA-256(
    trustKey,
    ASCII("NATSX-PAIRING-CONFIRM-V1") ||
    transcriptHash ||
    pairingRole
)
```

Trust is persisted only after the expected peer confirmation proof is valid.

### Message types

Protocol v1 reserves:

- 13 — PAIRING_OFFER
- 14 — PAIRING_RESPONSE
- 15 — PAIRING_CONFIRM
- 16 — PAIRING_ABORT

Pairing messages use the zero Session ID because the trusted controller session does not exist yet.

## User flow

Normal first pairing:

1. Windows Receiver enters **Add controller** mode.
2. Android selects the discovered receiver.
3. Both exchange ephemeral pairing material.
4. Both display the same six-digit code.
5. User verifies both codes.
6. User confirms.
7. Confirmation proofs are exchanged.
8. Both peers persist the trust relationship.
9. A normal authenticated controller session is started.
10. Future reconnects use the trusted reconnect flow from ADR 0003.

If either side rejects, times out, or sees a code mismatch, no trust key is saved.

## Security notes

The six-digit code is a short authentication string, not a password from which the trust key is derived.

An active attacker who substitutes ECDH keys causes the peers to derive different comparison codes except for the short-code collision probability.

The long-term trust key and raw ECDH shared secret are never sent over the wire.

Ephemeral private keys must not be persisted.

Long-term trust keys must be stored using platform-protected storage.

## Trust storage requirements

### Android

Use Android Keystore-backed encryption for persisted trust material.

### Windows

Use Windows user-bound protected storage for persisted trust material.

Exact storage adapters are implemented separately from protocol cryptography.

## Consequences

Positive:

- no cloud identity needed;
- no manual IP-based trust;
- trust is bound to actual cryptographic key agreement;
- reconnect keys are provisioned without sending them directly.

Costs:

- first pairing requires user confirmation;
- pairing UX must clearly present the same six-digit code on both devices;
- secure trust storage must be implemented on both platforms.
