# TODO.md

This is the execution roadmap for NATSX Controller.

Checkboxes should reflect repository reality. Do not mark items complete because a design was discussed; mark them complete only when the corresponding repository work exists and is verified.

---

## M0 — Product and engineering contract

- [x] Create repository.
- [x] Add `PRD.md`.
- [x] Add `AGENTS.md`.
- [x] Add `ARCHITECTURE.md`.
- [x] Add `TODO.md`.
- [x] Add `SKILL.md`.
- [x] Add `WORKFLOW.md`.
- [ ] Add top-level `README.md` once build/run commands exist.
- [ ] Add license decision.
- [ ] Add contributor/development environment notes when scaffolding exists.

---

## M1 — Repository scaffolding

### Root

- [x] Add `.gitignore`.
- [x] Add `.editorconfig`.
- [x] Add formatting conventions.
- [x] Create `docs/decisions/`.
- [x] Create `protocol/`.

### Windows

- [ ] Create .NET 10 solution under `windows/`.
- [ ] Create core library.
- [ ] Create protocol library.
- [ ] Create connection library.
- [ ] Create receiver WPF application.
- [ ] Create test projects.
- [ ] Establish dependency direction.

### Android

- [ ] Create Android project under `android/`.
- [ ] Establish Kotlin package structure.
- [ ] Create gameplay/core/transport separation.
- [ ] Add unit-test setup.
- [ ] Add foreground-service skeleton.

### CI

- [ ] Build Windows projects in CI.
- [ ] Run Windows tests in CI.
- [ ] Build Android project in CI.
- [ ] Run Android unit tests in CI.
- [ ] Add formatting/lint checks.

**Exit criteria:** empty product shells build consistently on clean environments.

---

## M2 — Protocol v1

- [ ] Define protocol version negotiation.
- [ ] Define peer/device identity fields.
- [ ] Define session ID.
- [ ] Define global realtime sequence format.
- [ ] Define monotonic timestamp representation.
- [ ] Define `GamepadState` bit layout.
- [ ] Define D-pad encoding.
- [ ] Define analog encoding.
- [ ] Define handshake messages.
- [ ] Define capability messages.
- [ ] Define heartbeat/health messages.
- [ ] Define realtime state message.
- [ ] Define state-sync/handover messages.
- [ ] Define output/rumble message.
- [ ] Define disconnect/recovery semantics.
- [ ] Define invalid/stale packet handling.
- [ ] Define sequence wrap behavior.
- [ ] Define authentication/integrity strategy.
- [ ] Add protocol test vectors.
- [ ] Add Android encoder/decoder.
- [ ] Add C# encoder/decoder.
- [ ] Verify cross-language round-trip fixtures.

**Exit criteria:** Kotlin and C# encode/decode the same v1 fixtures exactly.

---

## M3 — Windows controller core

- [ ] Implement `GamepadState`.
- [ ] Implement neutral state.
- [ ] Implement `ControllerSession`.
- [ ] Implement authoritative transport ownership.
- [ ] Implement global sequence validation.
- [ ] Implement `InputSafetyEngine`.
- [ ] Implement 150 ms default neutralization policy via centralized config.
- [ ] Define `IVirtualGamepadBackend`.
- [ ] Implement HIDMaestro adapter.
- [ ] Create one virtual Xbox 360-compatible controller.
- [ ] Submit digital buttons.
- [ ] Submit sticks.
- [ ] Submit triggers.
- [ ] Verify device remains alive while session transport is absent/recovering.
- [ ] Implement output/rumble callback boundary.
- [ ] Add virtual-backend diagnostics.

**Exit criteria:** Windows can drive the virtual controller from deterministic internal test states without any phone connection.

---

## M4 — Android gameplay core

### Touch engine

- [ ] Build dedicated landscape gameplay surface.
- [ ] Track pointer IDs independently.
- [ ] Implement control ownership.
- [ ] Implement touch hysteresis.
- [ ] Prevent unrelated pointer cancellation.
- [ ] Handle ACTION_CANCEL safely.
- [ ] Handle app focus loss safely.

### Left stick

- [ ] Fixed center.
- [ ] Radial normalization.
- [ ] Configurable deadzone.
- [ ] Outer clamp.
- [ ] Linear response curve.
- [ ] Light anti-jitter.
- [ ] Instant recenter.

### Right stick

- [ ] Same core analog pipeline.
- [ ] Independent sizing/hitbox.

### Digital controls

- [ ] A/B/X/Y.
- [ ] LB/RB.
- [ ] LT/RT.
- [ ] L3/R3.
- [ ] Back/View.
- [ ] Start/Menu.
- [ ] Guide where supported.
- [ ] D-pad.

### State

- [ ] Implement Android `GamepadStateStore`.
- [ ] Publish full-state snapshots.
- [ ] Avoid unbounded event queues.

**Exit criteria:** touch tests prove simultaneous independent controls and stable analog values.

---

## M5 — eFootball Layout v1

- [ ] Recreate the established Monect-derived baseline.
- [ ] Keep large left stick.
- [ ] Place LT/LB upper-left.
- [ ] Place RT/RB upper-right.
- [ ] Place large A/B/X/Y on the right.
- [ ] Place Back/Start/L3/R3 center.
- [ ] Place D-pad lower-middle.
- [ ] Increase right-stick usability.
- [ ] Separate visual size from invisible hitbox size.
- [ ] Add basic layout scale.
- [ ] Add safe-area handling.
- [ ] Add landscape orientation enforcement.
- [ ] Add keep-screen-awake behavior during gameplay.
- [ ] Add configurable haptic strength.
- [ ] Validate common multi-touch combinations used in eFootball.

**Exit criteria:** layout is comfortable enough for extended eFootball play before visual polish begins.

---

## M6 — Wi-Fi production transport

- [ ] Define Wi-Fi transport interface implementation.
- [ ] Implement local receiver binding.
- [ ] Implement discovery.
- [ ] Implement trusted peer identification.
- [ ] Implement authentication.
- [ ] Implement realtime UDP state flow.
- [ ] Implement control/handshake flow.
- [ ] Implement bounded/latest-state behavior.
- [ ] Implement heartbeat.
- [ ] Implement RTT measurement.
- [ ] Implement jitter calculation.
- [ ] Implement packet-loss estimation.
- [ ] Implement stale/duplicate rejection.
- [ ] Implement direct reconnect to last endpoint.
- [ ] Fall back to discovery when direct reconnect fails.
- [ ] Add diagnostics.
- [ ] Add network-loss test harness.

**Playable milestone:** pressing/holding controls on Android affects the virtual Windows controller and eFootball over Wi-Fi.

---

## M7 — Smart Connection Manager v1

### Policy

- [ ] Implement centralized `ConnectionPolicy`.
- [ ] Implement Fast/Normal/Long health windows.
- [ ] Implement transport-specific latency bands.
- [ ] Implement jitter bands.
- [ ] Implement Wi-Fi packet-loss bands.
- [ ] Implement silence timers.

### States

- [ ] DISCONNECTED.
- [ ] DISCOVERING.
- [ ] CONNECTING.
- [ ] AUTHENTICATING.
- [ ] STABILIZING.
- [ ] READY.
- [ ] ACTIVE.
- [ ] SUSPECT.
- [ ] DEGRADED.
- [ ] HANDOVER.
- [ ] RECOVERING.
- [ ] COOLDOWN.

### Selection

- [ ] Implement base preference USB > Wi-Fi > Bluetooth.
- [ ] Implement health score.
- [ ] Implement 15-point normal switch margin.
- [ ] Implement candidate minimum-health rule.
- [ ] Implement emergency override.

### Stability

- [ ] USB stabilization.
- [ ] Wi-Fi degraded recovery hysteresis.
- [ ] Wi-Fi failed recovery hysteresis.
- [ ] Bluetooth recovery hysteresis.
- [ ] Normal switch cooldown.
- [ ] Failure switch cooldown.
- [ ] Repeated failure cooldown.
- [ ] Circuit breaker.
- [ ] Failure penalty decay.

### Handover

- [ ] Make-before-break flow.
- [ ] Candidate state synchronization.
- [ ] Global sequence continuity.
- [ ] Atomic authoritative switch.
- [ ] Old transport demotion.
- [ ] No neutral frame during healthy handover.
- [ ] Neutralize safely when all transports fail.

**Exit criteria:** scripted Wi-Fi instability automatically reconnects/fails over according to policy without manual reconnect and without recreating the virtual controller.

---

## M8 — Bluetooth transport

- [ ] Implement Bluetooth permissions/setup.
- [ ] Implement first-time OS pairing flow.
- [ ] Implement RFCOMM transport.
- [ ] Implement trusted auto-reconnect.
- [ ] Implement protocol handshake.
- [ ] Implement health metrics appropriate to Bluetooth.
- [ ] Integrate with Smart Connection Manager.
- [ ] Support READY/warm standby.
- [ ] Wi-Fi -> Bluetooth failover.
- [ ] Bluetooth -> recovered Wi-Fi handback.
- [ ] Verify anti-flapping rules.

**Exit criteria:** Wi-Fi may be disabled mid-session and gameplay resumes automatically over Bluetooth when the link is ready.

---

## M9 — USB Direct transport

- [ ] Validate final Android USB accessory/direct data approach.
- [ ] Validate Windows-side USB implementation.
- [ ] Record ADR for exact USB design.
- [ ] Implement direct USB transport.
- [ ] No USB tethering.
- [ ] No ADB requirement.
- [ ] Detect charge-only/non-data cable failure.
- [ ] Implement handshake.
- [ ] Implement USB health monitoring.
- [ ] Implement 250–500 ms stabilization.
- [ ] Integrate with Smart Connection Manager.
- [ ] Wi-Fi/Bluetooth -> USB preferred takeover.
- [ ] USB disconnect -> immediate best-backup takeover.
- [ ] Verify Windows Ethernet/LAN route remains unaffected.

**Exit criteria:** USB can be plugged/unplugged during a running controller session and Smart Auto moves transports without manual reconnect.

---

## M10 — Pairing and trust

- [ ] Define first-pair user flow.
- [ ] Generate/store peer identity.
- [ ] Protect long-term trust material using platform secure storage.
- [ ] Implement pairing code/confirmation flow.
- [ ] Bind trust to the intended device.
- [ ] Reject untrusted LAN state packets.
- [ ] Reject stale session packets.
- [ ] Add “Forget device”.
- [ ] Add pairing reset/recovery.

---

## M11 — Rumble and haptics

- [ ] Local touch haptics.
- [ ] Off/Low/Medium/High settings.
- [ ] Virtual-controller rumble capture.
- [ ] Output protocol message.
- [ ] Android rumble handler.
- [ ] Ensure output cannot block input.
- [ ] Define behavior during transport handover.
- [ ] Graceful fallback on devices with limited haptics.

---

## M12 — Receiver UX

- [ ] Connected device status.
- [ ] Active transport.
- [ ] Backup transport status.
- [ ] Virtual controller status.
- [ ] RTT.
- [ ] Jitter.
- [ ] packet loss/error health.
- [ ] Input rate.
- [ ] Reconnect count.
- [ ] Recent handover reason.
- [ ] Settings.
- [ ] Diagnostics view.
- [ ] Minimize to system tray.
- [ ] Optional startup with Windows.
- [ ] Clear error states for missing backend/permissions.

---

## M13 — Android UX

- [ ] Pairing screen.
- [ ] Trusted PC list.
- [ ] Smart Auto connection status.
- [ ] Gameplay screen.
- [ ] Connection overlay that does not interrupt controls unnecessarily.
- [ ] Controller profile chooser.
- [ ] Sensitivity/deadzone settings.
- [ ] Haptic settings.
- [ ] Manual transport override.
- [ ] Diagnostics.
- [ ] Calibration flow.
- [ ] Custom layout editor.

---

## M14 — Reliability and performance

- [ ] 30-minute soak test.
- [ ] 2-hour soak test.
- [ ] repeated Wi-Fi toggle test.
- [ ] repeated Bluetooth recovery test.
- [ ] repeated USB reconnect test.
- [ ] transport flapping test.
- [ ] packet reordering test.
- [ ] packet duplication test.
- [ ] packet loss simulation.
- [ ] Android background/foreground test.
- [ ] screen rotation/lock behavior test.
- [ ] Windows sleep/resume test.
- [ ] receiver restart recovery.
- [ ] phone app restart recovery.
- [ ] no stuck-input verification.
- [ ] allocation/profile hot input path.
- [ ] CPU usage review.
- [ ] battery impact review.

---

## M15 — Release engineering

- [ ] Windows installer strategy.
- [ ] Driver/backend dependency installation.
- [ ] elevation flow.
- [ ] uninstall cleanup.
- [ ] Android signed release build.
- [ ] versioning strategy.
- [ ] changelog.
- [ ] release checklist.
- [ ] upgrade/migration behavior.
- [ ] crash/log collection strategy that remains local-first/private.
- [ ] security review.
- [ ] license/dependency review.

---

## Later / post-v1

- [ ] More controller profiles.
- [ ] Advanced response curves.
- [ ] Profile export/import.
- [ ] More sophisticated layout editor.
- [ ] Optional adaptive input rate.
- [ ] Additional accessibility options.
- [ ] Explore iOS only after Android/Windows product quality is stable.

---

## Explicitly deferred

Do not start these unless the product scope changes:

- [ ] Remote desktop.
- [ ] Keyboard/mouse remote.
- [ ] Game streaming.
- [ ] File transfer.
- [ ] Media remote.
- [ ] Presentation remote.
- [ ] Cloud account system.
