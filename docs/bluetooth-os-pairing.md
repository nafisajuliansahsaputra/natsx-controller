# Bluetooth first-time OS pairing flow

## Principle

NATSX does not implement or store its own Bluetooth PIN.

The first Bluetooth bond is delegated to the operating-system pairing flow.
Android calls `BluetoothDevice.createBond()`; any PIN, numeric comparison, or
user confirmation remains owned by Android / Windows Bluetooth UI.

This OS bond is transport setup only. NATSX application trust remains a separate
cryptographic layer and Bluetooth controller traffic still requires the NATSX
trusted-session handshake.

## Windows pairing mode

`BluetoothRfcommServiceHost.StartPairingModeAsync()` starts the NATSX RFCOMM
service with radio discoverability enabled.

Normal background operation may use `StartAsync(radioDiscoverable: false)` so
the receiver does not stay intentionally discoverable after setup.

## Android flow

`BluetoothBondingManager.ensureBonded()`:

1. returns immediately when the selected receiver is already bonded;
2. requests an OS bond when it is not bonded;
3. waits for the platform bond state to become `BONDED`;
4. reports rejected or timed-out pairing cleanly.

The wait is deliberately blocking and must be called from a worker thread, not
the Android UI thread.

## RFCOMM gate

`BluetoothRfcommConnector.forDevice(...)` requires the selected device to
already be `BOND_BONDED`.

This keeps responsibilities explicit:

```text
OS Bluetooth bond
    -> NATSX trusted session
    -> authenticated secondary-session join
    -> RFCOMM realtime transport
```

A Bluetooth MAC address or OS bond alone never grants controller authority.
