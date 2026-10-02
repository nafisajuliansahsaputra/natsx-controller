# Windows retail driver signing runbook

NATSX USB Direct needs two Windows driver packages on a clean retail machine:

1. the exact OPPO A58 bootstrap extension package;
2. the exact Google Android Open Accessory WinUSB package.

The repository can build and validate the driver payload, but Microsoft retail
signing requires the Windows Hardware Developer Program / Partner Center plus
the applicable Windows Hardware Lab Kit (HLK) certification process.

## Microsoft signing policy used by NATSX

For retail production, NATSX uses the Microsoft Hardware Dev Center / Windows
Hardware Compatibility Program path. HLK-tested dashboard signing is the
production route.

Attestation signing is not accepted by the NATSX production gate. Microsoft
documents attestation signing as a testing scenario and does not allow it to be
published to Windows Update for retail audiences.

Relevant Microsoft documentation:

- Driver signing options and best practices:
  https://learn.microsoft.com/windows-hardware/drivers/dashboard/driver-signing-offerings
- Partner Center for Windows Hardware:
  https://learn.microsoft.com/windows-hardware/drivers/dashboard/
- Validate the Microsoft signature:
  https://learn.microsoft.com/windows-hardware/drivers/dashboard/code-signing-validate
- Windows HLK:
  https://learn.microsoft.com/windows-hardware/test/hlk/
- Working with extension INF files:
  https://learn.microsoft.com/windows-hardware/drivers/dashboard/submit-dashboard-extension-inf-files

## Prerequisites outside this repository

Before a production submission:

- register the organization in the Windows Hardware Developer Program;
- associate the required EV code-signing identity with the Hardware Dev Center
  account as required by Microsoft;
- install the current HLK version appropriate for the target Windows versions;
- prepare a clean Windows 11 x64 validation machine with Secure Boot enabled;
- use the exact physical OPPO A58 / CPH2577 hardware already validated by NATSX.

Do not enable TESTSIGNING for the retail certification run.

## Build the HLK payload

Build the KMDF driver first:

```powershell
nuget restore windows\driver\aoa-bootstrap\packages.config -PackagesDirectory windows\driver\aoa-bootstrap\packages -NonInteractive
msbuild windows\driver\aoa-bootstrap\Natsx.AoaBootstrap.Driver.vcxproj /m /p:Configuration=Release /p:Platform=x64
```

Then create the deterministic driver payload:

```powershell
$version = (Get-Content VERSION -Raw).Trim()
.\windows\driver\build-hlk-submission-payload.ps1 -Version $version
```

For branch CI where VERSION ends in `-dev`, CI explicitly strips only that
suffix before invoking the payload builder. The production release preflight
does not allow `-dev`.

Output:

```text
artifacts/windows-driver-hlk-payload/
  README.txt
  submission-manifest.json
  aoa-bootstrap/
    Natsx.AoaBootstrap.OPPOA58.Extension.inf
    Natsx.AoaBootstrap.Driver.sys
    Natsx.AoaBootstrap.OPPOA58.Extension.cat
  aoa-winusb/
    NatsxAoaWinUsb.inf
    NatsxAoaWinUsb.cat
```

The generated CAT files are intentionally unsigned. This is a certification
input artifact, not a shipping package.

## Hardware IDs that must not change

Bootstrap:

```text
USB\VID_22D9&PID_2764&REV_0404
```

Post-AOA WinUSB:

```text
USB\VID_18D1&PID_2D00
USB\VID_18D1&PID_2D01&MI_00
```

Device interface GUID:

```text
{4D501A6D-7D41-4F1B-91A8-CC2D5D718C21}
```

Never broaden these to class-wide USB, PID-only 2D01, OPPO PID 2765, ADB, or
generic MTP matching.

## HLK / Partner Center

Run all applicable HLK tests for the intended Windows 11 x64 product and driver
packages. The bootstrap package is an Extension-class driver package that
references the NATSX KMDF binary; follow Microsoft's current extension-INF
submission rules when constructing the HLK submission.

The post-AOA package is a scoped USBDevice/WinUSB binding. Keep it as a separate
driver package unless the current HLK/Partner Center product configuration
explicitly requires another structure.

Create and sign the HLK submission package through the supported HLK tooling,
then submit it to Partner Center / Hardware Dev Center.

Do not select an attestation-only/testing submission for the retail package.

## After Microsoft signs the packages

Download the returned production-signed driver files and stage them as:

```text
artifacts/windows-drivers-production/
  aoa-bootstrap/
    Natsx.AoaBootstrap.OPPOA58.Extension.inf
    Natsx.AoaBootstrap.Driver.sys
    Natsx.AoaBootstrap.OPPOA58.Extension.cat
  aoa-winusb/
    NatsxAoaWinUsb.inf
    NatsxAoaWinUsb.cat
```

Do not copy the top-level `submission-manifest.json` from the unsigned HLK
payload into these shipping directories.

Validate the returned package:

```powershell
.\windows\driver\validate-production-packages.ps1 `
  -BootstrapPackageDirectory artifacts\windows-drivers-production\aoa-bootstrap `
  -WinUsbPackageDirectory artifacts\windows-drivers-production\aoa-winusb
```

The validator fails unless:

- SignTool kernel-policy verification succeeds;
- the Microsoft Windows Hardware Driver Verification EKU is present;
- the attestation verification EKU is absent;
- the bootstrap CAT validates the KMDF SYS;
- exact hardware-ID scoping remains intact;
- no test certificates, PFX/P12/PVK files, or CI-test artifacts exist.

## Publish the immutable driver bundle

Create one ZIP whose root contains exactly the two directories above.

Calculate SHA-256:

```powershell
(Get-FileHash .\natsx-production-drivers.zip -Algorithm SHA256).Hash
```

Host that immutable ZIP at a release-controlled URL and configure repository
variables:

```text
NATSX_PRODUCTION_DRIVER_BUNDLE_URL
NATSX_PRODUCTION_DRIVER_BUNDLE_SHA256
```

The GitHub release workflow downloads the exact pinned bundle, verifies its
SHA-256, validates the Microsoft signatures again, and only then allows the
feature-complete Windows installer to build.

## Clean-machine acceptance

Before tagging production, install the final NATSX installer on a clean retail
Windows machine with:

- Secure Boot enabled;
- TESTSIGNING disabled;
- no NATSX test certificates;
- no preinstalled NATSX bootstrap/WinUSB package.

Acceptance requires:

- setup completes without manual driver steps;
- HIDMaestro bootstrap completes during elevated setup;
- OPPO A58 can enter AOA mode without ADB or USB tethering;
- NATSX WinUSB binding is scoped to the expected AOA interface;
- Windows Ethernet/LAN remains unaffected;
- Wi-Fi/Bluetooth remain valid fallback candidates;
- uninstall removes NATSX driver packages without forced Driver Store deletion;
- normal uninstall preserves trust/pairing state as documented;
- explicit purge removes trust only when requested.

Record evidence under `docs/testing/results/`.
