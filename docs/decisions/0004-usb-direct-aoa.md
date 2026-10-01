# ADR 0004 — USB Direct uses Android Open Accessory mode

**Status:** Accepted for transport architecture; Windows bootstrap prototype required before production sign-off  
**Date:** 2026-10-01

## Context

NATSX Controller requires a wired controller path with these non-negotiable
properties:

- Android phone connected to the Windows PC by a normal data-capable USB cable;
- controller traffic travels directly over USB;
- no USB tethering;
- no ADB;
- no Android USB debugging;
- no cloud relay;
- the PC may continue using Ethernet/LAN normally;
- USB may remain READY as a warm candidate and must not create a second logical
  controller session.

On a stock Android phone the application cannot arbitrarily expose a custom USB
gadget function. Android's public accessory API is designed for the opposite
topology: the external accessory/PC is the USB host and Android becomes the USB
accessory/device.

Android Open Accessory (AOA) defines the host-side vendor control requests that
switch a compatible Android device into accessory mode. After re-enumeration,
AOA v1 exposes a generic accessory bulk data path.

## Decision

### Android mode

Use **Android Open Accessory v1 generic accessory communication**.

Windows is the physical USB host. Android is the USB accessory/device.

The Android application uses:

- `UsbManager`;
- `UsbAccessory`;
- `UsbManager.openAccessory()`;
- the resulting file descriptor as bidirectional input/output streams.

Android declares optional `android.hardware.usb.accessory` support.

### No ADB mode

NATSX does not use the AOA accessory+ADB product mode.

Production USB Direct targets the accessory-only AOA identity and does not
require Android Developer Options or USB debugging.

ADB is not a bootstrap mechanism, transport, recovery path, or diagnostic
dependency.

### No network mode

USB Direct does not create or require a USB Ethernet/RNDIS/NCM/tethering
interface.

It therefore does not intentionally modify:

- Windows default routes;
- Ethernet/LAN connectivity;
- Wi-Fi connectivity;
- DNS configuration.

USB controller traffic is USB bulk data, not IP traffic.

### Protocol/session behavior

USB carries the same NATSX protocol semantics as Wi-Fi and Bluetooth.

For an already active logical controller session, USB joins as a secondary
transport by proving possession of the current session key:

```text
authenticated SESSION_READY
authenticated TRANSPORT_READY(UsbDirect)
fresh GAMEPAD_STATE
STABILIZING -> READY
Smart Connection may select USB
```

The Android `SessionSequence` is never reset merely because USB appears.

### Stream framing

USB bulk transport uses the same bounded 2-byte little-endian length-prefixed
stream framing already proven for RFCOMM:

```text
uint16 frameLength
NATSX protocol frame
```

The logical framing contract belongs to the transport layer; AOA packet/bulk
boundaries are not treated as application-message boundaries.

### Windows bootstrap

The difficult part is switching a normal Android USB configuration (commonly
MTP/composite) into AOA mode.

Plain user-mode WinUSB APIs are **not** accepted as the bootstrap assumption,
because they require the target device/interface to already be exposed through
the WinUSB driver stack.

A system-wide generic USB capture/filter dependency is also **not** accepted as
the production baseline. In particular, UsbDk demonstrates that generic direct
USB capture is technically possible, but it installs into the USB stack
globally and is too broad a reliability/security dependency for the default
NATSX controller installation.

The production Windows bootstrap therefore has this explicit implementation
gate:

1. prototype a narrowly scoped NATSX Windows USB bootstrap component;
2. prove it can issue the required AOA endpoint-0 vendor requests to a selected
   Android phone without replacing MTP permanently;
3. prove normal USB ownership is restored after close/failure;
4. prove Windows 10/11 stability and sleep/resume behavior;
5. only then freeze/sign/package that driver path.

The bootstrap component may be KMDF if device-level control transfers cannot be
implemented safely in user mode.

### Post-AOA data access

After the phone re-enumerates as the AOA accessory identity, the Windows
transport reads the accessory bulk endpoint pair and feeds the common NATSX
stream protocol.

The exact post-enumeration client-driver choice (project driver vs inbox
WinUSB-compatible binding) is implementation detail as long as it does not
weaken the requirements above.

## Cable/data detection

A cable is not considered usable merely because charging begins.

USB Direct reports a data-path failure when, within the connection deadline:

- no compatible Android USB device enumerates;
- AOA negotiation cannot complete;
- the accessory identity never re-enumerates;
- required bulk endpoints are absent.

This provides a clean charge-only/non-data-cable error without pretending that
electrical charging proves data connectivity.

## Safety

USB does not become authoritative on physical attach.

Required lifecycle:

```text
AVAILABLE
-> CONNECTING
-> AUTHENTICATING
-> STABILIZING
-> READY
-> ACTIVE (only when Smart Connection commits)
```

A failed bootstrap or cable removal affects only the USB candidate. Wi-Fi,
Bluetooth, the logical controller session, and the virtual Xbox controller
remain alive.

## Rejected approaches

### USB tethering

Rejected because controller transport must not depend on or alter network
routing.

### ADB / USB debugging

Rejected because production controller use must not require Developer Options,
debug authorization, or an ADB daemon.

### Android USB host mode

Rejected for the normal phone-to-PC cable topology. The PC is the host; the
Android app cannot make a normal Windows PC behave as a USB peripheral.

### Generic UsbDk dependency as the default shipping path

Rejected for the default architecture despite its technical ability to provide
exclusive direct USB access. Its system-wide USB filter/capture model is too
broad for the reliability target of this product. A narrowly scoped NATSX
bootstrap must be prototyped instead.

## Consequences

Positive:

- stock Android public API on the phone side;
- no developer mode;
- no ADB;
- no tethering;
- controller traffic remains independent of LAN/Ethernet;
- same trusted session and global sequence model as other transports.

Costs:

- Windows needs a real USB bootstrap/driver engineering milestone;
- hardware validation is mandatory across multiple Android vendors;
- driver signing/installer work becomes part of release engineering.

## Implementation gate

This ADR freezes the **transport architecture**, not the Windows driver
prototype result.

Do not mark the Windows-side USB implementation complete until a prototype has
successfully switched at least one real stock Android phone into AOA mode and
completed authenticated NATSX traffic.
