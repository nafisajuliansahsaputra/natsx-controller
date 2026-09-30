# Wi-Fi diagnostics and network-loss verification

## Runtime diagnostics

`WifiRealtimeReceiver` exposes a snapshot-only diagnostics boundary. Reading
diagnostics does not participate in the realtime input path and does not alter
transport authority.

The snapshot currently exposes:

- transport runtime state;
- receiver running state;
- local bound endpoint;
- authenticated remote endpoint;
- accepted realtime state datagrams;
- accepted authenticated control datagrams;
- rejected datagrams;
- heartbeat send count;
- silence since the last accepted state;
- RTT;
- jitter;
- packet loss.

`WifiControllerTransport.GetDiagnosticsSnapshot()` attaches the adapter's
current runtime state to the same receiver metrics.

The snapshot is intended for later receiver UI / diagnostics aggregation.
Normal operation must not log every realtime packet.

## Network-loss harness

The Windows connection test suite contains a scripted transport harness that can
change health state and inject full-state packets without physical hardware.

The current loss scenario verifies:

1. Wi-Fi is selected as the initial authoritative transport.
2. A warm Bluetooth candidate already has a newer full-state snapshot.
3. Wi-Fi hard loss triggers automatic failover to Bluetooth.
4. Loss of every transport leaves the virtual backend present and the watchdog
   submits a neutral state after the safety timeout.
5. Recovered Wi-Fi publishes a fresh global sequence and automatically regains
   authority.
6. The virtual backend is never restarted during the scenario.

This harness is deterministic policy/runtime coverage. Real-device Wi-Fi
toggle, router interruption, and soak testing remain part of M14.
