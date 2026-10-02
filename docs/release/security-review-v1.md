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

## Supply-chain controls

- HIDMaestro is pinned to v1.9.2 and its release archive SHA-256 is verified before use.
- The exact HIDMaestro license is retained and redistributed with Windows publish output.
- NuGet audit is enabled for direct and transitive dependencies at moderate-or-higher severity. Repository-wide warnings-as-errors make an audit warning fail restore/build.
- Dependabot watches NuGet, Gradle, and GitHub Actions dependency families.

## Findings and disposition

No critical/high-severity code finding was identified in the reviewed trust/session/transport paths.

One release-blocking packaging finding remains by design: CI-generated NATSX USB driver packages are test-signed and MUST NOT ship. Clean-machine USB Direct requires production-signed KMDF AOA-bootstrap and AOA WinUSB packages, validated and bundled by the production installer. Until those packages are available, a release may exercise Wi-Fi/Bluetooth but must not be called feature-complete production because that would remove the promised USB Direct path.

The repository product-license choice is also intentionally unresolved. That is a legal/product-owner decision tracked separately in M0, not a cryptographic finding.

## Release rules

- Never enable Windows TESTSIGNING as an end-user installation step.
- Never bundle `*.CI-Test.cer`, ephemeral CI certificates, or a package manifest whose signing mode is test/development in a production installer.
- Never weaken the exact hardware-ID restrictions merely to make driver installation easier.
- Never persist live session keys across process restart as an upgrade shortcut.
- Any future protocol-major, trust-schema, or cryptographic primitive change requires a new review.
