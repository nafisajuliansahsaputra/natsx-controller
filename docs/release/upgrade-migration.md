# Upgrade and migration behavior

## Compatibility rules

Application upgrades preserve user trust and settings whenever the stored format remains compatible.

Windows persistent trust lives under %LOCALAPPDATA%\NATSX\Controller. The current trust document is version 1. Installer upgrades must not delete this directory.

Android uses versioned SharedPreferences and Keystore-backed trust storage. Package upgrades must keep the same application ID, com.natsx.controller, and the same signing identity so Android preserves protected app data.

## Upgrade sequence

1. Stop or replace running binaries without deleting trust stores.
2. Install the new application version.
3. Reuse compatible trusted-peer records.
4. Re-establish fresh transport sessions; never persist live session keys as an upgrade shortcut.
5. If a future storage schema is incompatible, migrate to a new versioned record atomically before deleting the old record.
6. If migration fails, retain the old data and surface a recoverable error instead of silently discarding pairing trust.

## Uninstall behavior

Normal uninstall removes program files and startup integration but preserves user trust data for reinstall continuity.

A full privacy/reset purge is explicit and separate. On Windows run windows/installer/purge-user-data.ps1 -PurgeTrust. Deleting trust means both peers must pair again.

## Rollback

Rollback is allowed only when the older application understands the currently stored trust/profile schema and the selected protocol major version.

Do not roll back across an incompatible protocol-major or storage-schema boundary without a documented migration path.
