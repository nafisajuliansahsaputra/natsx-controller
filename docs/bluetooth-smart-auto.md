# Bluetooth Smart Connection integration

## Verified production path

The Windows Smart Connection runtime is transport-agnostic through
`IControllerTransport`. `BluetoothControllerTransport` now has integration
coverage inside that real runtime rather than only isolated transport tests.

The scenario verifies:

1. Wi-Fi is initially authoritative.
2. A production Bluetooth realtime receiver accepts a fresh authenticated
   full-state frame and remains `READY` as warm standby.
3. Wi-Fi enters `LOST/FAILED`.
4. Smart Connection commits Bluetooth using the already cached newer global
   state sequence.
5. No neutral frame is inserted during the healthy failover.
6. Recovered Wi-Fi remains non-authoritative until recovery hysteresis and
   failure cooldown are satisfied.
7. Smart Connection hands authority back to Wi-Fi.
8. Bluetooth is explicitly demoted back to `READY`.
9. Post-handback cooldown prevents immediate ping-pong back to Bluetooth on a
   merely degraded Wi-Fi sample.

This is deterministic runtime integration. Physical radio interruption and
long-duration flapping remain part of the M14 soak/reliability milestone.
