# Protocol v1 security and trusted reconnect

This document defines the v1 trusted-session authentication contract. Initial user pairing UX/key exchange is implemented later under the pairing milestone, but once two devices are paired, reconnect behavior is fixed by this document.

## Persistent identity

Each installation creates:

- a 16-byte cryptographically random Device ID;
- platform-protected local storage for trust material.

The Device ID is not a secret.

## Pairing root key

A successful explicit first pairing establishes a 32-byte random `pairingRootKey` shared by the Android controller and Windows receiver.

Requirements:

- never commit it to source control;
- never log it;
- store it using platform-protected storage;
- removing/forgetting a trusted device deletes its root key.

Do not derive this long-term key from a device name, MAC address, IP address, or PIN alone.

## Trusted reconnect handshake

Windows acts as the reconnect challenger.

1. Android sends HELLO.
2. Windows sends HELLO.
3. Both select compatible protocol/capabilities.
4. Windows generates a fresh 32-byte AUTH_CHALLENGE.
5. A fresh non-zero 16-byte Session ID is created for the controller session.
6. Both derive the same 32-byte session key using HKDF-SHA-256.
7. Android sends AUTH_RESPONSE.
8. Windows verifies it in constant time.
9. Windows sends SESSION_READY in an authenticated frame.
10. Android verifies the SESSION_READY frame HMAC before accepting the session.

## Session-key derivation

Use HKDF-SHA-256:

```text
IKM  = pairingRootKey
salt = authChallenge
info = ASCII("NATSX-CONTROLLER-V1")
       || androidDeviceId
       || windowsDeviceId
       || sessionId
L    = 32 bytes
```

Device IDs are concatenated in the fixed Android-then-Windows order, never lexical/platform order.

## Android proof

Compute:

```text
helloTranscriptHash =
    SHA-256(androidHelloPayload || windowsHelloPayload)

proof =
    HMAC-SHA-256(
        sessionKey,
        ASCII("NATSX-ANDROID-PROOF-V1")
        || helloTranscriptHash
        || authChallenge
        || sessionId
    )
```

AUTH_RESPONSE sends the first 16 proof bytes.

Windows validates with a constant-time comparison.

## Frame authentication

After authentication succeeds, controller/session traffic uses the authenticated-frame flag and the frame HMAC defined in `messages.md`.

The truncated 16-byte HMAC tag is an integrity/authenticity tag; CRC32C remains only an accidental-corruption check.

## Replay protection

A stale captured realtime frame is rejected by:

- fresh Session ID per controller session;
- fresh session key per challenge/session;
- global monotonic sequence validation;
- authoritative transport validation.

AUTH_CHALLENGE values must not intentionally repeat.

## Failed authentication

On proof failure:

- do not create/activate controller authority;
- do not accept realtime state;
- emit a generic authentication failure;
- apply bounded reconnect/backoff behavior;
- never reveal key/proof details in logs.

## Initial pairing

The first pairing flow must explicitly involve the user and establish the random root key over an authenticated pairing process. The UI may use a code/confirmation/QR mechanism, but the code is not itself the long-term secret.

The precise first-pair exchange remains an M10 implementation decision and must be documented before release.
