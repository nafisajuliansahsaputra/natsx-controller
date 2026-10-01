# ADR 0006 — USB Direct via Android Open Accessory and UsbDk host access

**Status:** Provisional  
**Date:** 2026-10-01

## Context

NATSX Controller needs a wired transport that:

- carries controller data directly over USB;
- does not require USB tethering;
- does not require ADB or Android USB debugging;
- does not alter the PC's Ethernet/LAN internet route;
- can join the same authenticated logical controller session as Wi-Fi and Bluetooth;
- can become the preferred transport after a short stabilization window.

Android public application APIs do not allow a normal third-party app to expose an arbitrary custom USB gadget function. Android Open Accessory (AOA) is the supported public model where the external computer acts as USB host and the Android device enters accessory mode.

The Windows host must perform AOA control requests before the phone has re-enumerated into accessory mode. Requiring users to replace the phone's normal Windows driver manually is not acceptable product UX.

## Decision

### Android side

Use Android Open Accessory mode.

The Windows receiver identifies itself with:

```text
manufacturer = NATSX
model        = NATSX Controller Windows Receiver
description  = Low-latency NATSX Controller USB transport
version      = 1
uri          = https://natsx.my.id
serial       = natsx-controller
```

Android declares `android.hardware.usb.accessory` as optional and filters for the NATSX manufacturer/model.

After the system grants accessory permission, the app opens the `UsbAccessory` through `UsbManager.openAccessory()` and uses the resulting file descriptor as a bidirectional byte stream.

### AOA bootstrap

Windows performs the standard AOA endpoint-zero vendor requests:

```text
51 GET_PROTOCOL
52 SEND_STRING
53 START_ACCESSORY
```

After START_ACCESSORY, Windows waits for USB re-enumeration.

Communication-capable AOA identities are Google VID `0x18D1` with product IDs:

```text
0x2D00 accessory
0x2D01 accessory + adb
0x2D04 accessory + audio
0x2D05 accessory + audio + adb
```

NATSX uses only the accessory data interface. The presence of an ADB-capable product ID on a developer device does not make ADB a dependency.

### Windows host backend

AOA is accepted as the Android/USB protocol topology. The exact Windows native host backend remains **unresolved for release**.

UsbDk is retained only as a prototype/reference candidate because it can enumerate arbitrary USB devices and perform raw redirect/control/bulk I/O without requiring permanent manual WinUSB replacement. It is **not approved as the shipping default** until physical Windows 11 validation proves acceptable stability.

The shipping backend must provide the same application-owned abstraction for device enumeration, endpoint-zero control transfers, re-enumeration, and bulk I/O.

Reason:

- AOA bootstrap must access the Android USB device before it re-enumerates as a dedicated accessory interface;
- manual Zadig/driver replacement is rejected;
- UsbDk is designed for user-mode direct USB access through a Windows filter/redirector model.

The native driver/helper dependency is setup/installer work and must never be installed or elevated from the realtime input path.

The repository owns an abstraction around native USB access so protocol, Smart Connection, and controller-state code do not depend directly on UsbDk types.

Before release packaging, the exact UsbDk version, artifact hash, signature verification, and license notices must be pinned in a dependency update/ADR.

### Stream framing

AOA bulk endpoints carry the same NATSX Protocol v1 frames as Wi-Fi and Bluetooth.

Because USB bulk transfer boundaries are not application-message boundaries, each frame is prefixed with a 2-byte little-endian frame length:

```text
uint16_le frame_length
ProtocolFrame bytes
```

### Authentication/session semantics

USB does not establish a separate trusted relationship.

After AOA data transport is available, USB joins the existing logical controller session by proving possession of the current session key and matching the current Session ID / trusted peers.

USB therefore preserves the global realtime sequence and does not recreate the Windows virtual controller during takeover.

### Smart Auto preference

When health is comparable:

```text
USB Direct > Wi-Fi > Bluetooth
```

USB must stabilize before preferred takeover. The current competitive default is 500 ms.

## Rejected approaches

### USB tethering

Rejected. It changes networking semantics and is not a direct controller transport.

### ADB / USB debugging

Rejected. It requires developer settings and authorization unsuitable for normal users.

### Zadig/manual permanent WinUSB replacement

Rejected. Product operation must not require manual replacement of the phone's normal driver.

### Android USB host mode

Rejected for phone-to-PC direct connection. The required topology is PC host -> Android accessory.

### Custom Android gadget function

Rejected for the normal app because exposing arbitrary gadget functions requires privileges outside the standard third-party Android application model.

## Consequences

Positive:

- direct wired controller path;
- no dependency on IP networking;
- Ethernet/LAN remains independent;
- no ADB requirement;
- standard Android application-side accessory API;
- same NATSX session/protocol semantics as wireless transports.

Costs:

- Windows setup gains a native USB filter/host dependency;
- physical-device compatibility must be tested across Android OEMs;
- charge-only cables must be detected and reported clearly;
- AOA temporarily replaces MTP while accessory mode is active.


## Safety follow-up

Current upstream libusb Windows guidance discourages UsbDk because of reported stability issues, and public UsbDk issue reports include serious failures on recent Windows builds. Therefore:

- no UsbDk installer/runtime is added to the repository yet;
- no driver install is performed by the receiver;
- physical backend validation remains an open M9 task;
- the transport/session layers continue independently behind interfaces;
- the ADR must be superseded or promoted to Accepted only after the native backend and its installation model pass physical compatibility testing.
