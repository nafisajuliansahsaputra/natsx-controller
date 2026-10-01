# Windows kernel test-mode transition for OPPO A58 validation

Status: physical-validation procedure only.

This procedure exists because the validated OPPO A58 no-ADB stack requires the
KMDF pre-AOA bootstrap boundary, and the development filter must be loaded on a
Windows x64 machine whose normal security posture currently has:

- Secure Boot enabled;
- VBS/HVCI (Memory Integrity) enabled;
- BCD TESTSIGNING disabled;
- BitLocker protection enabled on the system drive.

Do not use this procedure on an unrelated machine or on a production deployment.

## Safety rules

- Keep HVCI enabled. The validation driver is embedded test-signed specifically
  so HVCI does not need to be disabled.
- Do not clear Secure Boot keys.
- Do not disable TPM.
- Do not decrypt the BitLocker drive.
- Suspend BitLocker only for the firmware/boot-policy transition window.
- Keep the BitLocker recovery key backed up outside the test machine.
- Never paste recovery passwords into repository files, issues, logs, or chat
  transcripts used as engineering records.
- Do not install the filter before the signed package, certificate, manifest,
  target hardware ID, and baseline checks pass.
- Restore Secure Boot, TESTSIGNING, BitLocker, and certificate trust after the
  physical validation session.

## Phase A - obtain the exact signed validation artifact

Use the artifact produced by the Windows Driver CI for the exact commit being
validated.

The artifact must contain:

- `artifacts/oppo-a58-test-package/Natsx.AoaBootstrap.Driver.sys`;
- `artifacts/oppo-a58-test-package/Natsx.AoaBootstrap.OPPOA58.Extension.inf`;
- `artifacts/oppo-a58-test-package/natsx.aoabootstrap.oppoa58.extension.cat`;
- `artifacts/oppo-a58-test-package/Natsx.AoaBootstrap.CI-Test.cer`;
- `artifacts/oppo-a58-test-package/package-manifest.json`.

The manifest must say:

- `ShippingPackage = false`;
- `TargetHardwareId = USB\\VID_22D9&PID_2764&REV_0404`;
- `ADBRequired = false`;
- `InfVerif = passed`;
- `SigningMode = ephemeral-ci-test`.

## Phase B - preflight before any security transition

Run from elevated PowerShell:

```powershell
.\windows\eng\inspect-driver-signing-posture.ps1 -AsJson
.\windows\eng\capture-usb-bootstrap-baseline.ps1 -OutputPath .\usb-bootstrap-baseline.json
```

Stop if:

- the phone is not in the exact no-ADB OPPO A58 target state;
- MTP is already unhealthy;
- the machine does not have a usable BitLocker recovery method available;
- an unexpected USB/network change is already present.

Before Phase C, run the aggregate read-only gate:

```powershell
.\windows\eng\preflight-windows-test-mode-transition.ps1 `
  -PackageDirectory "<artifact>\artifacts\oppo-a58-test-package" `
  -AsJson
```

Proceed only when `ReadyForBitLockerSuspendGate` is `true` and every
individual check is `true`. This gate verifies the exact phone target, no ADB,
valid/trusted artifact signatures, Secure Boot currently enabled, TESTSIGNING
currently off, VBS/HVCI running, and BitLocker protection/encryption state.

The signing-posture inspector intentionally reports only BitLocker status and
protector types; it does not emit recovery passwords.

## Phase C - suspend BitLocker before firmware changes

The system drive must remain encrypted; only protection is suspended.

From elevated PowerShell:

```powershell
Suspend-BitLocker -MountPoint "C:" -RebootCount 0
Get-BitLockerVolume -MountPoint "C:"
```

Confirm protection is suspended before continuing.

Then run the dedicated read-only gate:

```powershell
.\windows\eng\verify-bitlocker-suspend-gate.ps1 -AsJson
```

Proceed only when `ReadyForSecureBootDisableGate` is `true`. The expected
state is: Secure Boot still enabled, TESTSIGNING still off, VBS/HVCI still
running, BitLocker protection suspended, and the system drive still fully
encrypted.

## Phase D - disable Secure Boot manually

Reboot into the machine firmware/UEFI settings and disable **Secure Boot only**.

Do not:

- clear Secure Boot PK/KEK/db keys;
- disable TPM;
- reset the TPM;
- change unrelated firmware settings.

Boot back into Windows.

Before changing BCD, run:

```powershell
.\windows\eng\verify-secure-boot-disabled-gate.ps1 -AsJson
```

Proceed only when `ReadyForTestSigningEnableGate` is `true`. The expected
state is: Secure Boot disabled, TESTSIGNING still off, VBS/HVCI still running,
BitLocker protection still suspended, and the system drive still fully
encrypted.

## Phase E - enable Windows TESTSIGNING

From elevated PowerShell:

```powershell
bcdedit /set testsigning on
```

Restart Windows.

After reboot, run the dedicated aggregate gate:

```powershell
.\windows\eng\verify-test-signing-enabled-gate.ps1 `
  -PackageDirectory "<artifact>\artifacts\oppo-a58-test-package" `
  -AsJson
```

Proceed only when `ReadyForPhysicalFilterInstallGate` is `true`. The gate
requires Secure Boot disabled, TESTSIGNING on, VBS/HVCI still running,
BitLocker protection still suspended while the volume remains fully encrypted,
the OPPO A58 still in the exact healthy no-ADB WPD/MTP state, and the exact
trusted CI package signatures still valid and manifest-bound.

## Phase F - trust only the artifact's ephemeral public test certificate

Extract the CI artifact without changing its contents.

Run the trust harness from elevated PowerShell:

```powershell
.\windows\driver\aoa-bootstrap\trust-oppo-a58-ci-test-certificate.ps1 `
  -PackageDirectory "<artifact>\artifacts\oppo-a58-test-package"
```

The first run is preflight-only.

Before trust is allowed, the harness also verifies that the SYS and CAT hashes
match the manifest and that both Authenticode signer thumbprints equal the
artifact certificate thumbprint. An untrusted test certificate may initially
report `UnknownError`; signer identity must still match exactly. Only after
that identity check passes should the command be repeated with the explicit
`-Trust` switch. After import, both signatures must re-evaluate as `Valid`.

This certificate is for the exact CI validation artifact and is not a
production publisher identity.

## Phase G - physical filter attach

First run the install harness without `-Install`:

```powershell
.\windows\driver\aoa-bootstrap\install-oppo-a58-test-filter.ps1 `
  -PackageDirectory "<artifact>\artifacts\oppo-a58-test-package"
```

Only after the preflight output is reviewed may the explicit physical install be
run from elevated PowerShell with:

```powershell
.\windows\driver\aoa-bootstrap\install-oppo-a58-test-filter.ps1 `
  -PackageDirectory "<artifact>\artifacts\oppo-a58-test-package" `
  -Install `
  -IUnderstandThisRestartsTheUsbDevice
```

Do not trigger START_AOA until the post-install PnP stack and MTP behavior have
been reviewed.

## Phase H - recovery / cleanup order

After physical validation:

1. remove the NATSX prototype package with
   `uninstall-oppo-a58-test-filter.ps1`;
2. verify OPPO A58 returns to `WUDFWpdMtp / wpdmtp.inf`;
3. remove the ephemeral NATSX CI test certificate from LocalMachine Trusted
   Publishers and Trusted Root after recording its manifest thumbprint;
4. run `bcdedit /set testsigning off` from elevated PowerShell;
5. reboot;
6. re-enable Secure Boot in UEFI without clearing/replacing its key databases;
7. boot Windows and verify Secure Boot is enabled and TESTSIGNING is off;
8. resume BitLocker with `Resume-BitLocker -MountPoint "C:"`;
9. verify BitLocker protection, HVCI, Secure Boot, MTP, Ethernet routes, and
   normal Windows boot behavior.

If any stage behaves unexpectedly, stop advancing and restore the previous
known-good state before continuing.
