# ADR 0004 — Explicit pre-active transport lifecycle

**Status:** Accepted  
**Date:** 2026-10-01

## Context

Smart Connection already modeled manager states for connecting,
authentication, and stabilization, but transport implementations did not expose
those phases coherently.

A transport that has only opened a socket, completed trust authentication, or
is still waiting for its first fresh realtime state must not be treated as a
normal READY candidate.

Polling alone is also insufficient for short lifecycle phases because
`ConnectAsync` may move through them between 25 ms evaluation ticks.

## Decision

Add explicit per-transport runtime states:

```text
CONNECTING
AUTHENTICATING
STABILIZING
READY
```

A shared `TransportLifecycle` may be used by the control/authentication and
realtime halves of one transport.

Transport adapters publish state changes through
`IControllerTransport.StateChanged`. `ControllerTransportRuntime` feeds those
changes into Smart Connection immediately while serializing manager access with
normal health evaluation.

### Eligibility

Only:

- READY;
- ACTIVE;
- DEGRADED

may participate in authoritative transport selection.

CONNECTING, AUTHENTICATING, and STABILIZING are never authoritative candidates.

### Wi-Fi mapping

Wi-Fi uses the lifecycle as follows:

1. local realtime transport setup enters CONNECTING;
2. accepted trusted reconnect challenge enters AUTHENTICATING;
3. authenticated SESSION_READY enters STABILIZING;
4. first authenticated full-state realtime datagram enters READY;
5. ControllerTransportRuntime may later promote READY to ACTIVE;
6. handover demotes the previous ACTIVE transport back to READY.

### Health history

Pre-ready lifecycle snapshots are not inserted into rolling health windows.

A missing realtime packet while still CONNECTING/AUTHENTICATING/STABILIZING is
not itself recorded as a hard failure. This prevents expected startup silence
from poisoning recovery score or opening the circuit breaker.

## Consequences

Positive:

- manager lifecycle reflects actual transport progress;
- READY means the transport has demonstrated usable realtime data;
- short lifecycle phases can be observed without waiting for polling;
- startup silence does not look like packet loss/failure;
- the same contract can be reused by Bluetooth and USB.

Costs:

- transport adapters must expose state-change events;
- connection-manager access must be serialized because lifecycle changes may
  originate from transport I/O threads;
- control and realtime components must share the same lifecycle instance when
  they belong to one logical transport.
