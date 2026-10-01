# NATSX AOA bootstrap driver prototype

This directory is a **non-installing prototype** for the unresolved pre-AOA
Windows bootstrap boundary.

Current scope:

- KMDF pass-through filter skeleton;
- private NATSX device interface;
- version IOCTL;
- all unknown device-control requests are forwarded to the lower stack;
- no AOA vendor request is sent yet;
- no INF/install script is included yet;
- no driver is loaded by normal application CI or runtime.

The prototype exists to establish a reproducible WDK build before endpoint-zero
USB control logic is added.

Do not install this prototype on a production machine.
