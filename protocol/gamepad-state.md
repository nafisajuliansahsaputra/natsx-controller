# GamepadState v1

`GamepadState` is the complete current controller state.

It is a snapshot, not a collection of button-edge events.

## Digital controls

The v1 logical model includes:

- A
- B
- X
- Y
- LB
- RB
- L3
- R3
- Back / View
- Start / Menu
- Guide / Home capability where supported
- D-pad Up
- D-pad Down
- D-pad Left
- D-pad Right

The binary wire format should encode digital buttons as a compact bitset.

The exact bit assignments will be frozen before cross-language encoder implementation.

## Analog controls

| Field | Logical range | Neutral |
|---|---:|---:|
| LX | -32768..32767 | 0 |
| LY | -32768..32767 | 0 |
| RX | -32768..32767 | 0 |
| RY | -32768..32767 | 0 |
| LT | 0..255 | 0 |
| RT | 0..255 | 0 |

## Axis direction contract

Both sticks use Xbox logical directions: negative X is left, positive X is
right, positive Y is up, and negative Y is down. Android screen Y increases
downward, so touch processing reverses it once before serialization. Wi-Fi,
Bluetooth, USB, and the GamepadHost pipe preserve these signed values.

The HIDMaestro backend converts X to 0=left / 1=right and Y to 0=up / 1=down,
with 0.5 centered. Both Y axes must be reversed at this output boundary because
HIDMaestro's XInput companion converts HID Y back to Xbox Y. Do not invert the
Android state, skin, calibration, or individual transports to compensate.

## Neutral state

A neutral state is:

```text
all buttons released
D-pad neutral
LX = 0
LY = 0
RX = 0
RY = 0
LT = 0
RT = 0
```

The Windows safety engine may submit this state when fresh authoritative input has been unavailable beyond the configured safety timeout.

## Full-state rule

Every accepted realtime state supersedes the previous accepted realtime state.

Example:

```text
#100: A=pressed, RT=255
#101: A=released, RT=255
```

If packet #100 is lost but #101 arrives, the receiver still has the correct current state.

This is the reason the protocol does not depend solely on press/release edges.

## Sequence semantics

The state is carried with a controller-session-global sequence number.

The sequence does not restart merely because the transport changes.

```text
Wi-Fi      #900
Wi-Fi      #901
handover
Bluetooth  #902
Bluetooth  #903
```

The exact integer width and wrap comparison algorithm will be frozen in `messages.md` before implementation.

## Android analog pipeline

The recommended logical pipeline is:

```text
raw pointer
 -> vector from stick center
 -> clamp to physical radius
 -> normalize
 -> inner deadzone
 -> response curve
 -> light anti-jitter
 -> logical Xbox-style range
```

Filtering must not introduce heavy directional lag.

## eFootball profile

For the eFootball profile:

- left stick prioritizes fast response;
- smoothing remains very light;
- default curve is linear;
- deadzone starts low and remains configurable;
- LT/RT may expose digital-style full presses while the core state remains analog-capable.
