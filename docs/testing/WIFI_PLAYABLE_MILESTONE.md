# Wi-Fi playable milestone validation

This checklist is the first real hardware gate for NATSX Controller.

Passing CI is necessary but does **not** by itself prove controller feel, driver behavior, Windows firewall behavior, or eFootball compatibility.

## Required hardware

- Windows 10/11 PC
- Android 8.0+ phone
- both devices on the same LAN/Wi-Fi network
- NATSX Windows receiver build
- NATSX Android debug/release build
- HIDMaestro runtime/driver installed and functional on Windows

The laptop may use Ethernet for Internet access. Wi-Fi controller traffic only needs IP reachability between phone and PC.

## First pairing

1. Start NATSX Controller Receiver on Windows.
2. Verify the receiver reports the virtual controller/backend state.
3. Select **Pair new phone**.
4. Launch NATSX Controller on an unpaired Android phone.
5. Select **Find Windows receiver**.
6. Select the intended PC from discovery results.
7. Wait until both devices display a six-digit code.
8. Verify the six digits are identical.
9. Confirm **Codes match** on both devices.
10. Verify the phone switches to the controller surface.
11. Verify Windows reports an authenticated active controller session.

Do not confirm pairing when the two codes differ.

## Windows controller visibility

With the authenticated session active:

1. Open Windows Game Controllers / an XInput test utility.
2. Confirm exactly the intended NATSX Xbox 360-compatible virtual controller is visible.
3. Hold A on Android and verify A is held, not repeatedly tapped.
4. Hold combinations such as:
   - LS + A
   - LS + RT + A
   - LB + LS + X
   - two face buttons where the game accepts them
5. Release every touch and verify every Windows input returns neutral.
6. Move both sticks through full circles and verify no unexpected snapping or stuck axes.
7. Verify LT/RT reach their intended full range.

## Safety test

While holding a control:

1. disable phone Wi-Fi or otherwise interrupt the link;
2. verify Windows releases/neutralizes stale input within the configured safety deadline;
3. restore Wi-Fi;
4. verify the phone reconnects without manually pairing again;
5. verify a new authenticated session becomes active;
6. verify gameplay input resumes.

The virtual controller should not be recreated merely because Wi-Fi reconnects.

## Discovery fallback test

1. pair successfully once;
2. allow the phone to remember the receiver endpoint;
3. change the PC's LAN address or restart networking;
4. reopen the controller;
5. verify direct reconnect fails cleanly;
6. verify discovery finds the same trusted **Device ID**;
7. verify trusted reconnect succeeds automatically.

Receiver name/IP alone must never establish trust.

## eFootball feel test

Test in actual eFootball, not only a generic gamepad tester.

Focus on:

- left-stick first touch and direction changes;
- sprint + direction + face-button combinations;
- defensive shoulder/trigger combinations;
- right-stick accessibility;
- accidental presses around A/B/X/Y;
- D-pad reach;
- finger overlap during 15+ minutes of play;
- perceived input delay;
- reconnect behavior during a live match.

Record any layout discomfort separately from transport latency so the two problems are not mixed.

## Diagnostics to capture on failure

Record:

- active transport;
- RTT;
- jitter;
- packet loss;
- health grade;
- whether pairing, discovery, or trusted reconnect failed;
- whether Windows still saw the virtual controller;
- whether input neutralized safely;
- Android model and Android version;
- Windows version;
- network path (PC Ethernet/Wi-Fi and phone Wi-Fi).

Never record pairing root keys, session keys, HMAC proofs, or other trust secrets.
