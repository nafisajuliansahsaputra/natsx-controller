# WORKFLOW.md

## 1. Purpose

This file defines how changes move from idea to production-quality repository state.

The aim is to prevent rapid iteration from turning the project into inconsistent one-off code.

---

## 2. Default development flow

For implementation work:

```text
select TODO item
 -> inspect relevant docs/code
 -> create focused branch
 -> implement
 -> test
 -> update docs/TODO
 -> review diff
 -> merge
```

Recommended branch naming:

```text
feat/protocol-v1
feat/windows-core
feat/android-touch-engine
feat/wifi-transport
feat/smart-connection
feat/bluetooth
feat/usb-direct

fix/<short-description>
docs/<short-description>
refactor/<short-description>
```

Keep `main` buildable.

---

## 3. Before coding

For every task:

1. Identify the TODO milestone.
2. Read the relevant section of `PRD.md`.
3. Read relevant architecture boundaries.
4. Search existing code before creating new abstractions.
5. Determine required tests.
6. Determine whether the change needs an ADR.

Do not start from implementation details before understanding the product behavior.

---

## 4. Documentation-first cases

Write/update documentation before or with implementation when changing:

- protocol fields;
- protocol version;
- connection thresholds;
- state-machine transitions;
- USB architecture;
- Bluetooth architecture;
- virtual backend;
- authentication model;
- controller layout behavior visible to users.

This does not require writing essays for minor refactors.

---

## 5. Protocol change workflow

Any protocol change must follow:

```text
protocol spec
 -> fixtures/test vectors
 -> C# implementation
 -> Kotlin implementation
 -> cross-language verification
 -> integration test
```

Never merge a wire-format change implemented on only one platform.

For incompatible changes:

- increment protocol version appropriately;
- define rejection/compatibility behavior.

---

## 6. Smart Connection change workflow

Any connection-policy change must include:

1. affected metric/state;
2. old behavior;
3. new behavior;
4. hysteresis implications;
5. cooldown implications;
6. emergency override behavior;
7. at least one automated or deterministic scenario test.

Do not tune based on one anecdotal spike alone.

Record measurements during real-device tuning.

---

## 7. Android feature workflow

For gameplay controls:

1. implement core state behavior;
2. test pointer ownership;
3. test release/cancel;
4. test simultaneous touches;
5. test analog output;
6. only then polish animation/visual feedback.

A pretty control with unreliable state is not acceptable.

---

## 8. Windows feature workflow

For receiver features:

1. keep engine independent of WPF window;
2. implement/test core service;
3. integrate virtual backend;
4. expose state to UI;
5. add diagnostics;
6. verify tray/minimized operation where relevant.

---

## 9. Test layers

### Fast/unit

Run continuously while developing:

- state;
- protocol;
- math;
- state machine;
- policy;
- watchdog.

### Integration

Run before merging features that cross boundaries:

- protocol compatibility;
- transport/session;
- virtual backend;
- Android-to-Windows connection.

### Device/manual

Required where hardware/OS behavior is involved:

- multi-touch feel;
- USB;
- Bluetooth;
- Wi-Fi failover;
- eFootball behavior.

### Soak

Run before release candidates:

- long gameplay;
- reconnect loops;
- repeated handovers.

---

## 10. Review checklist

Before merging:

### Correctness

- [ ] Requirement is satisfied.
- [ ] Failure behavior is defined.
- [ ] No stale/stuck input path introduced.

### Architecture

- [ ] Transport semantics remain independent.
- [ ] UI does not own engine state.
- [ ] Virtual controller lifecycle is not coupled to socket lifecycle.
- [ ] Thresholds are centralized.

### Security

- [ ] Untrusted peers cannot inject controller state.
- [ ] Secrets are not logged or committed.

### Quality

- [ ] Build passes.
- [ ] Tests pass.
- [ ] Formatting/lint passes.
- [ ] TODO/docs updated.
- [ ] No temporary debug hacks.

---

## 11. Commit conventions

Use conventional, descriptive messages.

Examples:

```text
docs: add project architecture contract
chore: scaffold windows solution
feat(protocol): define gamepad state v1
feat(android): add pointer router
feat(wifi): stream realtime state over udp
feat(connection): add degraded-state hysteresis
fix(safety): neutralize stale authoritative state
test(protocol): add kotlin csharp fixtures
```

Avoid messages such as:

```text
fix
update
test
changes
final
```

---

## 12. Pull request guidance

A good PR should explain:

- what problem it solves;
- what changed;
- how it was tested;
- any remaining risk;
- any follow-up TODO.

Keep unrelated changes out of the PR.

Large milestones may be split into buildable vertical slices.

---

## 13. Definition of ready for merge

A change is ready when:

- it compiles on its target platform;
- relevant tests pass;
- public behavior matches documentation;
- no known severe input-safety bug remains;
- connection changes include deterministic state-machine coverage;
- new dependencies are justified;
- repository docs remain internally consistent.

---

## 14. Revert policy

If a merged change causes:

- stuck input;
- virtual-controller instability;
- repeated connection flapping;
- severe latency regression;
- inability to reconnect;
- broken release build;

prefer reverting to the last known-good behavior before layering emergency hacks on top.

After revert:

1. capture reproduction;
2. add regression coverage;
3. re-implement cleanly.

---

## 15. Experimental work

Experiments are allowed, but must not become hidden production dependencies.

Place short-lived experiments on dedicated branches or clearly isolated areas.

Production code must not depend on:

- ADB;
- temporary localhost scripts;
- hard-coded IPs;
- developer-only certificates;
- manually installed undocumented components.

---

## 16. Release workflow

Before a release candidate:

1. freeze protocol changes unless necessary;
2. run full CI;
3. run Windows installer test;
4. run Android clean install/update test;
5. test Wi-Fi;
6. test Bluetooth;
7. test USB Direct;
8. test Smart Auto transitions;
9. run eFootball gameplay session;
10. run soak tests;
11. review diagnostics for unexplained errors;
12. review dependency licenses;
13. tag/version;
14. publish release notes.

---

## 17. Real-device tuning workflow

Thresholds in `ConnectionPolicy` begin as engineering defaults.

When tuning:

1. capture actual RTT/jitter/loss;
2. capture device/transport context;
3. reproduce multiple times;
4. change centralized policy;
5. compare before/after;
6. test recovery/hysteresis;
7. keep a record in issue/ADR if behavior meaningfully changes.

Never spread one-off device workarounds through unrelated classes.

---

## 18. Work order currently approved

The current order is:

1. repository scaffolding;
2. protocol v1;
3. Windows controller core;
4. Android touch core;
5. eFootball layout v1;
6. Wi-Fi production transport;
7. Smart Connection Manager v1;
8. Bluetooth;
9. USB Direct;
10. pairing/trust hardening;
11. rumble/haptics;
12. UX/diagnostics;
13. reliability/performance;
14. release engineering.

This order may be changed only deliberately; do not skip foundational boundaries just to make a screen look finished.
