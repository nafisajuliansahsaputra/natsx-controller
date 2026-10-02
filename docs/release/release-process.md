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
4. Windows production driver/signing posture is approved. Test-signed KMDF packages must not be distributed as production drivers.
5. Security and dependency/license review is complete.
6. Changelog is updated for the target version.

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
- builds the Inno Setup installer when the production dependency gate is satisfied;
- keeps the portable ZIP available for validation.

## Windows installer policy

The installer requires administrator elevation because virtual-controller and driver setup may require privileged operations.

Normal uninstall removes installed application files and the Receiver startup entry. It intentionally preserves trusted controller records and local peer identity under %LOCALAPPDATA%\NATSX\Controller so reinstall/upgrade does not force pairing again.

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
