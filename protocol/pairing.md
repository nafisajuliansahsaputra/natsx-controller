# First pairing and trust provisioning v1

This document defines first-time trust provisioning for NATSX Controller.

Trusted reconnect is already defined in `protocol/messages.md` under
`AUTH_CHALLENGE / AUTH_RESPONSE`. First pairing must not redefine or overload
that established reconnect payload.

The first-pair wire messages themselves are **not frozen yet**. The
cryptographic transcript and key derivation below are frozen so Android and
Windows can build/test the same security foundation before pairing UI and
message orchestration are added.

## 1. Identities

Each installation owns a stable 16-byte opaque Peer ID.

Peer IDs are public identifiers. They are not credentials.

A trusted relationship is scoped to:

```text
AndroidPeerId + WindowsPeerId
```

## 2. Required primitives

Use standard platform cryptography:

- CSPRNG;
- ECDH P-256 / secp256r1;
- SHA-256;
- HMAC-SHA-256;
- HKDF-SHA-256 (RFC 5869).

Wire public keys for first pairing use the 65-byte uncompressed SEC1 form:

```text
0x04 || X[32] || Y[32]
```

Coordinates are unsigned big-endian.

## 3. First-pair nonces

Each peer generates an independent 32-byte cryptographically secure nonce.

Nonces must not be intentionally reused.

## 4. Ephemeral ECDH

Each peer creates a fresh P-256 key pair.

After validating the received P-256 public key, each side derives the same
32-byte raw ECDH shared secret using platform cryptography.

Ephemeral private keys and the raw shared secret are memory-only and are
discarded after pairing completes or fails.

## 5. Canonical pairing transcript

Role order is fixed and never depends on packet arrival order:

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

## 6. Pairing key

```text
pairingKey =
    HKDF-SHA256(
        ikm  = ECDH_shared_secret,
        salt = pairingTranscriptHash,
        info = ASCII("NATSX-PAIRING-KEY-V1"),
        len  = 32
    )
```

The pairing key is temporary and is never persisted.

## 7. Short Authentication String (SAS)

```text
sasMac =
    HMAC-SHA256(
        pairingKey,
        ASCII("NATSX-SAS-V1") ||
        pairingTranscriptHash
    )

sasValue =
    uint32_big_endian(sasMac[0..4]) mod 1_000_000

display =
    sasValue padded to exactly 6 decimal digits
```

Both devices display the six-digit value.

The user must confirm that the two displayed values match. A mismatch aborts
pairing and no trust record is written.

The SAS is a human verification value. It is **not** an encryption key and is
never stored as the trust secret.

## 8. Optional first-pair responder proof primitive

The implementation provides a first-pair response-proof primitive that future
pairing wire orchestration can use:

```text
pairingResponseProof =
    HMAC-SHA256(
        pairingKey,
        ASCII("NATSX-PAIRING-RESPONSE-V1") ||
        pairingTranscriptHash ||
        SessionId
    )
```

This proves possession of the temporary pairing key and binds the response to a
proposed Session ID.

The exact first-pair message carrying this proof remains intentionally
unassigned until the first-pair wire exchange is frozen.

## 9. Long-term trust secret

After successful human SAS confirmation:

```text
trustSecret =
    HKDF-SHA256(
        ikm  = pairingKey,
        salt = pairingTranscriptHash,
        info = ASCII("NATSX-TRUST-SECRET-V1"),
        len  = 32
    )
```

Only this 32-byte trust secret is persisted as secret trust material.

The trust record also stores non-secret metadata such as:

- remote Peer ID;
- optional display name;
- capability metadata;
- pairing version/time.

## 10. Protected local storage

### Android

The trust secret is encrypted with AES-256-GCM using a non-exportable Android
Keystore key.

Persistent preferences contain ciphertext, IV, and non-secret metadata only.

### Windows

The trust secret is protected with Windows DPAPI using
`DataProtectionScope.CurrentUser`.

The trust document contains the DPAPI-protected blob and non-secret metadata.

## 11. Trusted reconnect after pairing

After a trust relationship exists, use the existing reconnect contract in
`protocol/messages.md`:

```text
AUTH_CHALLENGE
    -> AUTH_RESPONSE
    -> derive fresh session key
    -> authenticated SESSION_READY
```

That contract uses:

- a fresh 32-byte challenge nonce;
- a new non-zero Session ID in the common frame header;
- HMAC-SHA-256 proof with the persisted 32-byte trust secret;
- HKDF-SHA-256 to derive a fresh 32-byte session key.

Do **not** create a second reconnect derivation inside the first-pair protocol.

## 12. Secondary transport join

Wi-Fi, Bluetooth, and USB do not establish independent trust relationships.

A secondary transport joins the already authenticated logical session by
proving knowledge of the current session key and matching the Session ID /
trusted Peer identities.

Transport handover therefore does not reset the global controller-state
sequence or recreate the virtual controller.

## 13. Secret lifetime

- P-256 ephemeral private key: memory only.
- Raw ECDH shared secret: memory only.
- Pairing key: memory only.
- Long-term trust secret: protected persistent storage until Forget/Reset.
- Per-session key: memory only for one logical controller session.

## 14. Forget/reset

Forget device must eventually:

1. terminate active sessions for that peer;
2. remove the protected trust secret;
3. remove peer metadata;
4. remove cached transport endpoints;
5. require first pairing again before accepting controller state.

## 15. Canonical first-pair derivation vector

This vector verifies cross-language transcript ordering, HKDF, SAS, trust-secret
derivation, and first-pair response proof.

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

Injected ECDH test shared secret:
c0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedf

Proposed Session ID for response-proof test:
e0e1e2e3e4e5e6e7e8e9eaebecedeeef
```

Expected:

```text
Pairing transcript SHA-256:
ae3a44b49aa7a34878ed64efa63cfb5d3a06b041515fe3fb77a93f14377a6bb0

Pairing key:
8a12e9de1c2df8d5e83d7ab02effffc2d543d52af0da156fd912138dc8c791cb

Pairing response proof:
69168c6fb8ab71e31e4cabd994c045261bbba267427408bfc3ad11a9e445dc6b

Six-digit SAS:
432656

Trust secret:
014850a457b18a4a55758249ade00423852dfb44a6c4a9a8bc422a28379ede4f
```

The ECDH shared secret above is injected only for deterministic derivation
testing. Separate tests verify that platform P-256 implementations derive the
same raw shared secret from generated key pairs.
