# Third-party notices

This file documents third-party software used by NATSX Controller. It does not choose or replace the license for NATSX Controller itself.

## Redistributed runtime dependencies

### HIDMaestro v1.9.2

- Project: HIDMaestro by HIDMaestro Contributors
- License: MIT
- Use: Windows virtual Xbox-compatible controller backend
- Source release: pinned to v1.9.2 by `windows/eng/fetch-hidmaestro.ps1`
- Integrity: the release archive is SHA-256 pinned by the fetch script
- Distribution notice: the exact upstream license from the pinned archive is copied into Windows publish/install output as `licenses/HIDMaestro.LICENSE.txt`

### Microsoft .NET runtime and System.Security.Cryptography.ProtectedData

- License family: MIT / Microsoft .NET distribution terms
- Use: self-contained Windows Receiver runtime and Windows DPAPI integration
- Package version for ProtectedData: 10.0.0

## Build and test dependencies

These are used to build or test the product and are not intentionally bundled as application runtime libraries:

- Android Gradle Plugin 9.4.1
- Gradle 9.6
- JUnit 4.13.2
- Microsoft.NET.Test.Sdk 17.12.0
- xUnit 2.9.2
- xunit.runner.visualstudio 2.8.2
- Inno Setup 6
- Windows Driver Kit tooling
- GitHub Actions used by repository workflows

## Product-license boundary

The repository's own product/source license remains an explicit project-owner decision tracked in M0 of `TODO.md`. Completing this dependency review does not make that legal choice on the owner's behalf.
