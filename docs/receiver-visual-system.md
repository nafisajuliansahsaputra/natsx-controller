# Windows Receiver visual system

This is the visual reference for `windows/src/Natsx.Controller.Receiver/MainWindow.xaml`, aligned to the Android controller's light visual language. It does **not** redefine controller input, pairing, transport selection, or session lifecycles.

## Shared colors

| Role | Color | Application |
| --- | --- | --- |
| Canvas | `#F4F3F7` | Window background |
| Elevated surface | `#FAFAFC` | Primary dashboard panels |
| Subtle lavender | `#E7DDF7` | Chips, surfaces, quiet emphasis |
| Lavender | `#B7A5E2` | Controller-inspired accents |
| Purple | `#9D89CF` | Logo, USB active indicator |
| Green | `#92DFA0` | Mint controls, Wi-Fi aesthetic |
| Wi-Fi active | `#71C982` | Live Wi-Fi and connection signal |
| Inactive | `#D9D5DF` | Non-authoritative connection bars |
| Foreground | `#282333` | Main text |

These colors correspond to the current Android controller implementation; avoid introducing a competing desktop-only theme without a design decision.

## Information hierarchy

1. Brand/header, real connection state.
2. Pairing confirmation or startup errors (when present; high priority).
3. Live receiver status, Smart Auto and authoritative transport.
4. RTT, jitter, packet loss and input rate.
5. Connection paths and virtual-controller health.
6. Trusted controller actions and Windows/tray preferences.

The pairing code, action buttons, startup errors, Forget action and settings remain functional. Existing `x:Name` references and event handlers are part of the UI/runtime contract.

## Runtime correctness

- `MainWindow.xaml.cs` may style the indicators using `ReceiverDiagnosticsSnapshot.ActiveTransport`.
- Only the actual authoritative transport receives the active color. Cable presence, discovery and standby alone must not light the active bar.
- Keep `ReceiverRuntime` and Smart Connection Manager as the sole authorities for connection, pairing and diagnostics.
- Do not invent metrics; show existing runtime data or neutral placeholders.
- No looping animations, frame-by-frame XAML redraw or UI-thread work on the realtime input path.

## Validation

Run on Windows:

```powershell
dotnet restore windows/Natsx.Controller.slnx
dotnet build windows/Natsx.Controller.slnx --configuration Release
dotnet test windows/Natsx.Controller.slnx --configuration Release
```

Verify at minimum: startup, pairing confirm/reject, no-active-link state, USB/Wi-Fi/Bluetooth active markers, startup error/retry, Forget, system tray, and start-with-Windows settings.
