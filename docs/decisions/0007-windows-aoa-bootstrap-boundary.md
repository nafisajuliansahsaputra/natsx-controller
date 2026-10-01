# ADR 0007 — Windows AOA bootstrap boundary

**Status:** Accepted boundary; physical bootstrap implementation pending  
**Date:** 2026-10-01

## Context

Android Open Accessory requires the USB host to send vendor control requests
`51`, `52`, and `53` to endpoint zero while the phone is still in its
normal OEM USB configuration.

Only after `START_ACCESSORY` does Android disconnect/re-enumerate as Google's
AOA VID/PID and expose the bulk data endpoints used by NATSX.

The post-AOA data path is already implemented with Microsoft's inbox WinUSB
driver and the NATSX AOA-only device interface.

The unresolved problem is pre-AOA access to an arbitrary Android phone whose
normal USB interface is commonly owned by the Windows MTP/WPD/composite stack.

## Decision

### Post-AOA path

Keep the current Microsoft WinUSB design.

NATSX binds only:

```text
USB\VID_18D1&PID_2D00
USB\VID_18D1&PID_2D01&MI_00
```

and feeds those bulk streams into the existing trusted USB session runtime.

### Pre-AOA path

Do not pretend that the post-AOA WinUSB binding solves bootstrap.

The production receiver needs a separate bootstrap component capable of sending
AOA vendor control requests to the normal-mode Android USB device without
permanently replacing its MTP/PTP function driver.

The exact driver/filter architecture remains a gated WDK prototype task.

For Windows 10 1903+ the installation direction is a **device-specific
declarative filter** using `DDInstall.Filters` / `AddFilter`, potentially
delivered by an extension INF that augments the phone's existing base driver.
NATSX must not register a class-wide MTP/WPD filter. Exact hardware matching,
filter position, signing, and uninstall behavior remain subject to physical
validation before this becomes the shipping install design.

A source-level KMDF pass-through filter prototype now exists on the dedicated
USB bootstrap branch. It exposes only a private version IOCTL and a bounded
`START_AOA` operation; user mode does not receive a generic endpoint-zero
vendor-control primitive. The prototype is not an accepted shipping design
until its attach/install scope and physical behavior are validated.

### Driverless Windows claim

Do not claim a fully driverless automatic AOA bootstrap on stock Windows.

Microsoft's user-mode `Windows.Devices.Usb` / WinUSB APIs operate on devices
that are already exposed through WinUSB. A normal Android MTP interface cannot
be assumed to satisfy that requirement.

### Rejected bootstrap shortcuts

- **ADB / USB debugging:** rejected by product requirements.
- **USB tethering:** rejected; it creates a network transport instead of direct
  controller USB.
- **Manual Zadig replacement of the phone driver:** rejected; users must not
  replace the normal phone driver by hand.
- **UsbDk as the default shipping bootstrap backend:** rejected unless future
  evidence reverses the current stability assessment.
- **libusb0 filter mode:** rejected as a default architecture.
- **Manual Android MIDI USB mode:** not equivalent to automatic USB Direct and
  would add a user-mode switch outside NATSX.

## Required properties of the bootstrap prototype

A candidate NATSX bootstrap component must prove all of the following before it
can become the exact M9 design:

1. It can access endpoint zero while the OEM phone function driver remains
   usable.
2. It sends only the AOA vendor requests required for NATSX.
3. It does not capture unrelated USB devices.
4. It does not replace the phone's normal driver permanently.
5. Driver installation/elevation occurs only during setup.
6. Normal receiver startup and realtime input require no elevation.
7. Failure cannot leave the USB/MTP stack unusable after reboot.
8. It works on supported Windows 11 builds and the user's target hardware.
9. Removal/uninstall cleanly restores the original device stack.
10. After AOA re-enumeration, ownership passes to the existing WinUSB AOA data
    backend rather than keeping a raw filter in the realtime path.

## Consequences

M9 remains intentionally incomplete.

The Android AOA protocol, authenticated USB session runtime, Smart Auto policy,
and post-AOA WinUSB data path can continue independently while the narrow
Windows bootstrap driver is prototyped and physically validated.
