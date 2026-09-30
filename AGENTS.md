# AGENTS.md

This file defines the operating rules for any coding agent, contributor, or automation working in this repository.

The goal is simple: **keep NATSX Controller technically coherent while it grows.**

---

## 1. Read this before changing code

Before implementing anything, read in this order:

1. `PRD.md`
2. `ARCHITECTURE.md`
3. `TODO.md`
4. `WORKFLOW.md`
5. `SKILL.md`
6. the files directly related to the task

Do not implement from a chat fragment alone when the repository documents already define the behavior.

If repository documents conflict, use this priority:

```text
PRD
  -> ARCHITECTURE
  -> WORKFLOW
  -> TODO
  -> SKILL
  -> local implementation notes
```

When a product decision changes, update the relevant documentation in the same change.

---

## 2. Non-negotiable product rules

Do not violate these without an explicit product decision.

### Controller identity

- Windows must expose one stable Xbox 360-compatible virtual controller.
- Changing Wi-Fi/Bluetooth/USB transport must not normally recreate the virtual device.
- Transport lifecycle and virtual-device lifecycle are separate concerns.

### Transport model

All transports carry the same logical controller protocol.

Never create:

- one controller model for Wi-Fi;
- another for Bluetooth;
- another for USB.

The transport layer moves state. It does not own gameplay semantics.

### USB

Production USB must be direct controller data transport.

Do not make USB tethering a requirement.

Do not make ADB or USB debugging a requirement for normal users.

Laptop Ethernet/LAN must remain free to carry normal internet/game traffic.

### Cloud

Core gameplay must have:

- no account dependency;
- no cloud dependency;
- no internet requirement;
- no Firebase/Supabase requirement.

### Input

- Full current gamepad state is authoritative.
- Do not rely only on edge events such as `ButtonDown` / `ButtonUp`.
- Multi-touch controls are independent.
- No UI animation may own or block the realtime input path.
- No network reconnect may leave the controller in a stuck-button state.

### Smart connection

- Reconnect automatically.
- Fail over automatically when a healthier trusted transport is ready.
- Use hysteresis and cooldown.
- Do not flap between transports.
- Emergency failure may override cooldown.
- A recovered transport must prove stability before normal takeover.

---

## 3. Engineering principles

Order of priority:

1. correctness;
2. input safety;
3. consistent latency;
4. resilient connection behavior;
5. ergonomics;
6. maintainability;
7. visual polish.

Prefer simple deterministic code over clever abstractions in realtime paths.

Use abstractions where they protect architectural boundaries, not merely to reduce line count.

---

## 4. Repository boundaries

Target repository structure:

```text
/
├── android/
├── windows/
├── protocol/
├── docs/
├── PRD.md
├── AGENTS.md
├── ARCHITECTURE.md
├── TODO.md
├── SKILL.md
└── WORKFLOW.md
```

The `protocol/` area is the source of truth for wire-level behavior.

Android and Windows may have generated/native representations of the protocol, but they must not silently diverge.

---

## 5. Required core abstractions

Names may evolve, but these responsibilities must remain separate.

### Shared concepts

- `GamepadState`
- `ProtocolMessage`
- `ControllerSession`
- `ConnectionPolicy`
- `TransportHealth`
- `TransportKind`
- `TransportState`

### Android

Equivalent responsibilities:

- Touch surface / pointer router
- Analog processor
- Gamepad state store
- Protocol encoder/decoder
- Wi-Fi transport
- Bluetooth transport
- USB transport
- Smart connection client
- Pairing/trust store
- Haptic/output handler

### Windows

Equivalent responsibilities:

- Transport host
- Protocol encoder/decoder
- Controller session
- Smart Connection Manager
- Input safety/watchdog
- Virtual controller backend
- Pairing/trust store
- Diagnostics
- Receiver UI

Do not allow WPF UI classes to become the controller engine.

Do not allow Android Activity/Compose/View lifecycle code to become the transport engine.

---

## 6. Realtime-path rules

The hot path is:

```text
touch
 -> GamepadState
 -> encode
 -> transport
 -> decode
 -> safety/session
 -> virtual controller
```

Inside that path:

- avoid blocking I/O;
- avoid disk I/O;
- avoid database access;
- avoid network discovery;
- avoid UI-thread dependencies;
- avoid unbounded queues;
- avoid repeated large allocations;
- avoid verbose logging per packet;
- avoid locks held across I/O.

Use bounded/latest-state semantics where appropriate.

Old realtime packets are usually less valuable than the newest valid state.

---

## 7. Gamepad state rules

The logical state must support:

- A/B/X/Y;
- LB/RB;
- L3/R3;
- Back/View;
- Start/Menu;
- Guide where supported;
- D-pad;
- LX/LY;
- RX/RY;
- LT/RT.

Expected logical ranges:

```text
LX, LY, RX, RY : signed 16-bit range
LT, RT         : 0..255
```

Neutral state means:

```text
buttons = released
dpad    = neutral
LX/LY   = centered
RX/RY   = centered
LT/RT   = 0
```

---

## 8. Protocol rules

Protocol versioning starts at v1.

Every implementation must:

- reject unsupported incompatible protocol versions clearly;
- reject invalid session identity;
- reject stale/duplicate realtime sequence numbers;
- use monotonic timing for local duration/timeout decisions;
- keep sequence numbers global per controller session across transports;
- separate realtime state messages from control/handshake messages.

Do not reuse wall-clock timestamps for timeout correctness.

---

## 9. Smart Connection Manager rules

Thresholds live in one policy object/configuration area.

Do not scatter constants such as `80 ms`, `3%`, or `5 sec` across classes.

At minimum track:

- RTT;
- jitter;
- packet loss or equivalent transport health;
- silence since last valid state;
- reconnect/failure history;
- active transport;
- candidate transport;
- cooldown;
- circuit-breaker status.

Normal preference:

```text
USB > Wi-Fi > Bluetooth
```

This is a preference, not permission to replace a currently healthy transport with an unstable candidate.

---

## 10. Safety rules

The Windows receiver owns final stuck-input protection.

If authoritative fresh state is unavailable beyond the configured safety timeout:

1. write neutral state;
2. keep the virtual controller alive;
3. continue reconnect/recovery.

Never preserve a potentially stuck previous state indefinitely.

During handover, use make-before-break where technically possible.

---

## 11. Android implementation guidance

Baseline:

- Kotlin native;
- dedicated gameplay touch surface;
- foreground service during active controller sessions.

The touch layer must use pointer IDs and explicit ownership.

Do not model multi-touch as “left finger” and “right finger”.

A pointer may only control the intended control according to the current ownership/hitbox rules.

Keep rendering and input-state mutation separable so visual effects cannot delay controller state.

---

## 12. Windows implementation guidance

Baseline:

- C#;
- .NET 10;
- WPF UI;
- virtual-controller backend behind an interface;
- HIDMaestro integration currently planned, subject to validation.

The core engine should run without requiring the WPF window to be visible.

System tray/minimized operation is expected.

Administrative/elevated operations must be isolated from everyday realtime operation wherever possible.

---

## 13. Testing expectations

“No prototype” does not mean “no tests.”

Every production feature requires the appropriate level of automated or repeatable verification.

Prioritize tests for:

- protocol encode/decode round trips;
- sequence rejection;
- neutral-state watchdog;
- handover;
- hysteresis;
- cooldown;
- circuit breaker;
- state synchronization;
- reconnect behavior;
- multi-touch ownership;
- analog normalization;
- deadzone/curve behavior.

When fixing a reproducible bug, add a regression test where practical.

---

## 14. Logging rules

Use structured logs.

Never log:

- pairing secrets;
- long-term trust keys;
- sensitive device credentials.

Do not log every 120/240 Hz state packet in normal mode.

Use counters/sampled diagnostics instead.

Debug packet tracing must be explicitly enabled and bounded.

---

## 15. Dependency rules

Before adding a dependency, answer:

1. What capability does it provide?
2. Is it required in the realtime path?
3. Is it actively maintained?
4. Can it introduce a cloud/runtime dependency?
5. What is the license?
6. Can the repository work if this dependency disappears?

Avoid adding frameworks merely for convenience.

---

## 16. Change discipline

A task is not complete merely because it compiles.

Before marking complete:

- code builds;
- relevant tests pass;
- formatting/linting passes;
- no secrets are committed;
- docs are updated if behavior changed;
- TODO is updated when a milestone changes;
- no temporary debug code remains;
- no architecture boundary is bypassed.

---

## 17. Forbidden shortcuts

Do not:

- hard-code the user's PC IP as product behavior;
- hard-code one Android screen size;
- require Wi-Fi for USB;
- implement USB through tethering as the final design;
- expose unauthenticated LAN control;
- recreate the virtual gamepad on every reconnect;
- let multiple transports simultaneously write authoritative state;
- use unbounded packet queues;
- block the UI thread with network/USB reads;
- silently swallow protocol incompatibility;
- add cloud services to “simplify pairing.”

---

## 18. If uncertain

When implementation details are unclear:

1. preserve the architectural boundaries;
2. choose the safest reversible implementation;
3. document the open decision;
4. do not invent a new product requirement.

If a change would alter the user's gameplay behavior, connection policy, protocol contract, or supported platform, treat it as a product/architecture decision rather than a minor refactor.
