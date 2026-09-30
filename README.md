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
- .NET 10 SDK.

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

The virtual Xbox backend is not yet wired into the receiver.

## Android development

Current build baseline:

- Android Gradle Plugin 9.4.1;
- Gradle 9.6;
- JDK 17;
- compileSdk 36;
- minSdk 26;
- targetSdk 36;
- native Kotlin through AGP built-in Kotlin support.

Build from a machine with Android SDK + Gradle 9.6 available:

```bash
gradle -p android :app:testDebugUnitTest
gradle -p android :app:assembleDebug
```

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
