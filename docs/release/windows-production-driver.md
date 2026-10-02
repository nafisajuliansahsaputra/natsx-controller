# Windows production USB driver release gate

NATSX USB Direct depends on two Windows driver packages:

1. the hardware-ID-scoped KMDF AOA bootstrap lower filter for the validated OPPO A58 no-ADB MTP state;
2. the scoped AOA WinUSB binding used after Android re-enumerates into Google AOA mode.

## Retail signing requirement

CI test-signed packages are development artifacts only and must never be shipped.

For a retail production release, the packages must be submitted through the Microsoft Windows Hardware Developer Program and returned Microsoft-signed through the production/HLK dashboard path. Current Microsoft guidance treats attestation signing as a testing scenario rather than the retail-production path.

The NATSX production validator therefore requires the catalog signer to expose the Windows Hardware Driver Verification EKU:

1.3.6.1.4.1.311.10.3.5

and rejects the attestation verification EKU:

1.3.6.1.4.1.311.10.3.5.1

It also runs SignTool kernel-policy verification, verifies that the bootstrap catalog covers the KMDF binary, and rejects CI-test certificates/manifests.

## Expected bundle layout

Create one ZIP whose root contains:

    aoa-bootstrap/
      Natsx.AoaBootstrap.OPPOA58.Extension.inf
      Natsx.AoaBootstrap.Driver.sys
      Natsx.AoaBootstrap.OPPOA58.Extension.cat

    aoa-winusb/
      NatsxAoaWinUsb.inf
      NatsxAoaWinUsb.cat

Do not include private keys, PFX/P12/PVK files, CI test certificates, or test-package manifests marked non-shipping.

## Local validation

Run the production validator against both package directories before building the installer.

A valid result proves the repository's automated gate accepted the exact hardware scoping, catalog membership, and Microsoft retail signature posture. Physical clean-machine install/uninstall validation is still required before tagging.

## Release workflow handoff

Host the approved production driver ZIP at a stable HTTPS location and configure these GitHub repository variables:

- NATSX_PRODUCTION_DRIVER_BUNDLE_URL
- NATSX_PRODUCTION_DRIVER_BUNDLE_SHA256

The release workflow downloads the bundle, verifies its SHA-256, expands it into artifacts/windows-drivers-production, and the installer build validates the Microsoft signatures before packaging.

If either variable is missing, the production release workflow fails. There is no automatic fallback that silently produces a Wi-Fi/Bluetooth-only production installer.

## Installer behavior

The production installer:

- validates the driver bundle at build time;
- stages both packages with PnPUtil while Setup is elevated;
- keeps Windows Ethernet/LAN configuration untouched;
- verifies the HIDMaestro virtual-controller backend;
- launches the Receiver as the original non-elevated user.

Normal uninstall attempts to remove the NATSX USB packages from the Driver Store before application files are deleted. Pairing/trust data remains preserved unless the user explicitly requests a full purge.
