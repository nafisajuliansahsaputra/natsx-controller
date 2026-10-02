# Changelog

All notable product changes are tracked here.

Application releases follow Semantic Versioning. Protocol compatibility is versioned separately in protocol/versions.md.

## Unreleased

### Added

- Smart Connection Manager with Wi-Fi, Bluetooth, and USB authority handover.
- Secure first pairing and trusted reconnect.
- Physical OPPO A58 AOA/WinUSB USB-direct uplink path.
- HIDMaestro-backed virtual Xbox controller.
- Android rumble/haptics, diagnostics, profiles, tuning, calibration, and custom layout editing.
- Android manual transport preference with emergency Smart Auto fallback.
- Windows Receiver tray/startup/recovery UX and live diagnostics.
- Signed Android release workflow plumbing and Windows publish artifacts.
- Windows Inno Setup installer with elevated virtual-controller driver bootstrap, normal-user launch, and pairing-preserving uninstall cleanup.
- Bounded local-only crash diagnostics on Windows and Android with no automatic telemetry upload.
- Dependency/license notices, NuGet vulnerability audit policy, Dependabot coverage, and a documented v1 security review.

### Reliability

- Duplicate/out-of-order rejection and packet-loss/failover coverage.
- USB flapping/reconnect soak coverage.
- Repeated Wi-Fi/Bluetooth reconnect regression coverage.
- Android background/lock neutralization and sticky-service restart recovery guards.

## 0.1.1-dev

Development baseline before release-engineering consolidation.
