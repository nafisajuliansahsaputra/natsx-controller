# NATSX AOA bootstrap driver prototype

This directory is a **non-installing prototype** for the unresolved pre-AOA
Windows bootstrap boundary.

Current scope:

- KMDF pass-through filter skeleton;
- sideband control device `\\.\NatsxAoaBootstrap` for user-mode IOCTLs,
  so custom control requests do not depend on WPD/WUDF forwarding;
- reserved driver service name: `NatsxAoaBootstrap`;
- version IOCTL;
- bounded `START_AOA` IOCTL that performs only the canonical Android Open
  Accessory endpoint-zero sequence;
- all unknown device-control requests are forwarded to the lower stack;
- the receiver has a ConfigMgr/CreateFile/DeviceIoControl client for this
  private interface;
- the no-ADB OPPO A58 target is now confirmed as
  `USB\\VID_22D9&PID_2764&REV_0404`, class `WPD`, service
  `WUDFWpdMtp`, with the USB root hub as its parent;
- a **source-only** extension-INF draft now matches that exact ADB-off device
  revision and declares `NatsxAoaBootstrap` as a device-specific lower filter;
- the earlier ADB-on `PID_2765` composite layout is recorded only as a
  diagnostic comparison and is not a NATSX dependency;
- the ADB-off device reports `WinUsb` in both `LowerFilters` and
  `CompoundLowerFilters`, which is normal for the Windows USB-MTP stack;
- the INF remains intentionally outside the install/package build because a
  second generic lower filter has no guaranteed relative order against WinUSB
  unless the base stack exposes usable filter levels; the effective PnP stack
  must be captured and the ordering/compatibility plan validated first;
- no driver is loaded by normal application runtime unless the prototype
  package is explicitly installed for physical validation.

The driver intentionally owns the privileged endpoint-zero sequence instead of
exposing an arbitrary user-mode vendor-control API. User mode can only ask the
sideband control device to report its protocol version or perform the narrowly
scoped NATSX AOA bootstrap operation.

The sideband change is deliberate: the physical OPPO A58 no-ADB stack is
`WpdUpFltr -> WUDFRd -> WINUSB -> ACPI -> USBHUB3`, and the direct user-mode
WinUSB probe fails at `WinUsb_Initialize` with `ERROR_INVALID_FUNCTION`.
Keeping custom IOCTLs off the PnP data stack avoids depending on upper WPD/WUDF
drivers to forward requests they do not own.

A CI-gated physical-validation package builder now exists at
`build-oppo-a58-test-package.ps1`. It hard-fails if the Models section broadens
beyond the exact validated OPPO A58 no-ADB `VID/PID/REV`, materializes the
KMDF token, runs `InfVerif /w /v`, and emits a manifest with SHA-256 hashes.
The output remains unsigned and is not installed automatically. The repository
also contains guarded install/rollback harnesses. Both default to read-only
preflight behavior; the install path refuses missing/invalid signatures and the
rollback path refuses to use forced driver removal.

The source prototype is not sufficient to mark M9 complete. It still requires:

1. capture the existing upper/lower/compound filter metadata for the ADB-off
   OPPO A58 WPD device;
2. validate that the device-specific lower-filter placement can issue the
   bounded AOA control sequence while `WUDFWpdMtp` remains healthy;
3. proof that the OEM MTP/PTP path still works before and after bootstrap;
4. uninstall/reboot recovery validation;
5. receiver integration only after those checks pass.

Do not install this prototype on a production machine yet.


## Test-signed physical-validation artifact

Windows Driver CI now creates an ephemeral, non-shipping test-signed OPPO A58
package after the static INF gate passes. The CI signing step:

- embeds a SHA-256 test signature in the KMDF SYS;
- creates the catalog with Inf2Cat for supported Windows 10/11 x64 targets;
- signs the catalog with the same ephemeral test identity;
- exports only the public certificate;
- records certificate/file SHA-256 values in `package-manifest.json`;
- never exports the private signing key.

The physical machine transition is documented in
`docs/testing/windows-kernel-test-mode-transition.md`. Secure Boot/BitLocker
and TESTSIGNING changes remain explicit test-machine operations and are not
performed automatically by the normal build or receiver runtime.
