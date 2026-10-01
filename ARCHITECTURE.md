# ARCHITECTURE.md

## 1. Purpose

This document is the technical architecture source of truth for NATSX Controller.

The product has two runtime applications:

- Android controller application;
- Windows receiver application.

Together they expose one stable Xbox 360-compatible virtual controller to Windows games.

---

## 2. System overview

```text
┌──────────────────────── ANDROID ─────────────────────────┐
│                                                         │
│  Gameplay Touch Surface                                 │
│          │                                              │
│          ▼                                              │
│  Pointer Router / Analog Processing                     │
│          │                                              │
│          ▼                                              │
│      GamepadState                                       │
│          │                                              │
│          ▼                                              │
│    Protocol Engine                                      │
│          │                                              │
│   ┌──────┼───────────┐                                  │
│   │      │           │                                  │
│   ▼      ▼           ▼                                  │
│  USB   Wi-Fi     Bluetooth                              │
│                                                         │
└───┼──────┼───────────┼──────────────────────────────────┘
    │      │           │
    │      │           │
┌───┼──────┼───────────┼──────── WINDOWS ────────────────┐
│   ▼      ▼           ▼                                 │
│               Transport Host                           │
│                    │                                   │
│                    ▼                                   │
│        Smart Connection Manager                        │
│                    │                                   │
│                    ▼                                   │
│          Controller Session                            │
│                    │                                   │
│                    ▼                                   │
│        Input Safety / Watchdog                         │
│                    │                                   │
│                    ▼                                   │
│        Virtual Controller Backend                      │
│                    │                                   │
└────────────────────┼───────────────────────────────────┘
                     ▼
              Windows / eFootball
```

---

## 3. Architectural invariants

These must remain true.

### 3.1 One logical controller session

A phone/PC gameplay session owns one logical `ControllerSession`.

USB, Wi-Fi, and Bluetooth are transport candidates belonging to that session.

### 3.2 One authoritative transport

At any instant, at most one transport is allowed to update the authoritative Windows gamepad state.

Other connections may remain READY/warm as backups.

### 3.3 Stable virtual-device lifecycle

The virtual controller is not tied to a network socket.

Transport transitions should not unplug/replug the Xbox 360-compatible controller from the game's perspective.

### 3.4 Full-state semantics

Realtime messages communicate the current gamepad state.

They are not merely event notifications.

This allows recovery from:

- lost packets;
- duplicate packets;
- transport handover;
- reconnect.

### 3.5 Transport-independent protocol

USB, Wi-Fi, and Bluetooth serialize the same logical protocol.

---

## 4. Technology baseline

### Android

- Kotlin;
- native Android APIs;
- dedicated/custom gameplay touch surface;
- foreground service for active sessions.

### Windows

- C#;
- .NET 10;
- WPF for desktop UI;
- system-tray operation;
- virtual-controller implementation behind `IVirtualGamepadBackend`.

### Virtual controller

Current planned backend: HIDMaestro Xbox 360-compatible profile/integration.

The backend must remain replaceable at the architecture boundary until integration is proven.

---

## 5. Proposed repository layout

```text
/
├── android/
│   ├── app/
│   ├── core/
│   │   ├── gamepad/
│   │   ├── protocol/
│   │   ├── connection/
│   │   ├── pairing/
│   │   └── diagnostics/
│   ├── transport/
│   │   ├── wifi/
│   │   ├── bluetooth/
│   │   └── usb/
│   └── feature/
│       ├── controller/
│       ├── pairing/
│       └── settings/
│
├── windows/
│   ├── src/
│   │   ├── Natsx.Controller.Core/
│   │   ├── Natsx.Controller.Protocol/
│   │   ├── Natsx.Controller.Connection/
│   │   ├── Natsx.Controller.Transport.Wifi/
│   │   ├── Natsx.Controller.Transport.Bluetooth/
│   │   ├── Natsx.Controller.Transport.Usb/
│   │   ├── Natsx.Controller.VirtualGamepad/
│   │   └── Natsx.Controller.Receiver/
│   └── tests/
│
├── protocol/
│   ├── README.md
│   ├── messages.md
│   ├── gamepad-state.md
│   └── versions.md
│
├── docs/
│   ├── decisions/
│   ├── testing/
│   └── release/
│
├── PRD.md
├── AGENTS.md
├── ARCHITECTURE.md
├── TODO.md
├── SKILL.md
└── WORKFLOW.md
```

Exact module names may change, but dependency direction must remain disciplined.

---

## 6. Dependency direction

### Android

```text
UI / Android lifecycle
        │
        ▼
Controller application layer
        │
        ├─────────────► Connection orchestration
        │
        ▼
Gamepad + Protocol core
        │
        ▼
Transport interfaces
        │
        ▼
platform transport implementations
```

Core gamepad logic must not depend on Activities, Fragments, or screen widgets.

### Windows

```text
WPF / Tray UI
      │
      ▼
Receiver application service
      │
      ├── SmartConnectionManager
      ├── ControllerSession
      ├── Diagnostics
      │
      ▼
Core interfaces
      ├── transports
      └── virtual gamepad
```

The WPF window is a view/controller surface, not the runtime engine.

---

## 7. Logical GamepadState

Conceptual representation:

```text
GamepadState
  buttons
  dpad
  leftX
  leftY
  rightX
  rightY
  leftTrigger
  rightTrigger
```

Recommended logical ranges:

```text
leftX/rightX     -32768..32767
leftY/rightY     -32768..32767
leftTrigger      0..255
rightTrigger     0..255
```

Buttons should use a compact bitset in the realtime wire representation.

State should be immutable or snapshot-safe at transport boundaries.

---

## 8. Protocol architecture

Protocol v1 uses two conceptual channels.

### Control channel

Used for:

- hello/handshake;
- version negotiation;
- pairing/authentication;
- capabilities;
- health probes;
- connection state;
- handover coordination;
- rumble/output messages;
- graceful disconnect.

### Realtime channel

Used for:

- current full `GamepadState`;
- global session sequence;
- monotonic send timestamp;
- session identity;
- integrity/validation data as appropriate.

Transport implementations may map these logical channels differently, but semantics remain consistent.

---

## 9. Sequence model

Sequence numbers are global to a controller session.

Example:

```text
Wi-Fi      state #1841
Wi-Fi      state #1842
handover
Bluetooth  state #1843
Bluetooth  state #1844
```

The Windows session tracks `lastAcceptedSequence`.

A realtime packet is rejected when it is:

- from the wrong session;
- malformed;
- duplicate;
- stale;
- from a non-authoritative transport after the authoritative switch.

Sequence wrap behavior must be defined in the protocol specification before implementation is considered complete.

---

## 10. Wi-Fi architecture

### Discovery

Use a local discovery mechanism so the user does not normally type an IP address.

Discovery is not authentication.

### Session traffic

Realtime input uses UDP.

Control/handshake may use a reliable mechanism or a reliability layer appropriate to the final protocol design.

### Requirements

- no cloud relay;
- LAN only;
- trusted peer authentication;
- bounded buffers;
- stale packet rejection;
- health sampling.

Wi-Fi reconnection should first try the last known trusted endpoint before broader discovery.

---

## 11. Bluetooth architecture

Primary plan:

- Bluetooth Classic;
- RFCOMM;
- trusted-device automatic reconnect after initial OS pairing.

Bluetooth is a peer transport, not a different controller implementation.

Bluetooth may remain warm/READY while Wi-Fi is authoritative in Competitive mode.

---

## 12. USB architecture

USB production requirements:

- direct data transport;
- no tethering requirement;
- no ADB requirement;
- no dependency on laptop network routing.

Target conceptual flow:

```text
Android controller
 -> Android USB accessory/data layer
 -> USB cable
 -> Windows USB transport
 -> protocol/session layer
```

The Android data path is frozen on Android Open Accessory v1 generic accessory
mode. Windows is the physical USB host and Android is the accessory/device.

Production USB Direct must use the accessory-only AOA identity; ADB/debugging
and USB tethering are not part of the transport.

Windows AOA bootstrap remains an explicit driver prototype gate. Plain WinUSB
cannot be assumed to access a phone that is still owned by its normal MTP /
composite driver stack. The accepted architecture therefore requires a narrowly
scoped NATSX bootstrap component rather than a system-wide generic USB capture
filter. See `docs/decisions/0004-usb-direct-aoa.md`.

After AOA re-enumeration, USB carries the same authenticated logical NATSX
session and global controller sequence as the wireless transports.

A cable becoming physically present does not immediately make USB authoritative.

USB must complete handshake and stabilization first.

---

## 13. Transport abstraction

Conceptual interface:

```text
IControllerTransport
  kind
  state
  connect()
  disconnect()
  sendControl(...)
  sendRealtime(...)
  receive(...)
  healthSnapshot()
```

Exact language-specific methods may differ.

Transport implementations must expose events/state to the connection manager; they do not independently decide to become authoritative.

---

## 14. Smart Connection Manager

The Windows side is the final authority for which inbound transport is allowed to drive the virtual controller.

The Android side also tracks connectivity and maintains candidates, but conflicting decisions must resolve deterministically around the established session/authority protocol.

### Global states

```text
DISCONNECTED
DISCOVERING
CONNECTING
AUTHENTICATING
STABILIZING
READY
ACTIVE
SUSPECT
DEGRADED
HANDOVER
RECOVERING
COOLDOWN
```

### Per-transport states

```text
UNAVAILABLE
AVAILABLE
CONNECTING
AUTHENTICATING
STABILIZING
READY
ACTIVE
DEGRADED
FAILED
COOLDOWN
```

Pre-active lifecycle semantics are explicit:

- `CONNECTING` — the transport endpoint/socket/link is being opened;
- `AUTHENTICATING` — trusted peer proof/session authentication is in progress;
- `STABILIZING` — authentication is complete, but the transport has not yet
  demonstrated fresh realtime controller state;
- `READY` — at least one fresh authenticated realtime state has arrived and
  the candidate may participate in normal selection.

Pre-ready states are never eligible to become authoritative and do not count as
hard transport failures merely because realtime silence is still infinite.

### Default preference

```text
USB > Wi-Fi > Bluetooth
```

Preference is applied only after health and stability rules.

---

## 15. Connection policy defaults

All thresholds belong to a centralized `ConnectionPolicy`.

### Health windows

```text
Fast    500 ms
Normal  3 s
Long    10 s
```

The windows have distinct responsibilities:

- **Fast** keeps a recently unstable active transport in `SUSPECT` briefly
  after its latest sample recovers, without delaying hard lost/critical
  detection.
- **Normal** caps the score used for ordinary candidate selection to the recent
  average, so a transport cannot jump immediately from poor history to a
  perfect takeover score after one good sample.
- **Long** acts as a reliability tie-break when candidates have the same
  effective selection score.

Emergency/lost decisions still use the newest transport snapshot directly.

### Silence thresholds

```text
<= 25 ms      normal
25..50 ms     suspect
50..80 ms     degraded
80..120 ms    critical / failover preparation
> 120 ms      transport lost
>= 150 ms     neutral-state safety action if no fresh authority
```

### Wi-Fi RTT

```text
<= 8 ms       excellent
<= 15 ms      good
<= 30 ms      warning
<= 60 ms      degraded
> 60 ms       critical
```

### Bluetooth RTT

```text
<= 15 ms      excellent
<= 25 ms      good
<= 40 ms      warning
<= 70 ms      degraded
> 70 ms       critical
```

### USB RTT

```text
<= 4 ms       excellent
<= 8 ms       good
<= 15 ms      warning
<= 30 ms      degraded
> 30 ms       critical
```

### Jitter

```text
< 3 ms        excellent
3..6 ms       good
6..12 ms      warning
12..25 ms     degraded
> 25 ms       critical
```

### Wi-Fi packet loss

```text
< 0.25%       excellent
0.25..1%      good
1..3%         warning
3..8%         degraded
> 8%          critical
> 15%         emergency
```

---

## 16. Health score

Initial conceptual score is normalized to 0..100.

Inputs:

- latency;
- jitter;
- loss/error rate;
- stability/failure history.

Preference bonuses may influence tie-breaking, but scoring must not create frequent oscillation.

Normal switch requires approximately:

```text
candidateScore >= activeScore + 15
```

and sustained candidate health.

USB is allowed a shorter preference takeover after its own stabilization.

---

## 17. Hysteresis and cooldown

Recovery stability defaults:

```text
USB                  0.5 s
Wi-Fi after degrade  3 s
Wi-Fi after failure  5 s
Bluetooth            3 s
```

Post-switch cooldown:

```text
normal switch        3 s
failure switch       5 s
repeated failure    10 s
```

A critical/lost active transport may override cooldown.

---

## 18. Circuit breaker

Three hard failures within approximately 20 seconds open a circuit for that transport.

Backoff:

```text
15 s
30 s
60 s maximum
```

The transport may still perform low-impact monitoring/recovery checks while not eligible for normal takeover.

Failure history also contributes a score penalty independently from the circuit
breaker. The current penalty tier is based on the number of hard failures still
inside the 30-second failure-history window, then decays linearly toward zero
from the most recent hard failure. A new hard failure recomputes the tier and
restarts that decay period. This avoids both immediate trust restoration and an
abrupt score jump at the end of the window.

---

## 19. Handover algorithm

Preferred behavior is make-before-break.

```text
ACTIVE transport
      │
      ├── candidate discovered
      ▼
candidate CONNECTING
      ▼
candidate AUTHENTICATING
      ▼
candidate STABILIZING
      ▼
candidate READY
      ▼
copy/snapshot latest GamepadState
      ▼
send candidate state with next global sequence
      ▼
receiver validates fresh candidate state
      ▼
atomic authoritativeTransport switch
      ▼
old transport demoted to READY/STANDBY
      ▼
COOLDOWN
```

Do not intentionally insert a neutral state between healthy transport handovers.

---

## 20. Hard disconnect behavior

When the active transport disappears:

1. immediately evaluate READY backups;
2. choose the best usable candidate;
3. fail over without waiting for normal preference hysteresis;
4. keep virtual controller alive;
5. if no fresh authoritative state is recovered before safety timeout, write neutral state;
6. continue automatic reconnect in background.

---

## 21. Input safety architecture

The Windows receiver owns `InputSafetyEngine`.

Responsibilities:

- track time since last accepted fresh state;
- reject stale state;
- trigger neutral state;
- prevent non-authoritative transport writes;
- recover cleanly after reconnect.

Safety must not depend on UI visibility.

---

## 22. Android touch architecture

Gameplay touch processing should be one dedicated subsystem.

Conceptual flow:

```text
MotionEvent
 -> PointerRouter
 -> Control ownership
 -> control processor
 -> GamepadStateStore
 -> realtime publication
```

### Pointer ownership

A pointer ID is associated with a control until released/cancelled or explicitly transferred by defined behavior.

### Analog controls

Analog processor handles:

- center;
- radius;
- radial normalization;
- inner deadzone;
- outer clamp;
- response curve;
- light anti-jitter filtering;
- recenter.

### Digital controls

Visible geometry and touch geometry are separate.

Touch hitboxes may be larger than rendered buttons.

---

## 23. eFootball profile

The first gameplay profile should preserve the established layout:

- large fixed left stick;
- LT/LB at upper left;
- RT/RB at upper right;
- large right-side ABXY;
- center Back/Start/L3/R3;
- lower-center D-pad;
- right stick between D-pad and ABXY.

Profile settings are data, not hard-coded drawing constants wherever practical.

---

## 24. Persistence

Local persistence should cover:

- trusted peer identities;
- user controller profiles;
- calibration;
- haptic preference;
- connection policy selection;
- last known trusted endpoint;
- diagnostics preference.

Secrets must use appropriate platform-protected storage.

Do not store trust secrets in plain repository/config files.

---

## 25. Concurrency model

### Android

Separate concerns:

- UI/touch thread receives events;
- state publication is lightweight;
- transport I/O runs off UI thread;
- connection health runs independently;
- haptics/output processing must not block input.

### Windows

Separate concerns:

- WPF dispatcher is presentation only;
- transport readers run asynchronously;
- accepted state flows through a bounded/latest-state path;
- virtual-controller writes are serialized deterministically;
- diagnostics aggregate asynchronously.

Avoid per-packet task creation if it creates avoidable allocation/scheduling overhead.

---

## 26. Queueing model

Realtime state is latest-value data.

Prefer:

- bounded channels;
- overwrite/latest semantics where safe;
- stale-sequence rejection.

Avoid:

- unbounded queues;
- replaying seconds of old controller input after a stall.

Control messages requiring reliability must use a separate strategy from realtime state.

---

## 27. Diagnostics architecture

Collect metrics without coupling them to gameplay.

Useful metrics:

- active transport;
- candidate states;
- RTT;
- jitter;
- packet loss/error rate;
- input/state rate;
- accepted/rejected packets;
- reconnect count;
- handover reason;
- watchdog neutralizations;
- virtual backend status.

Normal mode should aggregate rather than log every packet.

---

## 28. Pairing and authentication

Pairing establishes trust once.

Normal reconnect should authenticate using stored trust material.

Requirements:

- discovery cannot grant control;
- stale sessions cannot inject state;
- session identifiers rotate appropriately;
- trust material never appears in logs;
- initial Bluetooth OS pairing is allowed to require user confirmation.

Detailed cryptographic protocol is an implementation decision that must be documented before shipping.

---

## 29. Failure domains

A failure in one subsystem should not unnecessarily reset others.

Examples:

- Wi-Fi failure does not destroy virtual gamepad.
- WPF window crash/close should not silently corrupt input state.
- diagnostics failure must not stop gameplay.
- Bluetooth discovery failure must not affect active USB.
- rumble failure must not stop input.

---

## 30. Virtual backend boundary

Conceptual interface:

```text
IVirtualGamepadBackend
  start()
  stop()
  submit(GamepadState)
  setOutputHandler(...)
  status
```

HIDMaestro-specific types must stay behind this boundary.

This preserves the option to change backend without rewriting protocol/connection logic.

---

## 31. Testing architecture

At minimum provide:

### Unit tests

- analog mapping;
- deadzone;
- gamepad serialization;
- protocol validation;
- sequence arithmetic;
- health score;
- hysteresis;
- cooldown;
- circuit breaker;
- watchdog.

### Integration tests

- Android/Windows protocol compatibility;
- Wi-Fi reconnect;
- transport handover;
- virtual backend state;
- multi-touch combinations.

### Soak tests

Long sessions should exercise:

- packet loss;
- Wi-Fi toggles;
- Bluetooth recovery;
- USB reconnect;
- app background/foreground;
- repeated handovers;
- no stuck state.

---

## 32. Architecture decision records

Significant changes should add a small ADR under:

`docs/decisions/`

Use an ADR when changing:

- protocol framing;
- transport technology;
- virtual-controller backend;
- trust/authentication model;
- public module boundaries;
- major state-machine behavior.

---

## 33. Known open technical decisions

These are not excuses to block unrelated work.

- exact v1 binary frame format;
- exact integrity/authentication primitives;
- exact Windows AOA bootstrap driver implementation;
- final HIDMaestro integration wrapper;
- final discovery mechanism;
- exact state rate adaptation policy;
- exact rumble capability mapping.

Resolve them in order of milestone dependency and record the decision.
