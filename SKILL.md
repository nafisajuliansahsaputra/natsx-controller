# SKILL.md

## NATSX Controller project skill

This file is a compact implementation playbook for working effectively in this repository.

It is not a replacement for `PRD.md` or `ARCHITECTURE.md`.

---

## 1. Mission

Build a production-quality Android-to-Windows controller bridge that feels reliable enough for eFootball and presents one stable Xbox 360-compatible controller to games.

Three transports:

- USB Direct;
- Wi-Fi;
- Bluetooth.

One logical controller.

One protocol.

One Smart Connection Manager.

---

## 2. Start every task with scope classification

Classify the task before coding.

### Product behavior

Examples:

- button layout;
- failover behavior;
- latency policy;
- profile defaults.

Read/update:

- `PRD.md`
- `TODO.md`

### Architecture

Examples:

- new module;
- transport boundary;
- protocol change;
- virtual backend change.

Read/update:

- `ARCHITECTURE.md`
- ADR under `docs/decisions/` when significant.

### Protocol

Examples:

- packet fields;
- message type;
- sequence behavior.

Read/update:

- `protocol/`
- both Kotlin and C# implementations;
- cross-language fixtures.

### Implementation-only

Examples:

- internal refactor;
- bug fix that preserves behavior.

Keep documentation changes proportional, but still update TODO when milestone status changes.

---

## 3. Realtime mental model

Always trace changes through:

```text
Finger
 -> Android pointer
 -> control processor
 -> GamepadState
 -> protocol
 -> active transport
 -> Windows decoder
 -> ControllerSession
 -> safety
 -> virtual controller
 -> game
```

For connection behavior also trace:

```text
transport metrics
 -> health window
 -> policy
 -> state machine
 -> candidate selection
 -> handover
 -> authoritative transport
```

If a change cannot be explained along these paths, it may be crossing boundaries incorrectly.

---

## 4. How to implement input safely

### Digital button

A button implementation needs:

- visible geometry;
- touch geometry;
- pointer ownership;
- press;
- release;
- cancel;
- state-store update;
- haptic hook.

Never rely on visual pressed state as the source of truth.

### Analog stick

Implement in this order:

1. pointer capture;
2. local coordinates;
3. vector from center;
4. clamp to radius;
5. normalize;
6. deadzone;
7. curve;
8. light filter;
9. map to signed logical range;
10. publish;
11. recenter on release/cancel.

Test cardinal directions, diagonals, small movements, outer radius, and recenter.

---

## 5. How to implement a transport

Every transport must answer:

- How is a trusted peer discovered?
- How is the connection established?
- How is the session authenticated?
- How is realtime state delivered?
- How are control messages delivered?
- How is health measured?
- How is disconnect detected?
- How is reconnect attempted?
- How does it become READY?
- How does it cleanly stop?

The transport must not decide by itself that it is authoritative.

That decision belongs to Smart Connection Manager/session authority.

---

## 6. How to modify Smart Connection Manager

Before changing a threshold, identify:

- metric;
- averaging window;
- entry threshold;
- recovery threshold;
- minimum duration;
- cooldown interaction;
- emergency override;
- circuit-breaker effect.

Do not change one threshold in isolation without considering hysteresis.

Example:

Bad:

```text
if latency > 30:
    switch()
```

Correct model:

```text
metric window
 -> classify health
 -> enter SUSPECT/DEGRADED after sustained condition
 -> evaluate READY candidate
 -> apply margin/hysteresis/cooldown
 -> handover
```

---

## 7. Debugging connection problems

Use this order.

1. Is the transport physically/logically available?
2. Is the peer trusted?
3. Did protocol negotiation succeed?
4. Is the session ID current?
5. Are sequence numbers fresh?
6. Are state packets arriving?
7. Are they being rejected?
8. What is active authoritative transport?
9. Is watchdog neutralizing state?
10. Is virtual backend accepting writes?
11. Does Windows/game see the same virtual device?

Do not begin by changing thresholds.

---

## 8. Debugging stuck input

Check:

1. Android emitted release/cancel?
2. latest full state contains release?
3. state packet sequence is accepted?
4. authoritative transport is correct?
5. handover duplicated/staled state?
6. watchdog ran?
7. virtual backend received neutral/released state?

Because packets carry full state, a lost release edge should not permanently stick the button once a newer valid state arrives.

---

## 9. Debugging analog feel

Measure separately:

- raw touch geometry;
- normalized vector;
- deadzone output;
- response curve output;
- filtered output;
- serialized value;
- decoded value;
- virtual-controller value.

Never “fix” an analog feel problem by adding heavy smoothing first.

For eFootball, prioritize directional responsiveness over visual smoothness.

---

## 10. Performance checklist

Before optimizing, measure.

Inspect:

- allocations per state update;
- packet frequency;
- CPU usage;
- UI frame time;
- thread contention;
- queue depth;
- stale packet count;
- network jitter;
- virtual backend write frequency.

Do not sacrifice correctness for benchmark numbers.

---

## 11. Testing connection failover

A proper failover test records:

- active transport before fault;
- induced fault;
- detection time;
- candidate readiness;
- handover reason;
- authoritative switch time;
- any neutralization;
- reconnect time;
- handback time;
- whether virtual controller identity changed.

Test scenarios:

- Wi-Fi packet loss;
- Wi-Fi adapter disconnect;
- router/local network interruption;
- Bluetooth disconnect;
- USB cable unplug;
- USB bad cable/data loss;
- recovered Wi-Fi while Bluetooth active;
- USB insertion while Wi-Fi active.

---

## 12. Code review questions

For every nontrivial change:

- Does this alter product behavior?
- Does it create a second source of truth?
- Can it block the hot path?
- Can it produce stale input?
- Can it produce a stuck input?
- Does it bypass authentication?
- Does it recreate the virtual controller unnecessarily?
- Does it add a new hard-coded policy value?
- Does it work across transport handover?
- What happens on cancel/disconnect?
- How is it tested?

---

## 13. Commit quality

A good commit:

- has one coherent purpose;
- builds;
- includes tests when appropriate;
- updates docs when behavior changes;
- avoids unrelated formatting churn.

Preferred style:

```text
feat(protocol): add gamepad state v1 frame
feat(wifi): add receiver discovery
fix(connection): prevent handback flapping
test(connection): cover failure cooldown
docs(architecture): record USB direct decision
```

---

## 14. Definition of done

A feature is done when:

- behavior matches PRD;
- architecture boundaries are preserved;
- implementation builds;
- tests pass;
- failure path has been considered;
- no manual recovery is required where Smart Auto promises recovery;
- diagnostics are sufficient to debug it;
- relevant TODO item is updated;
- documentation is synchronized.

---

## 15. Current build order

Use this priority unless deliberately changed:

```text
repo scaffolding
 -> protocol v1
 -> Windows core + virtual backend
 -> Android touch core
 -> eFootball layout
 -> Wi-Fi end-to-end
 -> Smart Connection Manager
 -> Bluetooth
 -> USB Direct
 -> pairing hardening
 -> rumble
 -> UX/diagnostics
 -> soak/performance/release
```

Wi-Fi is implemented first as a production transport because it enables the fastest full end-to-end validation of the architecture. It is not a disposable prototype.
