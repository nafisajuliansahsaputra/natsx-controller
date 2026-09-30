# ADR 0002 — Pairing and trust cryptography

**Status:** Accepted  
**Date:** 2026-10-01

## Context

NATSX Controller must pair an Android controller with a Windows receiver once, then reconnect automatically over Wi-Fi, Bluetooth, or USB without asking the user to enter a code on every connection.

Discovery and Peer IDs are not authentication. An attacker on the same LAN must not be able to inject controller state by spoofing an IP address or discovery response.

The design also needs to support transport failover while preserving one logical controller session.

## Decision

### Cryptographic primitives

Use only standard, platform-provided primitives:

- ECDH using NIST P-256 / secp256r1 for first-pair ephemeral key agreement;
- SHA-256;
- HMAC-SHA-256;
- HKDF-SHA-256 as defined by RFC 5869;
- cryptographically secure random nonces;
- AES-GCM only for Android local secret-at-rest protection through Android Keystore;
- Windows DPAPI for Windows local secret-at-rest protection.

No custom cipher, custom hash, or custom random generator is permitted.

### First pairing

First pairing uses fresh ephemeral P-256 key pairs on both peers.

The peers exchange:

- stable Peer IDs;
- 32-byte random nonces;
- ephemeral P-256 public keys;
- protocol/capability context.

Both peers derive the same temporary pairing key from ECDH using HKDF-SHA-256.

A six-digit Short Authentication String (SAS) is derived from the pairing transcript and temporary pairing key. Both devices display the same SAS.

Trust is persisted only after the user confirms that the displayed codes match.

The temporary pairing key is not persisted.

### Trusted relationship

Successful pairing derives a 32-byte long-term trust secret scoped to the exact Android Peer ID and Windows Peer ID.

Each side stores:

- remote Peer ID;
- optional human-readable device label;
- transport capability metadata;
- pairing timestamp/version;
- protected 32-byte trust secret.

The trust secret is never logged and never written to plaintext storage.

### Reconnect authentication

A trusted reconnect uses fresh random challenge nonces and the stored trust secret.

The handshake:

1. proves possession of the trust secret using HMAC-SHA-256 over a canonical transcript;
2. derives a fresh 32-byte session key using HKDF-SHA-256;
3. establishes a new random Session ID;
4. exchanges authenticated SESSION_READY frames before realtime input is authoritative.

A new session key is derived for every logical controller session.

### Transport addition / failover

A secondary transport does not create a new trust relationship.

It joins the existing logical session by proving possession of the current session key and matching Session ID / trusted Peer IDs.

This supports Wi-Fi -> Bluetooth -> USB handover without rebuilding the virtual controller.

### Local secret protection

#### Android

Use Android Keystore to hold a non-exportable AES-256-GCM key.

Stored trust secrets are encrypted with AES-GCM. SharedPreferences/database storage contains only:

- ciphertext;
- IV;
- metadata.

The Keystore key material itself is not exportable.

#### Windows

Use Windows DPAPI scoped to the current user.

The trust document stores only DPAPI-protected secret blobs plus non-secret metadata.

### Secret lifetime

- Long-term trust secret: persists until “Forget device”, pairing reset, or unrecoverable protected-storage loss.
- Session key: memory only, zeroed/discarded when the logical session ends.
- Pairing temporary key/ECDH private key: memory only and discarded after pairing completes or fails.

## Security properties

The design is intended to provide:

- protection against unauthenticated LAN input injection;
- replay resistance through fresh nonces, fresh Session IDs, and global sequences;
- detection of first-pair man-in-the-middle attacks when the user correctly compares the SAS;
- per-session key separation;
- protected local storage of long-term trust material.

## Consequences

Positive:

- normal reconnect is automatic;
- no cloud identity service is required;
- transport failover reuses the same trust relationship;
- cached endpoints can be authenticated before reuse.

Costs:

- first pairing requires explicit human verification;
- platform-specific protected storage is required;
- pairing/session transcript formats must stay exactly cross-language compatible.

## Rejected approaches

### Pairing code as the encryption key

Rejected. A six-digit code has insufficient entropy to be a long-term secret.

### Trusting Peer ID or IP address

Rejected. Both are identifiers/routing metadata, not proof of possession.

### Persisting the per-session HMAC key

Rejected. Fresh session keys must be derived for new logical sessions.

### Custom encryption or ad-hoc key stretching

Rejected in favor of standard primitives and platform cryptographic libraries.
