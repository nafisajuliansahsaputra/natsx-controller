# USB bootstrap diagnostics

`UsbAoaBootstrapCoordinator` owns the transition from a physical USB bootstrap
candidate into the already-implemented AOA WinUSB data path.

## Result states

```text
AccessoryAlreadyReady
AccessoryStarted
BootstrapBackendUnavailable
BootstrapDriverMissing
BootstrapRequiresElevation
NoDataDeviceDetected
AoaUnsupported
AccessoryReenumerationTimeout
BootstrapFailed
```

These states are deliberately distinct.

For example, an AOA protocol failure must not be shown as a charge-only cable
error, and a missing bootstrap driver must not be shown as an authentication
failure.

## No-data / charge-only semantics

Windows cannot prove that a physically connected cable is charge-only if no USB
data device enumerates at all.

Therefore `NoDataDeviceDetected` means exactly:

> the bootstrap backend is operational, but no USB data device is visible.

The receiver UI may explain that this commonly means a charge-only cable, USB
data disabled on the phone, a bad cable/port, or a disconnected phone. It must
not claim certainty about the physical cable.

## Re-enumeration rule

Sending AOA `START_ACCESSORY` is not success by itself.

The coordinator waits for the scoped NATSX AOA WinUSB interface to appear.
Only then does it return `AccessoryStarted`.

The default re-enumeration timeout is 5 seconds with 100 ms polling.

## Backend abstraction

The coordinator depends on two independent boundaries:

- `IUsbAoaBootstrapDeviceProvider`: pre-AOA endpoint-zero access;
- `IAoaAccessoryDataBackend`: post-AOA WinUSB enumeration/open.

This keeps experimental Windows bootstrap code out of the trusted session,
Smart Connection, and realtime input layers.
