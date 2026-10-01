# USB Direct baseline

## Selected topology

```text
Windows PC (USB host)
  -> AOA endpoint-zero negotiation
  -> Android re-enumerates in accessory mode
  -> USB bulk IN/OUT
  -> 2-byte length-prefixed NATSX Protocol v1 frames
  -> trusted logical controller session
```

The transport is local and does not create a network interface.

## Android attachment

The app declares optional USB accessory support and listens for:

```text
NATSX
NATSX Controller Windows Receiver
```

The app must receive USB permission before opening the accessory.

## Windows phases

1. detect a physical Android USB device through the native host backend;
2. issue AOA GET_PROTOCOL;
3. send NATSX accessory identity strings;
4. issue AOA START_ACCESSORY;
5. wait for device re-enumeration;
6. select the first accessory bulk IN/OUT interface;
7. start NATSX authenticated session-join traffic;
8. enter STABILIZING;
9. receive fresh authenticated full-state traffic;
10. enter READY;
11. Smart Connection may prefer USB after the configured stabilization window.

## Cable failure classification

Implementation must distinguish at least:

- no USB device detected;
- device detected but AOA unsupported;
- AOA start requested but no accessory re-enumeration;
- accessory present but no bulk endpoints;
- bulk I/O failed/disconnected.

A charge-only cable normally presents as no usable USB data device and must not be confused with authentication failure.

## Windows network independence

AOA + native USB bulk transfer does not create or require an IP network adapter. The receiver must not modify routing tables, interface metrics, DNS, tethering settings, or the PC's Ethernet/Wi-Fi connectivity.
