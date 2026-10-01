# Windows WinUSB backend for AOA data mode

## Scope

This backend starts **after** Android has already switched into Android Open
Accessory mode.

It deliberately does not attach to the phone's normal MTP/PTP interface.

## Supported identities

V1 binds only the AOA data interface for:

```text
USB\VID_18D1&PID_2D00
USB\VID_18D1&PID_2D01&MI_00
```

The second form is important: on `2D01`, interface `MI_00` is the accessory
data interface while the separate ADB interface is not claimed by NATSX.

## Driver model

The development INF selects Microsoft's inbox `WinUSB.sys` and exposes the
device interface GUID:

```text
{4D501A6D-7D41-4F1B-91A8-CC2D5D718C21}
```

The application uses `Windows.Devices.Usb.UsbDevice` to enumerate that
interface and opens the first bulk IN and bulk OUT pipes.

No custom kernel-mode I/O path is used for the post-AOA realtime data path.

Production packaging still requires a properly signed driver package/catalog
and release validation.

## Runtime handoff

```text
WinUsbAoaAccessoryBackend
 -> WinUsbAoaAccessoryConnection
 -> bulk IN / bulk OUT streams
 -> USB trusted secondary-session join
 -> UsbControllerTransport
 -> Smart Connection
```

## Important unresolved boundary

This does not solve AOA bootstrap from the phone's normal USB configuration.

AOA requires vendor control requests 51/52/53 on endpoint zero before Android
re-enumerates into the Google accessory VID/PID. Microsoft's WinUSB user-mode
APIs require the target interface to already use WinUSB, so they cannot safely
be assumed to control an arbitrary OEM MTP phone interface.

Therefore the physical USB task remains open until the bootstrap method is
validated on supported Windows versions without replacing the normal phone
driver or depending on ADB/tethering.
