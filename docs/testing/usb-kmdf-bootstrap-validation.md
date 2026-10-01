# USB KMDF bootstrap physical validation

This checklist is the gate between the source-level KMDF prototype and an
accepted Windows-side USB Direct implementation.

It intentionally does not provide a broad install recipe. The filter package
must first be scoped to an explicitly identified test phone/device so unrelated
MTP/WPD or USB devices cannot be captured.

## Preconditions

- Windows 11 test machine with current updates.
- One explicitly identified Android test phone.
- Known-good USB data cable.
- NATSX post-AOA WinUSB package available.
- A recovery path for removing the prototype package if validation fails.
- Ethernet/LAN state captured before the driver is attached.

## Read-only target inspection

Before authoring any test INF, inspect the currently attached phone without
changing its driver stack:

```powershell
pwsh -File windows/eng/inspect-usb-bootstrap-target.ps1
```

If the phone is not identified automatically:

```powershell
pwsh -File windows/eng/inspect-usb-bootstrap-target.ps1 -IncludeAllUsb
```

After selecting the exact phone instance, capture a machine-readable report:

```powershell
pwsh -File windows/eng/inspect-usb-bootstrap-target.ps1 -InstanceId "<device-instance-id>" -AsJson
```

The report is read-only and records the hardware IDs, compatible IDs, current
service, parent, and current driver package needed to design a device-specific
test attach.

## Current physical target captured on 2026-10-01

The first physical target is an OPPO A58.

With USB debugging enabled, Windows exposed a composite device:

- parent: `USB\\VID_22D9&PID_2765&REV_0404`, service `usbccgp`;
- MTP/WPD child: `MI_00`, service `WUDFWpdMtp`;
- incidental ADB child: `MI_01`, service `WINUSB`.

With USB debugging disabled, the shipping-relevant no-ADB state is different:

- device: `USB\\VID_22D9&PID_2764&REV_0404`;
- class: `WPD`;
- service: `WUDFWpdMtp`;
- base INF: `wpdmtp.inf`;
- parent: USB root hub;
- no ADB interface is present.

NATSX must target the no-ADB `PID_2764` state. The `PID_2765` composite
layout is only a diagnostic comparison and must never become a dependency.

The ADB-off capture reports no upper filters and reports `WinUsb` as both
`LowerFilters` and `CompoundLowerFilters`. That is consistent with the normal
Windows USB-MTP stack, where WinUSB sits below the WPD MTP function driver; it
is not an ADB dependency.

This creates a real ordering gate for NATSX: adding another generic
`FilterPosition=Lower` filter can leave relative order among lower filters
unspecified when the base stack does not expose named filter levels. Do not
install the source INF until the effective stack is captured and the prototype
has a deliberate ordering/compatibility plan.

The captured effective stack is:

```text
WpdUpFltr
WUDFRd
WINUSB
ACPI
USBHUB3
```

That confirms that the inbox MTP stack already contains WinUSB below the WPD
function stack. Before installing any NATSX filter, run the safe user-mode
probe below to answer a narrower question: can the existing USB device
interface be opened with WinUSB and service AOA GET_PROTOCOL directly?

```powershell
dotnet run --project windows/tools/Natsx.Controller.UsbProbe -- `
  --instance-id "USB\VID_22D9&PID_2764\W4U4SCSSGMIBLJ8H"
```

The probe sends only AOA request 51 (GET_PROTOCOL). It does **not** send AOA
identity strings and does **not** send START_ACCESSORY, so it will not
intentionally switch the phone into accessory mode.

Physical result on the OPPO A58 no-ADB target:

```text
Success: false
Stage: winusb-initialize
Win32Error: 1 (ERROR_INVALID_FUNCTION / Incorrect function)
```

The existing inbox MTP WinUSB lower filter therefore does not expose a usable
application WinUSB handle through the device interface that Windows publishes
for this WPD device. The driverless bootstrap path is closed for this validated
stack and the kernel bootstrap boundary remains necessary.

Before installation, the KMDF prototype now moves its custom IOCTL channel to a
sideband control device. This follows the Windows filter-driver pattern used
when a filter can sit underneath another driver that may reject unknown IOCTLs.
The PnP filter stack remains pass-through; user-mode control requests no longer
depend on traversing WPD/WUDF first.

## Package/attach gate

The prototype may advance to install testing only when its INF/package:

- uses device-specific matching;
- uses the declarative `DDInstall.Filters` / `AddFilter` model on supported
  Windows versions;
- does not replace the OEM function driver;
- does not register a class-wide MTP/WPD filter;
- installs the NATSX filter service only for the intended device match;
- preserves normal receiver startup without elevation after setup;
- has a deterministic uninstall path.

## Validation sequence

### 1. Baseline before filter attach

Record:

- Device Manager device stack for the phone;
- working MTP/file-transfer behavior;
- default IPv4/IPv6 routes;
- active Ethernet adapter and route metrics;
- phone hardware IDs and compatible IDs.

Pass condition: the phone works normally before NATSX is installed.

### 2. Filter attach without AOA start

Install/attach only the device-specific prototype.

Verify:

- the OEM function driver is still loaded;
- MTP/file transfer still works;
- unrelated USB devices are unchanged;
- receiver can enumerate the private NATSX bootstrap interface as a normal
  non-elevated user;
- no new network adapter/default route appears.

### 3. AOA bootstrap

Trigger exactly one NATSX bootstrap operation.

Verify:

- driver protocol/version handshake succeeds;
- AOA protocol version is non-zero;
- the phone re-enumerates into the supported Google AOA VID/PID;
- the bootstrap interface disappears with the normal-mode stack as expected;
- the scoped NATSX WinUSB AOA interface appears within the coordinator timeout;
- the authenticated USB session can open on the post-AOA bulk endpoints.

### 4. Smart Auto behavior

With Wi-Fi active first:

- connect gameplay over Wi-Fi;
- plug USB;
- bootstrap AOA;
- keep USB in STABILIZING for the configured interval;
- verify USB becomes authoritative only after readiness/stability;
- unplug USB;
- verify immediate takeover by the best READY Wi-Fi/Bluetooth candidate;
- verify the virtual Xbox controller identity remains unchanged.

### 5. LAN isolation

Compare the pre-test and active-USB route state.

Pass condition:

- Ethernet/LAN remains the normal internet/game route;
- no USB tethering/RNDIS/NCM network dependency is created by NATSX;
- default-route ownership/metrics are not changed by the controller transport.

### 6. Failure and recovery

Exercise:

- unsupported/non-AOA Android device;
- charge-only or bad data path;
- phone unplug during bootstrap;
- AOA re-enumeration timeout;
- driver interface access failure;
- receiver restart;
- Windows reboot.

Pass condition: failures are diagnosed distinctly and the normal phone stack is
not left unusable.

### 7. Uninstall

Remove the prototype package and reboot.

Verify:

- NATSX filter no longer appears in the target stack;
- OEM MTP/file transfer works;
- unrelated USB devices remain unaffected;
- network routes match the expected non-NATSX baseline.

## Acceptance

Only after all sections pass on the supported Windows 11 + target phone matrix
may the repository:

- finalize the shipping INF/installer design;
- mark Windows-side USB implementation validated;
- mark direct USB transport complete;
- mark LAN-isolation verification complete;
- change ADR 0007 from prototype-gated to final.
