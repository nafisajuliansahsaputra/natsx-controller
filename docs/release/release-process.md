# Release process

## Versioning

- VERSION is the application version shared by Android and Windows.
- VERSION_CODE is the monotonic Android package version code.
- Protocol compatibility is independent and remains defined in protocol/versions.md.
- Release tags use v<application-version>.
- Before tagging, remove the -dev suffix from VERSION and increment VERSION_CODE.

## Required release gates

1. Android CI, Windows CI, Windows Driver CI, and HIDMaestro Bootstrap CI are green.
2. Required M14 physical reliability gates are complete.
3. Android signing secrets are configured in GitHub: ANDROID_KEYSTORE_BASE64, ANDROID_KEYSTORE_PASSWORD, ANDROID_KEY_ALIAS, and ANDROID_KEY_PASSWORD.
4. Windows Authenticode secrets are configured: WINDOWS_SIGNING_PFX_BASE64 and WINDOWS_SIGNING_PFX_PASSWORD; WINDOWS_TIMESTAMP_URL points to an approved RFC3161 timestamp service.
5. Windows production driver/signing posture is approved. Test-signed KMDF packages must not be distributed as production drivers.
6. Security and dependency/license review is complete; the separate NATSX product-license decision in M0 is resolved before public distribution.
7. Inno Setup usage is confirmed for the intended release context and NATSX_INNO_LICENSE_CONFIRMED is set only after that review.
8. Local crash diagnostics behavior has been validated and does not upload data automatically.
9. Changelog has a final section matching VERSION.

## Release workflow

The Release GitHub Actions workflow runs on v* tags or manual dispatch.

Android release:

- runs unit tests;
- verifies signing configuration;
- builds a signed release APK;
- uploads the APK as a workflow artifact;
- removes the temporary keystore from the runner.

Windows release:

- restores and tests the solution;
- publishes a self-contained win-x64 Receiver;
- Authenticode-signs and verifies NATSX-owned Receiver binaries;
- builds the Inno Setup installer only when the production dependency gate is satisfied;
- Authenticode-signs and verifies the final installer;
- emits SHA-256 checksum files for Windows artifacts;
- keeps the signed portable ZIP available for validation.

## Windows installer policy

The installer requires administrator elevation because virtual-controller and driver setup may require privileged operations.

The installer performs the HIDMaestro/virtual-controller bootstrap while Setup is already elevated. It launches the Receiver in the hidden --install-driver mode, requires a successful backend create/teardown, and aborts installation if that verification fails. The normal post-install Receiver launch is then returned to the original unelevated user context.

Normal uninstall runs the hidden --uninstall-cleanup mode before deleting program files. That removes the per-user Receiver startup entry and Receiver UI settings while intentionally preserving trusted controller records and local peer identity under %LOCALAPPDATA%\NATSX\Controller so reinstall/upgrade does not force pairing again.

Use windows/installer/purge-user-data.ps1 -PurgeTrust only for an explicit full reset.

## Release checklist

- [ ] Set final VERSION and increment VERSION_CODE.
- [ ] Confirm protocol compatibility/version.
- [ ] Run all CI workflows.
- [ ] Run required M14 physical soak/recovery tests.
- [ ] Validate Android release signing.
- [ ] Validate Windows publish artifact on a clean Windows machine.
- [ ] Validate production-signed driver/backend installation.
- [ ] Validate upgrade over previous installed version.
- [ ] Validate normal uninstall preserves pairing data.
- [ ] Validate explicit purge removes pairing data.
- [ ] Complete security review.
- [ ] Complete dependency/license review.
- [ ] Update changelog.
- [ ] Tag v<version>.


## Local diagnostics

Crash collection is local-only and bounded. See `docs/release/local-diagnostics.md` for storage paths, privacy boundaries, rotation, and the support-sharing procedure.


## Dependency and security evidence

- `docs/release/security-review-v1.md` records the v1 security review and residual release blockers.
- `THIRD_PARTY_NOTICES.md` inventories redistributed/build-only dependencies.
- The Windows publish output carries the exact pinned HIDMaestro v1.9.2 license as `licenses/HIDMaestro.LICENSE.txt`.
- NuGet audit is enabled for all transitive packages at moderate-or-higher severity, and warnings are already treated as build errors.
- Dependabot watches NuGet, Gradle, and GitHub Actions dependencies.


## Production USB driver gate

Feature-complete Windows releases require the two production-signed NATSX USB driver packages described in `docs/release/windows-production-driver.md`.

The release workflow consumes a SHA-256-pinned ZIP through repository variables `NATSX_PRODUCTION_DRIVER_BUNDLE_URL` and `NATSX_PRODUCTION_DRIVER_BUNDLE_SHA256`. Missing variables, hash mismatch, development/test certificate material, attestation-only signing, invalid kernel-policy signatures, catalog/binary mismatch, or broadened hardware IDs fail the release.

Ordinary Windows CI compiles the installer with `-AllowMissingProductionUsbDrivers` only as a NON-SHIPPING syntax/packaging smoke test. The release workflow never uses that bypass.


## Fail-closed release metadata

The release workflow refuses to produce artifacts while VERSION contains a development suffix. VERSION must be stable x.y.z, VERSION_CODE must be a positive integer, a pushed release tag must exactly equal v<VERSION>, LICENSE must exist, and CHANGELOG.md must contain a final section named exactly for VERSION.

This intentionally keeps the unresolved M0 product-license choice outside automation while preventing an accidental public release before that choice is made.

## Windows Authenticode signing

Production Windows artifacts use `windows/eng/sign-windows-release.ps1`. The workflow requires:

- secret `WINDOWS_SIGNING_PFX_BASE64`;
- secret `WINDOWS_SIGNING_PFX_PASSWORD`;
- repository variable `WINDOWS_TIMESTAMP_URL`.

The PFX is decoded only into the runner temporary directory, used to sign NATSX-owned Receiver binaries and the final installer, verified with SignTool, and deleted in an `always()` cleanup step. The PFX/private key must never be committed or bundled.

## Installer tool licensing gate

Recent Inno Setup releases explicitly ask commercial users to purchase a commercial license, and an unlicensed compiler identifies itself as non-commercial use. Before a production release, the project owner must confirm that the intended distribution is permitted or covered by an appropriate Inno Setup commercial license, then set `NATSX_INNO_LICENSE_CONFIRMED=true`.

That variable is an explicit release-owner acknowledgement, not an automated legal determination.
