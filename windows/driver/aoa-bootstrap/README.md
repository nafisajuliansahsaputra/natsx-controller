# NATSX AOA bootstrap driver prototype

This directory is a **non-installing prototype** for the unresolved pre-AOA
Windows bootstrap boundary.

Current scope:

- KMDF pass-through filter skeleton;
- private NATSX device interface;
- reserved driver service name: `NatsxAoaBootstrap`;
- version IOCTL;
- bounded `START_AOA` IOCTL that performs only the canonical Android Open
  Accessory endpoint-zero sequence;
- all unknown device-control requests are forwarded to the lower stack;
- the receiver has a ConfigMgr/CreateFile/DeviceIoControl client for this
  private interface;
- the no-ADB OPPO A58 target is now confirmed as
  `USB\\VID_22D9&PID_2764&REV_0404`, class `WPD`, service
  `WUDFWpdMtp`, with the USB root hub as its parent;
- a **source-only** extension-INF draft now matches that exact ADB-off device
  revision and declares `NatsxAoaBootstrap` as a device-specific lower filter;
- the earlier ADB-on `PID_2765` composite layout is recorded only as a
  diagnostic comparison and is not a NATSX dependency;
- the INF remains intentionally outside the install/package build until the
  existing filter-stack metadata is captured and the lower-filter position is
  validated against the real WPD stack;
- no driver is loaded by normal application runtime unless the prototype
  package is explicitly installed for physical validation.

The driver intentionally owns the privileged endpoint-zero sequence instead of
exposing an arbitrary user-mode vendor-control API. User mode can only ask the
filter to report its protocol version or perform the narrowly scoped NATSX AOA
bootstrap operation.

The source prototype is not sufficient to mark M9 complete. It still requires:

1. capture the existing upper/lower/compound filter metadata for the ADB-off
   OPPO A58 WPD device;
2. validate that the device-specific lower-filter placement can issue the
   bounded AOA control sequence while `WUDFWpdMtp` remains healthy;
3. proof that the OEM MTP/PTP path still works before and after bootstrap;
4. uninstall/reboot recovery validation;
5. receiver integration only after those checks pass.

Do not install this prototype on a production machine yet.
