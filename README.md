# NATSX Controller

Turn an Android phone into a low-latency Xbox 360-compatible controller for Windows.

The project is designed around eFootball first, while keeping the controller core reusable for other Windows games.

## Current status

Early production foundation.

Already implemented in the current development branch:

- product/architecture contract;
- protocol v1 framing specification;
- matching C# and Kotlin protocol codecs;
- authenticated frame test vector;
- Windows controller/session safety core;
- Smart Connection Manager selection core;
- Android native controller surface with independent multi-touch;
- eFootball-inspired default layout;
- Android and Windows CI.

Still under active development:

- virtual Xbox 360 backend integration;
- Wi-Fi discovery/pairing/streaming;
- Bluetooth RFCOMM;
- USB Direct;
- full Smart Auto transport orchestration;
- rumble path;
- installer and release packaging.

See `TODO.md` for the full roadmap.

## Product principles

1. Correct input is more important than animation.
2. No stuck buttons after connection loss.
3. One stable virtual controller across transport changes.
4. USB, Wi-Fi, and Bluetooth share one logical protocol.
5. Core gameplay does not depend on the cloud.
6. The laptop's Ethernet/LAN route remains independent from USB controller traffic.

## Repository layout

```text
android/       Android controller application
windows/       Windows receiver and controller core
protocol/      Cross-platform wire protocol specification
docs/          Architecture decision records and project docs
PRD.md         Product requirements
AGENTS.md      Rules for coding agents/contributors
ARCHITECTURE.md
TODO.md
SKILL.md
WORKFLOW.md
```

## Windows development

Requirements:

- Windows 10/11 for receiver/runtime work;
- .NET 10 SDK;
- internet access on the first clean build so the pinned HIDMaestro v1.9.2 SDK can be bootstrapped.

The HIDMaestro SDK is fetched automatically on the first Windows build when
`windows/lib/HIDMaestro/HIDMaestro.Core.dll` is absent. The release archive is
pinned and SHA-256 verified by `windows/eng/fetch-hidmaestro.ps1`; no global
HIDMaestro SDK installation is required.

HIDMaestro's virtual-device driver is also installed idempotently before the
first virtual Xbox controller is created. The initial driver installation
requires Windows elevation; once the matching driver is present, subsequent
receiver starts do not require reinstalling it.

Build and test:

```powershell
dotnet restore windows/Natsx.Controller.slnx
dotnet build windows/Natsx.Controller.slnx --configuration Release
dotnet test windows/Natsx.Controller.slnx --configuration Release
```

The WPF receiver project is:

```text
windows/src/Natsx.Controller.Receiver/
```

The WPF receiver now starts one HIDMaestro-backed virtual Xbox controller
independently from transport lifetime. Trusted Wi-Fi, Bluetooth, and USB
transports attach to the shared Smart Connection runtime dynamically, so
transport reconnect/handover does not recreate the virtual controller.

Run the receiver during development with:

```powershell
dotnet run --project windows/src/Natsx.Controller.Receiver/Natsx.Controller.Receiver.csproj --configuration Release
```

### First pairing

When neither side has a trust record yet, connect the Android phone over the
NATSX USB accessory path and open the Android app. Android sends a first-pair
offer and both devices display the same six-digit SAS.

Pairing completes only after the user confirms that code on **both** screens.
The derived long-term trust secret is then stored with Android Keystore-backed
protection on Android and Windows DPAPI on the receiver. The still-open USB
connection immediately continues into the normal trusted reconnect handshake;
no cable replug, ADB, manual secret entry, or IP-based trust is required.

If the codes differ, reject pairing. No trust record is written until the
remote role-bound confirmation proof also verifies.

## Android development

Current build baseline:

- Android Gradle Plugin 9.4.1;
- Gradle 9.6;
- JDK 17;
- compileSdk 36;
- minSdk 26;
- targetSdk 36;
- native Kotlin through AGP built-in Kotlin support.

Build from Windows with the repository-pinned Gradle Wrapper:

```powershell
.\android\gradlew.bat -p android :app:testDebugUnitTest
.\android\gradlew.bat -p android :app:assembleDebug
```

The wrapper downloads the pinned Gradle 9.6.0 distribution on first use and
verifies its SHA-256 checksum. A globally installed `gradle` command is not
required.

The repository intentionally does not require ADB as part of the final controller transport design.

## Protocol

The wire contract lives under `protocol/`.

The current v1 realtime frame uses:

- 40-byte fixed header;
- complete `GamepadState` snapshots;
- global session sequence numbers;
- CRC32C;
- optional/authenticated-session HMAC-SHA-256 tag truncated to 16 bytes.

The canonical v1 test vector is defined in `protocol/messages.md` and is reproduced by both the Kotlin and C# implementations.

## Connection policy

Smart Auto is designed around:

```text
USB Direct > Wi-Fi > Bluetooth
```

when links are similarly healthy.

Actual switching also considers:

- RTT;
- jitter;
- packet loss;
- silence/timeout;
- stability duration;
- hysteresis;
- cooldown;
- failure history;
- circuit breaker.

A preferred transport does not take over merely because it exists.

## Documentation

Before implementing a feature, read:

1. `PRD.md`
2. `ARCHITECTURE.md`
3. `TODO.md`
4. `WORKFLOW.md`
5. `SKILL.md`
6. `AGENTS.md`

Architecture-changing decisions should be recorded under `docs/decisions/`.

## License

No project license has been selected yet.
