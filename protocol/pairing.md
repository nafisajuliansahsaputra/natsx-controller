# Pairing, trust, and session establishment v1

This document defines the security model for NATSX Controller.

The exact binary payloads for AUTH_CHALLENGE and AUTH_RESPONSE will be frozen alongside cross-language test vectors before the handshake implementation is marked complete.

## 1. Identities

Each installation owns a stable 16-byte opaque Peer ID.

Peer IDs are public identifiers. They are not credentials.

A trusted relationship is keyed by the tuple:

```text
(localPeerId, remotePeerId)
```

## 2. Required primitives

- CSPRNG
- ECDH P-256 / secp256r1
- SHA-256
- HMAC-SHA-256
- HKDF-SHA-256 (RFC 5869)

Wire public keys use the 65-byte uncompressed SEC1 representation:

```text
0x04 || X[32] || Y[32]
```

Coordinates are unsigned big-endian as required by SEC1.

## 3. Canonical transcript encoding

Cryptographic derivation never concatenates ambiguous variable-length text.

Every transcript begins with an ASCII context label followed by fixed-width fields.

Peer ordering is role-defined:

```text
AndroidPeerId || WindowsPeerId
AndroidNonce  || WindowsNonce
AndroidPubKey || WindowsPubKey
```

The Android controller is always first regardless of which network packet arrived first.

## 4. First-pair nonces

Each peer generates an independent 32-byte cryptographically secure nonce.

Nonces must not be reused intentionally.

## 5. ECDH shared secret

Both peers generate an ephemeral P-256 keypair.

After validating the received public key as a valid P-256 point, both derive the ECDH shared secret using platform cryptography.

Ephemeral private keys are never persisted.

## 6. Pairing transcript hash

```text
pairingTranscript =
    ASCII("NATSX-PAIRING-V1") ||
    AndroidPeerId[16] ||
    WindowsPeerId[16] ||
    AndroidNonce[32] ||
    WindowsNonce[32] ||
    AndroidPublicKeySEC1[65] ||
    WindowsPublicKeySEC1[65]

pairingTranscriptHash = SHA256(pairingTranscript)
```

## 7. Pairing key

```text
pairingKey =
    HKDF-SHA256(
        ikm  = ECDH_shared_secret,
        salt = pairingTranscriptHash,
        info = ASCII("NATSX-PAIRING-KEY-V1"),
        len  = 32
    )
```

## 8. Short Authentication String (SAS)

```text
sasMac =
    HMAC-SHA256(
        pairingKey,
        ASCII("NATSX-SAS-V1") || pairingTranscriptHash
    )

sasValue =
    uint32_big_endian(sasMac[0..4]) mod 1_000_000

display =
    sasValue padded to exactly 6 decimal digits
```

Both devices display the six-digit value.

The user must confirm that both values match before trust is persisted.

A mismatch aborts pairing and discards all temporary material.

## 9. Long-term trust secret

After successful SAS confirmation:

```text
trustSecret =
    HKDF-SHA256(
        ikm  = pairingKey,
        salt = pairingTranscriptHash,
        info = ASCII("NATSX-TRUST-SECRET-V1"),
        len  = 32
    )
```

Only `trustSecret` is persisted as secret pairing material.

The pairing key, ECDH private keys, and ECDH shared secret are discarded.

## 10. Trusted reconnect

A reconnect uses:

- trusted Android Peer ID;
- trusted Windows Peer ID;
- fresh Android reconnect nonce (32 bytes);
- fresh Windows reconnect nonce (32 bytes);
- new random 16-byte Session ID.

Canonical reconnect transcript:

```text
reconnectTranscript =
    ASCII("NATSX-RECONNECT-V1") ||
    AndroidPeerId[16] ||
    WindowsPeerId[16] ||
    AndroidNonce[32] ||
    WindowsNonce[32] ||
    SessionId[16]
```

Proof:

```text
proof =
    HMAC-SHA256(
        trustSecret,
        reconnectTranscript
    )
```

Where a payload carries a proof, v1 uses all 32 bytes unless a later frozen payload explicitly defines a safe truncation.

## 11. Session key derivation

```text
reconnectTranscriptHash = SHA256(reconnectTranscript)

sessionKey =
    HKDF-SHA256(
        ikm  = trustSecret,
        salt = reconnectTranscriptHash,
        info = ASCII("NATSX-SESSION-KEY-V1"),
        len  = 32
    )
```

The session key is memory-only.

After derivation, both peers exchange authenticated SESSION_READY frames using the new Session ID and session key.

Realtime GAMEPAD_STATE traffic is not authoritative until session readiness succeeds.

## 12. Secondary transport join

Wi-Fi, Bluetooth, and USB do not establish independent trust relationships.

A secondary transport joins an existing logical session by proving knowledge of the current session key and matching:

- Session ID;
- Android Peer ID;
- Windows Peer ID.

Transport handover does not reset the global controller-state sequence.

## 13. Replay rules

A stale authentication flow must not revive an old session.

Implementations reject:

- unexpected Session IDs;
- reused/expired pending challenge identifiers;
- invalid proofs;
- SESSION_READY frames for inactive negotiation;
- stale realtime sequence values.

## 14. Local storage

### Android

Trust secret encrypted using an Android Keystore AES-256-GCM key.

### Windows

Trust secret protected using current-user Windows DPAPI.

Storage records may contain non-secret metadata in plaintext, including Peer ID and friendly name.

## 15. Forget/reset

“Forget device” must:

1. remove protected trust secret;
2. remove peer metadata;
3. remove cached transport endpoints;
4. terminate active sessions for that peer;
5. require first pairing again before accepting controller state.


## 16. Canonical derivation test vector

This vector verifies cross-language transcript ordering, HKDF, SAS, reconnect proof, and session-key derivation.

Inputs:

```text
Android Peer ID:
000102030405060708090a0b0c0d0e0f

Windows Peer ID:
101112131415161718191a1b1c1d1e1f

Android nonce:
202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f

Windows nonce:
404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f

Android public key:
04 || bytes 60..9f

Windows public key:
04 || bytes a0..df

ECDH test shared secret:
c0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedf

Session ID:
e0e1e2e3e4e5e6e7e8e9eaebecedeeef
```

Expected outputs:

```text
Pairing transcript SHA-256:
ae3a44b49aa7a34878ed64efa63cfb5d3a06b041515fe3fb77a93f14377a6bb0

Pairing key:
8a12e9de1c2df8d5e83d7ab02effffc2d543d52af0da156fd912138dc8c791cb

Six-digit SAS:
432656

Trust secret:
014850a457b18a4a55758249ade00423852dfb44a6c4a9a8bc422a28379ede4f

Reconnect proof:
f7a5e3ff77584a64cd92a239ae2d75293a77c8b81c66a91a54095654976230de

Session key:
a1241e0b7d71918fd9e506d597a0c29a4531b94723064ea84260fb03705ed63a
```

The ECDH shared secret in this vector is injected directly to test key derivation. A separate ECDH test must verify platform P-256 public-key parsing and shared-secret agreement.
