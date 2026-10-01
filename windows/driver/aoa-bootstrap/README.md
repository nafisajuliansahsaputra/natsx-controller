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
- a **source-only** OPPO A58 extension-INF prototype is now included for the
  observed normal-mode composite parent `USB\\VID_22D9&PID_2765&REV_0404`;
  it is intentionally not wired into the install/package build yet;
- that INF attaches `NatsxAoaBootstrap` as a declarative lower filter to the
  composite parent only, not to the MTP (`MI_00`) or ADB (`MI_01`) child;
- ADB may be present on the test phone but is not used or required by NATSX;
- no driver is loaded by normal application runtime unless the prototype
  package is explicitly installed for physical validation.

The driver intentionally owns the privileged endpoint-zero sequence instead of
exposing an arbitrary user-mode vendor-control API. User mode can only ask the
filter to report its protocol version or perform the narrowly scoped NATSX AOA
bootstrap operation.

The source prototype is not sufficient to mark M9 complete. It still requires:

1. physical validation of the current OPPO A58-scoped declarative
   `AddFilter` / extension-INF package on the observed hardware;
2. confirmation that lower-filter attachment to the composite parent can issue
   the bounded AOA control sequence while `usbccgp` and MTP remain healthy;
3. proof that the OEM MTP/PTP path still works before and after bootstrap;
4. uninstall/reboot recovery validation;
5. receiver integration only after those checks pass.

Do not install this prototype on a production machine yet.
