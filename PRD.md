# NATSX Controller — Product Requirements Document (PRD)

**Status:** Foundation / pre-implementation  
**Product:** NATSX Controller  
**Platforms:** Android + Windows 10/11  
**Primary use case:** Turn an Android phone into a high-quality Xbox 360-compatible controller for Windows, optimized first for eFootball.  
**Repository:** `nafisajuliansahsaputra/natsx-controller`

---

## 1. Product vision

NATSX Controller is a dedicated phone-to-PC gamepad system. It is not a remote-desktop suite, media remote, keyboard/mouse replacement, or game-streaming product.

The product must make an Android phone behave as a stable, low-latency Xbox 360/XInput-style controller on Windows while preserving a controller layout that is comfortable for long eFootball sessions.

The application must prioritize:

1. input responsiveness;
2. reliable multi-touch;
3. stable virtual-controller identity;
4. automatic reconnection and transport failover;
5. ergonomic touch layout;
6. predictable analog behavior;
7. local-only operation without cloud dependency.

---

## 2. Goals

### 2.1 Primary goals

- Android phone becomes a virtual Xbox 360-compatible controller on Windows.
- Windows games see one persistent virtual controller even when the underlying transport changes.
- Support three connection methods:
  - USB Direct;
  - Wi-Fi;
  - Bluetooth.
- Smart Connection Manager automatically chooses and recovers the best transport.
- eFootball is the first-class gameplay profile.
- Preserve the user's established Monect-inspired layout as the baseline rather than redesigning controls from scratch.
- Support simultaneous multi-touch combinations such as:
  - left stick + RT + X + LB;
  - left stick + face buttons + shoulder buttons;
  - both sticks + other buttons.
- Work without an account, cloud backend, Firebase, Supabase, or internet connection.
- Maintain the laptop's normal Ethernet/LAN internet route while USB is used only as the controller data path.

### 2.2 Quality goals

- No stuck buttons after packet loss or disconnect.
- No manual reconnect required during normal recoverable failures.
- No virtual-controller recreation during Wi-Fi/Bluetooth/USB handover.
- No unnecessary transport flapping.
- No UI animation or background task should block the hot input path.
- Transport changes must preserve the latest authoritative gamepad state where safe.

---

## 3. Non-goals

The first production scope does **not** include:

- remote desktop;
- keyboard remote;
- mouse remote;
- game streaming;
- file transfer;
- media controls;
- presentation remote;
- racing wheel emulation;
- gyro mouse;
- cloud accounts or cloud synchronization.

These may only be considered later if they do not compromise controller quality.

---

## 4. Target users

### Primary user

A Windows gamer who:

- owns an Android phone;
- needs a controller without carrying a physical gamepad;
- values low latency and reliable controls;
- may use Ethernet for PC internet while using USB for controller input;
- frequently plays eFootball or similar games.

### Secondary users

- emulator users;
- casual PC gamers;
- users needing a temporary gamepad;
- users wanting configurable touch layouts.

---

## 5. Core user experience

### First-time setup

1. Install NATSX Controller Receiver on Windows.
2. Install NATSX Controller on Android.
3. Pair the phone and PC once.
4. Approve OS-level Bluetooth pairing if Bluetooth is enabled.
5. The trusted device relationship is persisted locally.
6. The virtual Xbox 360-compatible controller is created on Windows.

### Normal use

1. Open the Android app.
2. The app discovers the trusted PC.
3. Smart Connection Manager evaluates available transports.
4. The best healthy transport becomes active.
5. The controller layout opens.
6. Game input works immediately.

No repeated pairing code or manual IP entry should be required during normal use.

---

## 6. Controller layout requirements

The initial eFootball layout is based on the user's existing Monect custom layout.

### Baseline control placement

- Large left analog stick on the left.
- LT and LB near the upper-left edge.
- RT and RB near the upper-right edge.
- Large A/B/X/Y controls on the right.
- Back, Start, L3, and R3 near the center.
- D-pad in the lower-middle region.
- Right analog stick between the D-pad and face-button region.

The current approved visual proportions and lightweight skin are specified in
`docs/design/gameplay-ui-v1.md`, section 6 (2026-10-02 reference).

### Ergonomic principles

- Preserve established muscle memory.
- Visible button size and touch hitbox size may differ.
- Hitboxes may be enlarged while preventing overlap ambiguity.
- A/B/X/Y placement should follow natural right-thumb travel rather than a rigid visual grid.
- Left stick defaults to fixed-position behavior.
- Right stick may be visually smaller than the left stick but must have a generous interaction area.
- Layout must be scalable for different phone aspect ratios and hand sizes.

---

## 7. Input requirements

### Digital inputs

Support at minimum:

- A
- B
- X
- Y
- LB
- RB
- L3
- R3
- Back/View
- Start/Menu
- Guide/Home where supported
- D-pad Up/Down/Left/Right

### Analog inputs

- Left stick X/Y: signed 16-bit logical range.
- Right stick X/Y: signed 16-bit logical range.
- LT: 0–255.
- RT: 0–255.

### eFootball trigger behavior

The eFootball profile may treat LT/RT as fast digital-style full presses while preserving analog-capable core state for other profiles.

### Analog processing pipeline

```text
raw pointer coordinates
    -> radial normalization
    -> inner deadzone
    -> anti-jitter filtering
    -> response curve
    -> outer clamp
    -> controller logical range
```

Default eFootball behavior:

- low deadzone;
- linear response;
- very light smoothing;
- instant recenter;
- no forced diagonal snapping.

---

## 8. Multi-touch requirements

The touch engine must track pointer IDs independently.

Each control interaction should maintain at least:

- `pointerId`;
- `controlId`;
- initial position;
- current position;
- pressed/active state.

Required behavior:

- multiple controls can be active simultaneously;
- one control releasing must not cancel unrelated pointers;
- Android system gestures should be suppressed appropriately during active gameplay;
- touch hysteresis should prevent accidental release when a finger moves slightly beyond a visible button.

---

## 9. Haptics and rumble

### Touch haptics

Optional user-configurable local feedback:

- Off
- Low
- Medium
- High

Different control classes may use different haptic patterns.

### Game rumble

Target flow:

```text
Game
 -> virtual Xbox output
 -> Windows Receiver
 -> active transport
 -> Android app
 -> phone vibration/haptics
```

Rumble support must not block or delay input delivery.

---

## 10. Transport requirements

All transports carry the same transport-independent gamepad protocol.

```text
Touch Engine
    -> GamepadState
    -> Unified Protocol
       -> USB Direct
       -> Wi-Fi
       -> Bluetooth
    -> Windows Receiver
    -> Virtual Xbox 360-compatible controller
```

### 10.1 USB Direct

Requirements:

- no USB tethering;
- no network adapter required for controller traffic;
- laptop Ethernet/LAN internet remains unchanged;
- no ADB requirement for normal users;
- direct USB accessory/bulk data path;
- USB cable must be validated as a working data connection before takeover.

### 10.2 Wi-Fi

Requirements:

- local network only;
- UDP-based realtime input path;
- automatic discovery;
- trusted-device authentication;
- sequence numbers;
- stale and duplicate packet rejection;
- health telemetry.

### 10.3 Bluetooth

Requirements:

- Bluetooth Classic RFCOMM as the primary planned transport;
- first pairing may require OS confirmation;
- trusted devices reconnect automatically afterward;
- same `GamepadState` and protocol semantics as other transports.

---

## 11. Smart Connection Manager

### 11.1 Core behavior

The Smart Connection Manager must:

- auto-connect;
- auto-reconnect;
- auto-failover;
- maintain a warm standby in Competitive mode;
- use health metrics rather than static priority alone;
- separate transport lifecycle from virtual-controller lifecycle;
- avoid transport flapping using hysteresis and cooldowns.

### 11.2 Default preference

When transports are similarly healthy:

1. USB Direct
2. Wi-Fi
3. Bluetooth

A healthier lower-priority transport may remain active when the preferred transport is unstable.

### 11.3 Global states

- DISCONNECTED
- DISCOVERING
- CONNECTING
- AUTHENTICATING
- STABILIZING
- READY
- ACTIVE
- SUSPECT
- DEGRADED
- HANDOVER
- RECOVERING
- COOLDOWN

### 11.4 Per-transport states

- UNAVAILABLE
- AVAILABLE
- CONNECTING
- READY
- ACTIVE
- DEGRADED
- FAILED
- COOLDOWN

### 11.5 Health windows

- Fast: 500 ms
- Normal: 3 s
- Long: 10 s

### 11.6 Latency thresholds

#### Wi-Fi

- Excellent: <= 8 ms
- Good: 8–15 ms
- Warning: 15–30 ms
- Degraded: 30–60 ms
- Critical: > 60 ms

#### Bluetooth

- Excellent: <= 15 ms
- Good: 15–25 ms
- Warning: 25–40 ms
- Degraded: 40–70 ms
- Critical: > 70 ms

#### USB

- Excellent: <= 4 ms
- Good: 4–8 ms
- Warning: 8–15 ms
- Degraded: 15–30 ms
- Critical: > 30 ms

These are initial policy defaults and must be configurable rather than scattered as hard-coded constants.

### 11.7 Jitter thresholds

- Excellent: < 3 ms
- Good: 3–6 ms
- Warning: 6–12 ms
- Degraded: 12–25 ms
- Critical: > 25 ms

### 11.8 Wi-Fi packet-loss thresholds

- Excellent: < 0.25%
- Good: 0.25–1%
- Warning: 1–3%
- Degraded: 3–8%
- Critical: > 8%
- Emergency: > 15%

### 11.9 Silence / timeout policy

- 0–25 ms: normal
- 25–50 ms: suspect
- 50–80 ms: degraded
- 80–120 ms: failover candidate / critical
- > 120 ms: transport lost
- >= 150 ms without fresh valid authoritative state: neutralize controller input

### 11.10 Hysteresis

Recovery stability requirements:

- USB: 0.5 s
- Wi-Fi after degraded: 3 s
- Wi-Fi after failed: 5 s
- Bluetooth: 3 s

### 11.11 Cooldown

After handover:

- normal switch: 3 s;
- failure-induced switch: 5 s;
- repeated failure: 10 s.

Emergency failures may override cooldown.

### 11.12 Circuit breaker

If a transport hard-fails 3 times within 20 seconds:

- candidate cooldown 15 s;
- repeated failure extends to 30 s;
- maximum 60 s.

Health monitoring/discovery may continue while the circuit is open.

### 11.13 Takeover rules

#### USB takeover

USB may take over when:

- authenticated;
- valid protocol negotiated;
- valid state samples received;
- stable for approximately 250–500 ms;
- no active USB circuit-breaker penalty.

#### Wi-Fi over Bluetooth

Wi-Fi may take over when:

- health is at least GOOD;
- stable for 3–5 s depending on previous failure state;
- candidate advantage is meaningful;
- cooldown allows switching.

#### Bluetooth over Wi-Fi

Bluetooth may take over when:

- Wi-Fi is CRITICAL; or
- Wi-Fi remains DEGRADED for about 1.5 s and Bluetooth is healthy; or
- Wi-Fi disconnects.

Hard disconnects trigger immediate failover when a READY backup exists.

### 11.14 Anti-flapping rule

Normal switching should require an approximate candidate score margin of 15 points and a sustained advantage, except for emergency takeover or USB-preference takeover after stabilization.

---

## 12. Protocol requirements

The protocol is independent of USB, Wi-Fi, and Bluetooth.

Each realtime state packet must support:

- protocol version;
- device/session identity;
- global session sequence number;
- monotonic timestamp;
- button bitset;
- D-pad state;
- LX / LY;
- RX / RY;
- LT / RT;
- integrity check where appropriate.

Additional message types must support:

- pairing/authentication;
- handshake;
- capabilities;
- heartbeat;
- health probes;
- transport negotiation;
- state synchronization;
- handover;
- rumble/output;
- disconnect/recovery.

Sequence numbers are global per controller session, not per transport.

---

## 13. State synchronization and safety

### Authoritative transport

Only one transport may be authoritative for virtual-controller updates at a time.

During handover:

1. prepare candidate;
2. authenticate;
3. synchronize current full state;
4. confirm fresh valid sequence;
5. atomically switch authoritative transport;
6. demote previous transport to standby.

### Full-state packets

The system uses complete current gamepad state, not only edge events.

This prevents missed release/press events during packet loss or handover.

### Neutral watchdog

If no fresh valid authoritative state is available for the configured safety timeout, write a neutral controller state:

- sticks centered;
- triggers zero;
- all buttons released.

The virtual device itself remains present.

---

## 14. Performance targets

These are engineering targets, not guaranteed numbers on every device.

- Input path must avoid unnecessary allocations.
- UI animations must not block the realtime controller path.
- Default state rate: 120 Hz.
- Competitive profile may support up to 240 Hz when both devices and transport remain stable.
- Failover decision logic should add minimal artificial delay.
- USB should be the preferred competitive transport once stable.
- Wi-Fi should normally provide low-latency local operation.
- Bluetooth prioritizes resilient wireless fallback.

---

## 15. Windows Receiver requirements

Technology baseline:

- C#;
- .NET 10;
- WPF for desktop UI;
- HIDMaestro-based Xbox 360-compatible virtualization, subject to integration validation.

Core components:

- transport manager;
- Smart Connection Manager;
- protocol decoder;
- controller-session manager;
- safety/watchdog engine;
- virtual-controller backend;
- diagnostics;
- pairing/trust store;
- system-tray integration.

The virtual controller must remain stable across transport changes.

---

## 16. Android requirements

Technology baseline:

- Kotlin native;
- dedicated touch surface/custom view for gameplay controls;
- Android foreground service while an active controller session is running.

Core components:

- touch/pointer router;
- analog processors;
- gamepad-state store;
- haptic engine;
- unified protocol;
- USB transport;
- Wi-Fi transport;
- Bluetooth transport;
- Smart Connection client logic;
- profile/layout storage;
- pairing/trust store.

---

## 17. Security and trust

- Pairing establishes a trusted local device relationship.
- Normal reconnects authenticate automatically.
- Wi-Fi input must not accept arbitrary unauthenticated packets from other LAN clients.
- Session identity must prevent stale packets from an old session controlling the current virtual gamepad.
- Secrets/keys must never be committed to Git.
- No telemetry leaves the local network unless a future opt-in feature explicitly defines it.

---

## 18. Configuration profiles

Initial profiles:

### eFootball

- Competitive connection policy.
- Large left stick.
- generous ABXY touch targets;
- low input filtering;
- low analog deadzone;
- digital-style trigger response option;
- Smart Auto enabled.

### Xbox Standard

More conventional mapping/feel.

### Custom

User-editable placement, sizing, sensitivity, deadzone, and haptics.

---

## 19. Diagnostics

Windows and Android should eventually expose:

- active transport;
- backup transport status;
- connection score;
- RTT/latency;
- jitter;
- packet loss;
- state/input rate;
- reconnect count;
- recent handovers;
- virtual-controller status.

Diagnostics must not become a dependency of the hot input path.

---

## 20. Acceptance criteria for the first playable milestone

The milestone is complete when:

1. Android and Windows pair locally.
2. Wi-Fi production transport connects automatically.
3. Pressing A on Android produces A on the Windows virtual Xbox controller.
4. Analog left stick values reach the virtual controller correctly.
5. At least four simultaneous touch controls work reliably.
6. eFootball can consume the virtual controller.
7. Short packet loss does not create stuck input.
8. Loss of the active transport causes neutral-state safety behavior.
9. Reconnection happens automatically.
10. Reconnection does not create a new virtual controller instance.
11. Core code is structured for USB and Bluetooth transports without rewriting controller logic.

---

## 21. Product principles

When implementation trade-offs arise, use this order:

1. correctness;
2. input safety;
3. latency consistency;
4. connection resilience;
5. ergonomics;
6. maintainability;
7. visual polish.

Never improve visual polish at the cost of input responsiveness or reliability.
