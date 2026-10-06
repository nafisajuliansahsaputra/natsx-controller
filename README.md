# NATSX Controller

Android-to-Windows gamepad system that turns a phone into an Xbox 360-compatible controller through a shared cross-platform protocol, independent multi-touch input, transport health monitoring, and automatic connection recovery.

**Status:** Complete · **Role:** Software Developer / Systems Engineer / Product Designer

[Portfolio Case Study](https://natsx.my.id/work/natsx-controller)

## Recruiter snapshot

NATSX Controller is a systems-oriented project spanning native Android, Windows desktop development, realtime networking, cross-language protocol design, connection state machines, input safety, and virtual-controller integration.

| Area | Implementation |
| --- | --- |
| Android | Kotlin, native touch/gamepad state engine, multi-touch pointer ownership |
| Windows | C#, .NET 10, WPF receiver, controller/session safety core |
| Protocol | Shared Kotlin/C# binary framing, sequencing, integrity, authenticated session semantics |
| Networking | UDP Wi-Fi path, Bluetooth RFCOMM, USB Direct architecture |
| Connection management | Health scoring, hysteresis, cooldowns, circuit breaker, automatic handover |
| Safety | Full-state snapshots, stale/duplicate rejection, neutral watchdog behavior |
| Quality | Android + Windows CI, protocol fixtures, deterministic connection-policy tests |

### Engineering highlights

- Designed one transport-independent **GamepadState** protocol implemented in both Kotlin and C#.
- Built independent Android multi-touch handling so simultaneous stick, trigger, shoulder, D-pad, and face-button input does not collapse into a single pointer path.
- Implemented **Smart Connection Manager** policy around USB Direct, Wi-Fi, and Bluetooth with latency, jitter, packet loss, silence, hysteresis, cooldown, and failure history.
- Separated **virtual-controller lifetime from transport lifetime** so connection recovery does not require recreating the controller model.
- Added full-state sequencing and watchdog behavior to reduce stuck-input risk after packet loss, stale data, or disconnects.
- Built trusted local pairing/reconnect foundations without requiring a cloud account or internet backend.
- Added cross-language test vectors and automated Windows/Android quality gates.

## Problem

A phone-based controller is easy to prototype if every button press is treated as an isolated network event. It becomes much harder when the goal is controller-like behavior under packet loss, reconnects, simultaneous touches, transport changes, and Windows virtual-device constraints.

The project therefore treats input state, connection state, and virtual-controller state as separate engineering concerns.

## Architecture

```text
Android touch surface
        |
        v
GamepadState store
        |
        v
Cross-platform protocol
        |
        +---- Wi-Fi / UDP
        +---- Bluetooth / RFCOMM
        +---- USB Direct path
        |
        v
Windows Receiver
        |
        v
Controller session + safety engine
        |
        v
Virtual Xbox 360-compatible backend
```

Only one transport may be authoritative for controller updates at a time. Candidate transports are evaluated before takeover, and stale state is rejected.

## Smart Connection Manager

The preferred order is:

```text
USB Direct > Wi-Fi > Bluetooth
```

but priority alone does not force a switch.

The policy also considers:

- RTT;
- jitter;
- packet loss;
- silence / timeout;
- stability duration;
- hysteresis;
- cooldown;
- recent failure history;
- circuit breaker state.

This avoids repeatedly bouncing between transports just because a nominally preferred link appears for a moment.

## Input safety

The realtime protocol carries complete controller snapshots instead of depending only on edge events.

That allows the Windows side to reason about the latest authoritative state even when packets are lost or transports change.

Safety rules include:

- global session sequence validation;
- stale and duplicate rejection;
- one authoritative transport;
- full-state synchronization during handover;
- neutralization when fresh valid state disappears beyond the safety timeout;
- controller-session state kept independent from UI state.

## Android gameplay surface

The Android side includes:

- independent pointer tracking;
- multi-touch control ownership;
- analog-stick processing;
- configurable deadzone and clamp behavior;
- A/B/X/Y;
- LB/RB;
- LT/RT;
- L3/R3;
- Back/View;
- Start/Menu;
- D-pad;
- full-state snapshot publishing;
- landscape gameplay layout designed first around eFootball.

The gameplay path prioritizes reliable input over visual animation.

## Protocol

The wire contract lives under `protocol/`.

The current v1 realtime frame includes:

- 40-byte fixed header;
- complete `GamepadState` snapshots;
- global session sequence numbers;
- monotonic timestamps;
- CRC32C;
- authenticated-session HMAC-SHA-256 tag truncated to 16 bytes.

The canonical test vector is reproduced by both Kotlin and C# implementations.

## Project scope

The **Complete** status refers to the portfolio engineering scope represented by this repository: cross-platform controller architecture, Android input, Windows session/safety design, shared protocol, Smart Connection Manager, Wi-Fi/Bluetooth transport work, USB Direct engineering, and automated verification.

The repository also keeps an extended productization roadmap for work such as broader release packaging, additional UX/settings, advanced haptics/rumble, and longer soak testing. Those roadmap items are documented separately and are not used to inflate the completed engineering claims above.

## Repository layout

```text
android/       Android controller application
windows/       Windows receiver and controller core
protocol/      Cross-platform wire protocol specification
docs/          Architecture decisions and technical notes
PRD.md         Product requirements
ARCHITECTURE.md
TODO.md
```

Internal contributor workflow files remain in the repository for implementation consistency but are not part of the recruiter-facing overview.

## Windows development

Requirements:

- Windows 10/11
- .NET 10 SDK

```powershell
dotnet restore windows/Natsx.Controller.slnx
dotnet build windows/Natsx.Controller.slnx --configuration Release
dotnet test windows/Natsx.Controller.slnx --configuration Release
```

The WPF receiver lives at:

```text
windows/src/Natsx.Controller.Receiver/
```

## Android development

Current build baseline:

- Android Gradle Plugin 9.4.1
- Gradle 9.6
- JDK 17
- compileSdk 36
- minSdk 26
- targetSdk 36
- native Kotlin

Build:

```bash
gradle -p android :app:testDebugUnitTest
gradle -p android :app:assembleDebug
```

## Documentation

For a technical review, start with:

1. [Product Requirements](PRD.md)
2. [Architecture](ARCHITECTURE.md)
3. [Roadmap / TODO](TODO.md)
4. [Protocol](protocol/)
5. [Architecture decisions](docs/decisions/)

## License

No project license has been selected yet.
