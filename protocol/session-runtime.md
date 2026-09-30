# Wi-Fi trusted session runtime v1

This document fixes the runtime state transition used after a controller and receiver already share a pairing root key.

Initial pairing remains a separate user-confirmed flow. No long-term secret is embedded in the application or repository.

## Windows receiver states

```text
WAIT_HELLO
  -> CHALLENGE_SENT
  -> AUTHENTICATED
  -> ACTIVE
```

Any protocol/authentication failure returns the peer to `WAIT_HELLO` after bounded backoff.

## Android states

```text
IDLE
  -> HELLO_SENT
  -> WINDOWS_HELLO_RECEIVED
  -> PROOF_SENT
  -> ACTIVE
```

## Frame order

```text
Android                    Windows
   | HELLO                    |
   |------------------------->|
   |              HELLO       |
   |<-------------------------|
   |      AUTH_CHALLENGE      |
   |<-------------------------|
   | AUTH_RESPONSE            |
   |------------------------->|
   |          SESSION_READY   |
   |<-------------------------|
   | GAMEPAD_STATE ...        |
   |------------------------->|
```

Rules:

- both HELLO frames use zero Session ID and are unauthenticated;
- AUTH_CHALLENGE is unauthenticated but carries the fresh non-zero Session ID in the frame header;
- AUTH_RESPONSE carries that same Session ID and is unauthenticated because its payload is already the proof;
- SESSION_READY is the first authenticated frame;
- after SESSION_READY, gameplay/control traffic for the session must be authenticated;
- Android must not publish gameplay state before SESSION_READY validates;
- Windows must not grant controller authority before AUTH_RESPONSE validates;
- a new reconnect creates a fresh Session ID and session key without recreating the virtual Xbox controller.

## Trust lookup

Windows resolves the Android Device ID from HELLO against its trusted peer store.

If no trust record exists:

- no gameplay session is created;
- no root key is disclosed;
- the receiver may surface a pairing-required state to the UI.

Android similarly selects a trusted Windows Device ID/root key from its trusted peer store before reconnect.

## Reconnect

The controller keeps the last trusted receiver endpoint as a fast path.

1. Try direct reconnect to the last endpoint.
2. If it does not establish a session within the reconnect deadline, run local discovery.
3. Match discovery results by trusted Windows Device ID, not receiver name/IP.
4. Start a fresh trusted reconnect handshake.
5. Resume full-state publishing immediately after authenticated SESSION_READY.

The virtual controller remains allocated on Windows while transport recovery is in progress. Input safety neutralizes stale held input when the configured silence deadline is reached.
