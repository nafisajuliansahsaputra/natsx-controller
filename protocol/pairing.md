# First pairing protocol v1

First pairing creates the long-term 32-byte pairing root key used by trusted reconnect.

Pairing is intentionally separate from realtime controller traffic.

## Transport

- TCP port **37072**
- one pairing peer per connection
- each message is prefixed by a 4-byte little-endian unsigned payload length
- maximum pairing payload: 2048 bytes

TCP provides reliable ordered delivery. Pairing is not latency sensitive.

## Security model

Pairing uses:

- ephemeral ECDH over NIST P-256 / `secp256r1`;
- X.509 SubjectPublicKeyInfo DER public-key encoding;
- SHA-256 transcript hashing;
- HKDF-SHA-256;
- HMAC-SHA-256 confirmation tags;
- a six-digit Short Authentication String (SAS) that the user must compare on both devices.

No PIN is used as a cryptographic key.

A network attacker performing a man-in-the-middle exchange will cause the two displayed SAS values to differ. The user must only confirm when both screens show the same six digits.

## Common pairing message header

All pairing payloads begin with:

| Offset | Size | Field |
|---:|---:|---|
| 0 | 4 | ASCII `NXP1` |
| 4 | 1 | major = 1 |
| 5 | 1 | minor = 0 |
| 6 | 1 | message type |
| 7 | 1 | reserved zero |

Message types:

| Value | Name |
|---:|---|
| 1 | PAIR_REQUEST |
| 2 | PAIR_RESPONSE |
| 3 | PAIR_CONFIRM |
| 4 | PAIR_COMPLETE |
| 5 | PAIR_CANCEL |

## PAIR_REQUEST / PAIR_RESPONSE

Layout:

| Offset | Size | Field |
|---:|---:|---|
| 8 | 16 | sender Device ID |
| 24 | 2 | public key DER length |
| 26 | 1 | UTF-8 display name length |
| 27 | 1 | reserved zero |
| 28 | N | ephemeral P-256 public key, X.509 SPKI DER |
| 28+N | M | display name UTF-8 |

Constraints:

- public key length 1..512 bytes;
- display name length 0..63 bytes;
- Device ID must be non-zero.

The exact encoded PAIR_REQUEST and PAIR_RESPONSE bytes form the pairing transcript.

## Transcript and root key

```text
transcriptHash =
  SHA-256(pairRequestBytes || pairResponseBytes)

sharedSecret =
  ECDH-P256(localEphemeralPrivateKey, peerEphemeralPublicKey)

pairingRootKey =
  HKDF-SHA-256(
    IKM  = sharedSecret,
    salt = transcriptHash,
    info = ASCII("NATSX-PAIRING-ROOT-V1"),
    L    = 32
  )
```

Ephemeral private keys and raw shared secrets must be cleared/disposed as soon as practical.

## Six-digit SAS

```text
sasHash =
  HMAC-SHA-256(
    pairingRootKey,
    ASCII("NATSX-PAIRING-SAS-V1") || transcriptHash
  )

sasNumber =
  UInt32-BE(sasHash[0..4]) mod 1,000,000

display =
  sasNumber padded to exactly 6 decimal digits
```

Both screens must display the same six digits before either user confirms.

## PAIR_CONFIRM

Fixed 28 bytes:

| Offset | Size | Field |
|---:|---:|---|
| 8 | 1 | role: 1 Android, 2 Windows |
| 9 | 3 | reserved zero |
| 12 | 16 | confirmation tag |

Android tag:

```text
first16(HMAC-SHA-256(
  pairingRootKey,
  ASCII("NATSX-ANDROID-CONFIRM-V1") || transcriptHash
))
```

Windows tag uses `NATSX-WINDOWS-CONFIRM-V1`.

A confirmation is accepted only after the local user also confirms the matching SAS.

## PAIR_COMPLETE

Fixed 24 bytes:

| Offset | Size | Field |
|---:|---:|---|
| 8 | 16 | completion tag |

```text
first16(HMAC-SHA-256(
  pairingRootKey,
  ASCII("NATSX-PAIRING-COMPLETE-V1") || transcriptHash
))
```

Windows stores the Android trust record before sending PAIR_COMPLETE.

Android stores the Windows trust record only after validating PAIR_COMPLETE.

## PAIR_CANCEL

Fixed 12 bytes:

| Offset | Size | Field |
|---:|---:|---|
| 8 | 1 | reason |
| 9 | 3 | reserved zero |

Cancellation or timeout must not create or modify trust records.

## UX requirements

- pairing is explicitly user initiated;
- SAS is displayed prominently on both devices;
- Confirm is disabled until the peer handshake has produced a SAS;
- cancelling on either side aborts without saving trust;
- pairing times out after a bounded period;
- the pairing root key is stored only in platform-protected storage.
