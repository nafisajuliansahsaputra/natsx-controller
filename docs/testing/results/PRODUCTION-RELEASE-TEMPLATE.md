# NATSX Controller production release evidence

> Duplicate this file for the release candidate. Do not mark a gate passed
> without the requested physical evidence.

## Build identity

- Date:
- Branch:
- Commit:
- VERSION:
- VERSION_CODE:
- Windows installer SHA-256:
- Android APK SHA-256:
- Production driver bundle SHA-256:

## Windows validation machine

- Manufacturer/model:
- Windows edition/version/build:
- Secure Boot: **must be enabled**
- TESTSIGNING: **must be disabled**
- NATSX test certificates present: **must be no**
- Previous NATSX drivers preinstalled before clean install: **must be no**

Attach/output:

- `windows-environment.json`
- `pnputil-enum-drivers.txt`
- `pnputil-enum-devices-connected.txt`

## Clean installer

- [ ] Installer Authenticode signature verifies.
- [ ] Installation completes from a clean machine.
- [ ] No manual driver installation is needed.
- [ ] HIDMaestro bootstrap succeeds during setup.
- [ ] Receiver starts as the normal user after elevated setup.
- [ ] Virtual Xbox 360-compatible controller becomes available.
- [ ] Android pairs through the intended SAS-confirmed flow.
- [ ] Pairing persists after Receiver restart.

Notes/evidence:

## USB Direct retail-driver acceptance

- [ ] OPPO A58 starts from the exact no-ADB normal mode.
- [ ] ADB is not required.
- [ ] USB tethering is not required.
- [ ] Microsoft retail-signed bootstrap package installs.
- [ ] START_AOA re-enumerates to expected Google AOA identity.
- [ ] Microsoft retail-signed scoped WinUSB package binds.
- [ ] USB Direct authenticates into the existing trusted session.
- [ ] USB becomes Smart Auto authority after stabilization.
- [ ] Unplug falls back without manual reconnect.
- [ ] Replug can retake authority.
- [ ] Windows Ethernet/LAN remains usable.
- [ ] Normal MTP behavior recovers after transition/reboot where applicable.

Notes/evidence:

## 30-minute soak

- Start:
- End:
- Active transport sequence:
- Profile CSV:
- Profile summary JSON:

- [ ] No stuck input.
- [ ] No Android crash.
- [ ] No Receiver crash.
- [ ] No virtual-controller loss/recreation.
- [ ] Automatic recovery works after forced transport loss.
- [ ] No continuous memory/thread/handle growth.

Notes:

## 2-hour soak

- Start:
- End:
- Active transport sequence:
- Profile CSV:
- Profile summary JSON:

- [ ] No stuck input.
- [ ] No Android crash.
- [ ] No Receiver crash.
- [ ] No virtual-controller loss/recreation.
- [ ] Automatic recovery works after forced transport loss.
- [ ] No continuous resource growth.
- [ ] No unacceptable Android thermal behavior.

Notes:

## Windows sleep/resume

### Without USB attached

- [ ] Stale input neutralizes before/during resume.
- [ ] Receiver remains/restarts healthy.
- [ ] Pairing/trust is preserved.
- [ ] Wi-Fi reconnects automatically.
- [ ] Bluetooth can rejoin when available.
- [ ] Virtual controller becomes usable without reinstall.

### With USB attached

- [ ] Stale input neutralizes.
- [ ] Driver/device stack recovers.
- [ ] USB or best healthy backup becomes authoritative automatically.
- [ ] No manual pairing/reinstall.

Notes:

## CPU/resource review

- 30-minute average CPU:
- 30-minute peak CPU:
- 2-hour average CPU:
- 2-hour peak CPU:
- Peak working set:
- Peak private memory:
- Thread count start/end:
- Handle count start/end:

- [ ] No sustained unexplained CPU spike.
- [ ] No continuous memory growth.
- [ ] No thread leak.
- [ ] No handle leak.

## Android battery/thermal review

Phone:
Android version:
Brightness/network conditions:
Screen-on idle baseline duration:
Controller session duration:

- Baseline battery delta:
- Controller-session battery delta:
- Thermal warning observed:
- Unusual background consumption observed:

- [ ] Battery impact reviewed and accepted for v1.
- [ ] No thermal warning during representative gameplay.
- [ ] Foreground service stays healthy.

## Upgrade / uninstall

- [ ] Upgrade over prior installed version succeeds.
- [ ] Pairing/trust survives normal upgrade.
- [ ] Normal uninstall removes application components.
- [ ] Normal uninstall attempts NATSX Driver Store cleanup.
- [ ] Normal uninstall preserves pairing/trust by design.
- [ ] Reinstall can reuse preserved trust.
- [ ] Explicit purge removes pairing/trust only when requested.

## Signing / legal gates

- [ ] Product LICENSE decision is final and LICENSE exists.
- [ ] Android APK signature is final production identity.
- [ ] Windows Receiver/installer Authenticode signature is final production identity.
- [ ] Microsoft retail driver signatures pass repository production validator.
- [ ] Inno Setup licensing posture is confirmed for intended distribution.
- [ ] Release tag exactly matches VERSION.
- [ ] CHANGELOG has the final VERSION section.

## Final release decision

- [ ] All v1 production gates passed.
- Decision:
- Approved by:
- Date:
