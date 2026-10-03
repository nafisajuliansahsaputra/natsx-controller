# Production readiness contract

This document defines what "production ready" means for NATSX Controller v1.

## Repository-complete gates

The codebase is ready for final physical release validation only when all of
the following are true:

- Android CI is green, including lint, unit tests, and APK assembly;
- Windows CI is green, including build, tests, publish, installer compile, and full install/repair/reconnect/uninstall lifecycle;
- Windows Driver CI is green;
- HIDMaestro bootstrap CI is green;
- the driver production gate rejects CI/test/attestation packages;
- the reproducible HLK driver payload builds;
- hot-path allocation regression tests pass;
- the Windows Receiver remains `asInvoker` and privileged virtual-controller work is isolated to the Program-Files-only LocalSystem GamepadHost;
- installer post-install verification fails closed if the unelevated Receiver cannot use the GamepadHost;
- release metadata/signing gates fail closed;
- no release artifact is produced from a `-dev` version;
- Windows app and installer Authenticode signing are mandatory;
- Android release signing is mandatory;
- SHA-256 checksum artifacts are generated.

## External release gates

These cannot be truthfully completed by source code or CI alone:

- Microsoft retail-signed driver packages returned from the Windows Hardware
  Compatibility / HLK path;
- final Windows Authenticode signing certificate/private key;
- final Android release keystore/private key;
- product LICENSE decision;
- Inno Setup licensing posture confirmation;
- clean-machine installation;
- 30-minute physical soak;
- 2-hour physical soak;
- Windows sleep/resume;
- CPU/resource review on representative hardware;
- Android battery/thermal review;
- upgrade/uninstall physical validation;
- standard-user game/XInput validation that the LocalSystem-created Xbox 360 virtual controller is visible and usable by the target game.

The release workflow deliberately blocks on the external signing/license
inputs rather than silently producing a reduced Wi-Fi/Bluetooth-only build.

## v1 scope

The following are post-v1 enhancements and are not blockers for the controller
product:

- additional controller profiles;
- advanced response curves;
- profile export/import;
- more sophisticated layout editor;
- adaptive input rate;
- additional accessibility options;
- iOS support.

Remote desktop, keyboard/mouse remote, game streaming, file transfer, media
remote, presentation remote, and cloud accounts are explicitly outside the v1
controller scope.


## Exact-commit physical approval

Final physical acceptance is bound to the tested source revision. After clean-machine install, soak, sleep/resume, resource, battery, upgrade, and uninstall validation pass, set `NATSX_PHYSICAL_RELEASE_APPROVED_SHA` to that exact full commit SHA.

A later source change intentionally invalidates the release approval until the variable is moved to the newly validated commit.

## Complete release input audit

The Production release input audit workflow reports every missing input in one
run using repository variables and secret-presence booleans. It never exposes
secret values and never changes approval, signing identities, or licenses. Its
green CI status means the audit executed, not that production is approved.
`inputs_ready` in the JSON is a configuration inventory only; the release jobs
still verify actual APK, Authenticode and driver catalog signatures. The Release
preflight runs the same inventory with `--require-ready` before any signing work.

Android `preReleaseBuild` always depends on `verifyReleaseSigning`. Missing
credentials, invalid/expired certificates and Android Debug keys/subjects are
rejected, even for a direct local `assembleRelease` invocation.
