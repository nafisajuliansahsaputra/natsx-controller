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
- no INF/install script is included yet;
- no driver is loaded by normal application CI or runtime.

The driver intentionally owns the privileged endpoint-zero sequence instead of
exposing an arbitrary user-mode vendor-control API. User mode can only ask the
filter to report its protocol version or perform the narrowly scoped NATSX AOA
bootstrap operation.

The source prototype is not sufficient to mark M9 complete. It still requires:

1. a device-specific declarative `AddFilter` / extension-INF attach strategy
   that matches only explicitly supported Android hardware and never registers
   a class-wide MTP/WPD filter;
2. physical validation on supported Windows 11 hardware;
3. proof that the OEM MTP/PTP path still works before and after bootstrap;
4. uninstall/reboot recovery validation;
5. receiver integration only after those checks pass.

Do not install this prototype on a production machine yet.
