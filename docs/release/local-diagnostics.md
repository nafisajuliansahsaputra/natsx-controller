# Local diagnostics and crash collection

NATSX Controller does not require cloud telemetry, analytics, or a remote crash-reporting service.

## Privacy boundary

Crash collection is local-first:

- no crash report is uploaded automatically;
- no gamepad input frame or button/stick history is written to crash logs;
- no trust secret, session key, pairing proof, or Android Keystore/Windows DPAPI payload is intentionally logged;
- logs contain only crash metadata, exception type/message, thread information where applicable, and a bounded stack trace;
- the user decides whether to share a log for support.

Operational exception messages can still contain ordinary local metadata such as a component name or endpoint description. Treat diagnostic files as private user data.

## Windows

Receiver crash diagnostics are stored under:

`%LOCALAPPDATA%\NATSX\Controller\diagnostics\`

The primary file is `receiver-crash.log`. It is capped at approximately 256 KiB and rotated through three archives.

The Receiver records:

- WPF dispatcher unhandled exceptions;
- process/AppDomain unhandled exceptions;
- unobserved task exceptions;
- installer virtual-controller bootstrap failures.

The logger is best-effort. Any logging failure is swallowed so diagnostics can never become a new gameplay/runtime failure.

The elevated installer also keeps a one-shot setup failure report under:

`%ProgramData%\NATSX\Controller\setup-driver-error.log`

That file is written only if the virtual-controller bootstrap fails during Setup.

## Android

Android crash diagnostics stay in the application's private internal files directory under `diagnostics/android-crash.log`.

The app installs a wrapping default uncaught-exception handler. It writes the bounded local record first, then delegates to Android's previous default handler so normal platform crash termination/reporting behavior is preserved.

The Android log uses the same approximate 256 KiB cap and three rotated archives.

## Support workflow

1. Reproduce the failure once.
2. Close/restart NATSX Controller if possible.
3. Copy only the relevant local diagnostic file.
4. Review it before sharing.
5. Never share trust-store files, pairing secrets, keystores, certificates with private keys, or BitLocker recovery material.

Normal runtime diagnostics remain aggregated in the UI and do not log every controller packet.
