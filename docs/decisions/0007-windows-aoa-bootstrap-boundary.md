# ADR 0007 — Windows AOA bootstrap boundary

**Status:** Accepted implementation boundary; physical prototype validated, production signing/release pending  
**Date:** 2026-10-01

## Context

Android Open Accessory requires the USB host to send vendor control requests
`51`, `52`, and `53` to endpoint zero while the phone is still in its
normal OEM USB configuration.

Only after `START_ACCESSORY` does Android disconnect/re-enumerate as Google's
AOA VID/PID and expose the bulk data endpoints used by NATSX.

The post-AOA data path is already implemented with Microsoft's inbox WinUSB
driver and the NATSX AOA-only device interface.

The validated OPPO A58 path now proves that pre-AOA endpoint-zero access can
coexist with the OEM Windows MTP/WPD stack when NATSX uses a narrowly scoped
device lower filter and submits only bounded AOA vendor URBs to the physical
USB PDO. Generalization to other Android hardware remains vendor/device gated.

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

The accepted OPPO A58 implementation uses a **device-specific declarative
lower filter** delivered through an extension INF. The physical validation
target is exactly:

```text
USB\VID_22D9&PID_2764&REV_0404
```

The filter augments the existing WPD/MTP stack; it does not replace the OEM
function driver and it does not register a class-wide MTP/WPD filter.

The live validated normal-mode stack is:

```text
WpdUpFltr
-> WUDFRd
-> NatsxAoaBootstrap
-> WINUSB
-> ACPI
-> USBHUB3
```

The filter exposes a private sideband control device
`\\.\NatsxAoaBootstrap`. User mode receives only bounded NATSX bootstrap
operations; it never receives a generic USB vendor-control primitive.

Physical builds 3 through 7 isolated the correct endpoint-zero mechanism:

- WDFUSBDEVICE specialization at this lower-filter position returns
  `STATUS_INVALID_DEVICE_REQUEST`.
- Sending `IOCTL_INTERNAL_USB_SUBMIT_URB` through the filter's normal
  next-lower I/O target also returns `STATUS_INVALID_DEVICE_REQUEST`.
- Sending the same bounded USB vendor URB to the **physical USB PDO** obtained
  from `WdfDeviceWdmGetPhysicalDevice` succeeds.

Build 7 proved read-only AOA `GET_PROTOCOL` physically with:

```text
submit status   = 0x00000000
USB status      = 0x00000000
bytes returned  = 2
AOA version     = 2
```

Build 8 therefore performs the complete bounded sequence on that same physical
PDO path:

```text
GET_PROTOCOL (51)
SEND_STRING (52) x 6
START_ACCESSORY (53)
```

The physical OPPO A58 successfully re-enumerates from the normal OEM identity
to:

```text
USB\VID_18D1&PID_2D00\<same device serial>
```

The shipping package must preserve the same narrow matching, bounded IOCTL
surface, rollback behavior, and no-ADB/no-tethering requirements. Production
driver signing and installer packaging remain release-engineering work; the
ephemeral CI certificate used for physical validation is not a shipping trust
model.

### Driverless Windows claim

Do not claim a fully driverless automatic AOA bootstrap on stock Windows.

Microsoft's user-mode `Windows.Devices.Usb` / WinUSB APIs operate on devices
that are exposed through WinUSB. A normal Android MTP interface cannot be
assumed to provide an application-usable WinUSB handle merely because
`Winusb.sys` appears in its kernel stack.

The first OPPO A58 no-ADB capture is useful because its effective stack is
`WpdUpFltr -> WUDFRd -> WINUSB -> ACPI -> USBHUB3`. Before mutating that
stack with another kernel filter, NATSX now performs a bounded user-mode probe
against the existing `GUID_DEVINTERFACE_USB_DEVICE` path. The probe attempts
`WinUsb_Initialize` and sends only AOA `GET_PROTOCOL` (request 51). It never
sends the AOA identity strings or `START_ACCESSORY`.

The OPPO A58 no-ADB probe failed at `WinUsb_Initialize` with Win32 error 1
(`ERROR_INVALID_FUNCTION`). For this validated Windows/MTP stack, the inbox
WinUSB lower filter is therefore not directly usable as the NATSX user-mode
AOA bootstrap path. The kernel bootstrap boundary remains required.

Because that kernel filter may sit beneath WPD/WUDF components that reject
unknown custom IOCTLs, the prototype's user-mode control channel is now a
separate KMDF sideband control device (`\\.\NatsxAoaBootstrap`). The filter
itself remains pass-through in the PnP stack and performs only the bounded AOA
endpoint-zero sequence when commanded through that sideband channel.

This conclusion is device/stack-specific and must not be generalized to other
Android vendors without their own validation.

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

The Windows bootstrap mechanism is no longer the unresolved M9 blocker on the
validated OPPO A58 hardware. Physical validation now proves normal MTP coexistence,
bounded AOA bootstrap, successful AOA re-enumeration, scoped WinUSB discovery,
and usable bulk IN/OUT stream opening.

M9 still requires clean transport-session/gameplay validation, charge-only cable
diagnostics, Ethernet/LAN non-interference verification, unplug/replug Smart Auto
handover validation, and release-signing decisions before it is complete.

The raw lower filter is strictly a **pre-AOA bootstrap component**. Once Android
re-enumerates into AOA mode, realtime controller traffic uses the scoped
Microsoft WinUSB data backend and the authenticated NATSX USB session runtime.
