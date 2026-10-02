# v1 security review

Date: 2026-10-02

Scope: Android controller, Windows Receiver, NATSX protocol/session code, Wi-Fi/Bluetooth/USB transport authentication, trust storage, release secrets, diagnostics, and Windows USB-driver release posture.

This is a repository security review, not a guarantee that software is vulnerability-free.

## Threat boundaries reviewed

The v1 design assumes that untrusted devices may exist on the same LAN, arbitrary Bluetooth peers may attempt connections, USB devices may reconnect or re-enumerate unexpectedly, packets may be duplicated/reordered/replayed, and local application data may be inspected by the current user. NATSX must not treat transport reachability as trust.

## Controls verified

- First pairing uses ephemeral P-256 ECDH, transcript binding, a human-compared six-digit SAS, role-bound confirmation proofs, and HMAC-SHA-256 based key derivation.
- Pairing completes only after both local approval and cryptographic remote confirmation.
- Long-term trust secrets are not stored plaintext: Android uses Android Keystore AES-256-GCM with peer-bound AAD; Windows uses CurrentUser DPAPI.
- Temporary pairing/session secret buffers are defensively copied where ownership crosses boundaries and are zeroed on disposal where the runtime permits.
- Trusted reconnect and secondary-transport join reuse authenticated session material; Bluetooth and USB do not silently create long-term trust.
- Wi-Fi/Bluetooth/USB control traffic is authenticated and bound to the expected SessionId/peer/role before becoming authoritative.
- Sequence/freshness logic and duplicate/out-of-order rejection are covered by protocol/transport tests.
- Android disables application backup, keeps the controller service non-exported, and stores trust/configuration in private app storage.
- Release keystores/certificates and common local secret-file formats are excluded from Git.
- Android signing material is supplied only through release secrets/environment and the temporary decoded keystore is removed after the build.
- Local crash logging uploads nothing automatically and does not intentionally record raw controller frames, trust secrets, pairing proofs, or session keys.
- The Windows USB bootstrap package is hardware-ID scoped to the validated OPPO A58 no-ADB target; the AOA WinUSB package is restricted to the expected Google AOA IDs/interfaces.
- CI test certificates are ephemeral and explicitly marked non-shipping.
- The network-facing Windows Receiver is explicitly `asInvoker`; it is never elevated for normal runtime.
- Privileged HIDMaestro runtime ownership is isolated in the minimal LocalSystem `NatsxControllerGamepadHost` service.
- The LocalSystem service executable must live below Program Files; the installer rejects user-writable service roots.
- GamepadHost exposes no network listener and parses none of the Wi-Fi/Bluetooth/USB/pairing protocol.
- Receiver/GamepadHost IPC is local-only, uses a fixed versioned frame protocol, has a restrictive pipe ACL, and validates the connected client process path against the installed Receiver executable.
- The GamepadHost keeps the virtual controller alive across Receiver reconnects, neutralizes stale sessions after 500 ms, and drops the pipe rather than retaining stale input.
- Normal GamepadHost runtime does not re-run HIDMaestro's install/global sweep when the machine dependency is already present.
- Windows CI exercises first install, unelevated Receiver-to-host smoke, GamepadHost stop/start with automatic Receiver reconnect, repair/upgrade preservation, and uninstall cleanup.

## Supply-chain controls

- HIDMaestro is pinned to v1.9.2 and its release archive SHA-256 is verified before use.
- The exact HIDMaestro license is retained and redistributed with Windows publish output.
- NuGet audit is enabled for direct and transitive dependencies at moderate-or-higher severity. Repository-wide warnings-as-errors make an audit warning fail restore/build.
- Dependabot watches NuGet, Gradle, and GitHub Actions dependency families.

## Findings and disposition

No critical/high-severity code finding was identified in the reviewed trust/session/transport or privileged GamepadHost boundary paths. The previous design risk of letting the network-facing Receiver directly own privileged HIDMaestro runtime operations has been removed: normal gameplay now crosses a narrow local IPC boundary into the LocalSystem service.

One release-blocking packaging finding remains by design: CI-generated NATSX USB driver packages are test-signed and MUST NOT ship. Clean-machine USB Direct requires production-signed KMDF AOA-bootstrap and AOA WinUSB packages, validated and bundled by the production installer. Until those packages are available, a release may exercise Wi-Fi/Bluetooth but must not be called feature-complete production because that would remove the promised USB Direct path.

The repository product-license choice is also intentionally unresolved. That is a legal/product-owner decision tracked separately in M0, not a cryptographic finding.

## Release rules

- Never enable Windows TESTSIGNING as an end-user installation step.
- Never bundle `*.CI-Test.cer`, ephemeral CI certificates, or a package manifest whose signing mode is test/development in a production installer.
- Never weaken the exact hardware-ID restrictions merely to make driver installation easier.
- Never persist live session keys across process restart as an upgrade shortcut.
- Any future protocol-major, trust-schema, or cryptographic primitive change requires a new review.


## Windows production-driver gate

The repository now has an explicit production-package validator and installer handoff. The validator requires the Microsoft Windows Hardware Driver Verification EKU (1.3.6.1.4.1.311.10.3.5), rejects the attestation verification EKU (1.3.6.1.4.1.311.10.3.5.1), runs SignTool kernel-policy verification, verifies the bootstrap catalog covers the KMDF binary, rejects CI-test/private certificate material, and revalidates exact hardware-ID scoping.

The release pipeline fails closed when the production bundle or its pinned SHA-256 is missing. CI may compile a clearly NON-SHIPPING installer without those drivers only to validate installer syntax.
