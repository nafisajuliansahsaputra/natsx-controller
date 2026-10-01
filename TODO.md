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
- [x] Add top-level `README.md` once build/run commands exist.
- [ ] Add license decision.
- [x] Add contributor/development environment notes when scaffolding exists.

---

## M1 — Repository scaffolding

### Root

- [x] Add `.gitignore`.
- [x] Add `.editorconfig`.
- [x] Add formatting conventions.
- [x] Create `docs/decisions/`.
- [x] Create `protocol/`.

### Windows

- [x] Create .NET 10 solution under `windows/`.
- [x] Create core library.
- [x] Create protocol library.
- [x] Create connection library.
- [x] Create receiver WPF application.
- [x] Create test projects.
- [x] Establish dependency direction.

### Android

- [x] Create Android project under `android/`.
- [x] Establish Kotlin package structure.
- [x] Create gameplay/core/transport separation.
- [x] Add unit-test setup.
- [x] Add foreground-service skeleton.

### CI

- [x] Build Windows projects in CI.
- [x] Run Windows tests in CI.
- [x] Build Android project in CI.
- [x] Run Android unit tests in CI.
- [ ] Add formatting/lint checks.

**Exit criteria:** empty product shells build consistently on clean environments.

---

## M2 — Protocol v1

- [x] Define protocol version negotiation.
- [x] Define peer/device identity fields.
- [x] Define session ID.
- [x] Define global realtime sequence format.
- [x] Define monotonic timestamp representation.
- [x] Define `GamepadState` bit layout.
- [x] Define D-pad encoding.
- [x] Define analog encoding.
- [x] Define handshake messages.
- [x] Define capability messages.
- [x] Define heartbeat/health messages.
- [x] Define realtime state message.
- [x] Define state-sync/handover messages.
- [x] Define output/rumble message.
- [x] Define disconnect/recovery semantics.
- [x] Define invalid/stale packet handling.
- [x] Define sequence wrap behavior.
- [x] Define authentication/integrity strategy.
- [x] Add protocol test vectors.
- [x] Add Android encoder/decoder.
- [x] Add C# encoder/decoder.
- [x] Verify cross-language round-trip fixtures.

**Exit criteria:** Kotlin and C# encode/decode the same v1 fixtures exactly.

---

## M3 — Windows controller core

- [x] Implement `GamepadState`.
- [x] Implement neutral state.
- [x] Implement `ControllerSession`.
- [x] Implement authoritative transport ownership.
- [x] Implement global sequence validation.
- [x] Implement `InputSafetyEngine`.
- [x] Implement 150 ms default neutralization policy via centralized config.
- [x] Define `IVirtualGamepadBackend`.
- [x] Implement HIDMaestro adapter.
- [ ] Create one virtual Xbox 360-compatible controller.
- [x] Submit digital buttons.
- [x] Submit sticks.
- [x] Submit triggers.
- [ ] Verify device remains alive while session transport is absent/recovering.
- [x] Implement output/rumble callback boundary.
- [ ] Add virtual-backend diagnostics.

**Exit criteria:** Windows can drive the virtual controller from deterministic internal test states without any phone connection.

---

## M4 — Android gameplay core

### Touch engine

- [x] Build dedicated landscape gameplay surface.
- [x] Track pointer IDs independently.
- [x] Implement control ownership.
- [ ] Implement touch hysteresis.
- [x] Prevent unrelated pointer cancellation.
- [x] Handle ACTION_CANCEL safely.
- [x] Handle app focus loss safely.

### Left stick

- [x] Fixed center.
- [x] Radial normalization.
- [x] Configurable deadzone.
- [x] Outer clamp.
- [x] Linear response curve.
- [x] Light anti-jitter.
- [x] Instant recenter.

### Right stick

- [x] Same core analog pipeline.
- [x] Independent sizing/hitbox.

### Digital controls

- [x] A/B/X/Y.
- [x] LB/RB.
- [x] LT/RT.
- [x] L3/R3.
- [x] Back/View.
- [x] Start/Menu.
- [x] Guide where supported.
- [x] D-pad.

### State

- [x] Implement Android `GamepadStateStore`.
- [x] Publish full-state snapshots.
- [x] Avoid unbounded event queues.

**Exit criteria:** touch tests prove simultaneous independent controls and stable analog values.

---

## M5 — eFootball Layout v1

- [x] Recreate the established Monect-derived baseline.
- [x] Keep large left stick.
- [x] Place LT/LB upper-left.
- [x] Place RT/RB upper-right.
- [x] Place large A/B/X/Y on the right.
- [x] Place Back/Start/L3/R3 center.
- [x] Place D-pad lower-middle.
- [x] Increase right-stick usability.
- [x] Separate visual size from invisible hitbox size.
- [x] Add basic layout scale.
- [ ] Add safe-area handling.
- [x] Add landscape orientation enforcement.
- [x] Add keep-screen-awake behavior during gameplay.
- [ ] Add configurable haptic strength.
- [ ] Validate common multi-touch combinations used in eFootball.

**Exit criteria:** layout is comfortable enough for extended eFootball play before visual polish begins.

---

## M6 — Wi-Fi production transport

- [x] Define Wi-Fi transport interface implementation.
- [x] Implement local receiver binding.
- [x] Implement discovery.
- [x] Implement trusted peer identification.
- [x] Implement authenticated realtime frame validation.
- [x] Implement realtime UDP state flow.
- [x] Implement control/handshake flow.
- [x] Implement bounded/latest-state behavior.
- [x] Implement heartbeat.
- [x] Implement RTT measurement.
- [x] Implement jitter calculation.
- [x] Implement packet-loss estimation.
- [x] Implement stale/duplicate rejection.
- [x] Implement direct reconnect to last endpoint.
- [x] Fall back to discovery when direct reconnect fails.
- [x] Add diagnostics.
- [x] Add network-loss test harness.

**Playable milestone:** pressing/holding controls on Android affects the virtual Windows controller and eFootball over Wi-Fi.

---

## M7 — Smart Connection Manager v1

### Policy

- [x] Implement centralized `ConnectionPolicy`.
- [x] Implement Fast/Normal/Long health windows.
- [x] Implement transport-specific latency bands.
- [x] Implement jitter bands.
- [x] Implement Wi-Fi packet-loss bands.
- [x] Implement silence timers.

### States

- [x] DISCONNECTED.
- [x] DISCOVERING.
- [x] CONNECTING.
- [x] AUTHENTICATING.
- [x] STABILIZING.
- [x] READY.
- [x] ACTIVE.
- [x] SUSPECT.
- [x] DEGRADED.
- [x] HANDOVER.
- [x] RECOVERING.
- [x] COOLDOWN.

### Selection

- [x] Implement base preference USB > Wi-Fi > Bluetooth.
- [x] Implement health score.
- [x] Implement 15-point normal switch margin.
- [x] Implement candidate minimum-health rule.
- [x] Implement emergency override.

### Stability

- [x] USB stabilization.
- [x] Wi-Fi degraded recovery hysteresis.
- [x] Wi-Fi failed recovery hysteresis.
- [x] Bluetooth recovery hysteresis.
- [x] Normal switch cooldown.
- [x] Failure switch cooldown.
- [x] Repeated failure cooldown.
- [x] Circuit breaker.
- [x] Failure penalty decay.

### Handover

- [x] Make-before-break flow.
- [x] Candidate state synchronization.
- [x] Global sequence continuity.
- [x] Atomic authoritative switch.
- [x] Old transport demotion.
- [x] No neutral frame during healthy handover.
- [x] Neutralize safely when all transports fail.

**Exit criteria:** scripted Wi-Fi instability automatically reconnects/fails over according to policy without manual reconnect and without recreating the virtual controller.

---

## M8 — Bluetooth transport

- [x] Implement Bluetooth permissions/setup.
- [x] Implement first-time OS pairing flow.
- [x] Implement RFCOMM transport.
- [x] Implement trusted auto-reconnect.
- [x] Implement protocol handshake.
- [x] Implement health metrics appropriate to Bluetooth.
- [x] Integrate with Smart Connection Manager.
- [x] Support READY/warm standby.
- [x] Wi-Fi -> Bluetooth failover.
- [x] Bluetooth -> recovered Wi-Fi handback.
- [x] Verify anti-flapping rules.

**Exit criteria:** Wi-Fi may be disabled mid-session and gameplay resumes automatically over Bluetooth when the link is ready.

---

## M9 — USB Direct transport

- [x] Validate final Android USB accessory/direct data approach.
- [ ] Validate Windows-side USB implementation.
- [ ] Record ADR for exact USB design.
- [ ] Implement direct USB transport.
- [x] No USB tethering.
- [x] No ADB requirement.
- [ ] Detect charge-only/non-data cable failure.
- [x] Implement handshake.
- [x] Implement USB health monitoring.
- [x] Implement 250–500 ms stabilization.
- [x] Integrate with Smart Connection Manager.
- [x] Wi-Fi/Bluetooth -> USB preferred takeover.
- [x] USB disconnect -> immediate best-backup takeover.
- [ ] Verify Windows Ethernet/LAN route remains unaffected.

> Current physical blocker: pre-AOA Windows endpoint-zero bootstrap. Post-AOA AOA data mode already uses scoped Microsoft WinUSB. A KMDF pass-through bootstrap prototype plus user-mode driver bridge exists on the USB prototype branch. The OPPO A58 no-ADB state is now confirmed as `USB\\VID_22D9&PID_2764&REV_0404`, class `WPD`, service `WUDFWpdMtp`; the earlier ADB-on `PID_2765` composite layout is not a product dependency. The source-only extension-INF draft has been retargeted to the exact no-ADB PID/revision. The captured stack is `WpdUpFltr -> WUDFRd -> WINUSB -> ACPI -> USBHUB3`, and the device reports the standard MTP `WinUsb` lower filter in both legacy and compound properties. NATSX therefore does not blindly install a second unordered lower filter. The safe user-mode WinUSB probe failed at `WinUsb_Initialize` with `ERROR_INVALID_FUNCTION`, so the inbox MTP WinUSB filter cannot be used directly as the OPPO A58 bootstrap path and the kernel boundary remains required. The KMDF prototype now routes version/START_AOA IOCTLs through a sideband control device instead of relying on WPD/WUDF to forward custom requests. The exact OPPO A58 no-ADB validation package now builds in CI and passes `InfVerif /w /v`; guarded install/rollback harnesses exist but refuse unsigned packages and default to read-only preflight. The physical Windows signing posture is now confirmed: Secure Boot is enabled, VBS/HVCI is running, HVCI registry enforcement is enabled, TESTSIGNING is currently off, and the system drive is BitLocker-protected/encrypted. The ephemeral CI certificate is now trusted on the validation machine and both SYS/CAT signatures verify as Valid with signer thumbprints matching the package manifest. The read-only transition preflight passed with every gate true. BitLocker protection is physically suspended while the system drive remains FullyEncrypted at 100%, and the post-suspend gate also passed. Secure Boot is now physically disabled on the validation machine and the post-UEFI gate passed while TESTSIGNING remained off, HVCI remained running, and BitLocker remained suspended/fully encrypted. TESTSIGNING is now enabled on the validation machine and the post-TESTSIGNING physical-install gate passed with every check true: Secure Boot remains disabled, VBS/HVCI remains running, BitLocker remains suspended/fully encrypted, the OPPO A58 is healthy in the exact no-ADB WPD/MTP state, and the trusted SYS/CAT signatures remain valid. The guarded physical filter attach has now succeeded on the validation OPPO A58: the exact extension package is installed as oem229.inf, the WPD device remains Started on WUDFWpdMtp/wpdmtp.inf, and the live stack is WpdUpFltr -> WUDFRd -> NatsxAoaBootstrap -> WINUSB -> ACPI -> USBHUB3. START_AOA has not been sent. Physical build-3 probing then showed the filter loaded in the expected stack but the \\.\NatsxAoaBootstrap control device was absent. Source audit found a control-plane coupling that can produce exactly that symptom: sideband creation was attempted only after WdfUsbTargetDeviceCreateWithParameters succeeded, so build 3 could not distinguish USB-target creation failure from sideband creation failure. Build 4 now decouples the control plane from USB-target readiness, bumps the validation package to 0.1.0.1, and adds a read-only GET_STATUS readiness contract. Build 4 has now passed both Windows CI and Windows Driver CI, and the newly signed validation artifact has been emitted. A guarded build-4 preparation harness now pins the green artifact ZIP digest, CI certificate identity, exact OPPO hardware target, package version 0.1.0.1, manifest hashes, and SYS/CAT signer binding before extraction is accepted. The signed build-4 artifact has now been prepared locally and passed the pinned ZIP digest, exact hardware target, package version 0.1.0.1, manifest hash, certificate identity, and SYS/CAT signer-binding checks. Its signatures are expectedly untrusted until the new ephemeral build-4 certificate is imported. The next physical gate is trusting only that exact build-4 certificate, then replacing build 3 with build 4, followed by MTP, GET_VERSION, and GET_STATUS verification before any START_AOA transition. M9 remains incomplete until the signing posture is chosen, the device-specific package is installed physically, the resulting filter order is inspected, AOA bootstrap succeeds, and normal MTP/recovery/LAN behavior remains intact.

**Exit criteria:** USB can be plugged/unplugged during a running controller session and Smart Auto moves transports without manual reconnect.

---

## M10 — Pairing and trust

- [ ] Define first-pair user flow.
- [x] Generate/store peer identity.
- [x] Protect long-term trust material using platform secure storage.
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
