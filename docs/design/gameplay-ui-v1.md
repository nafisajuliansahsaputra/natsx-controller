# NATSX Controller — Gameplay UI/UX Specification v1

Status: **Implementation source of truth**
Scope: Android gameplay/controller surface
Priority: **input performance and usability first; visual polish second**

---

## 1. Purpose

This document defines the final v1 direction for the Android controller gameplay UI.

The UI must feel clean, modern, comfortable, easy to read, and visually coherent while preserving the already-working controller engine, multitouch behavior, calibration, transport logic, haptics, and low-latency input path.

The visual reference establishes the **layout hierarchy and color family**, not a requirement to reproduce a photorealistic or 3D gamepad.

---

## 2. Non-negotiable priorities

Implementation decisions must follow this order:

1. input responsiveness;
2. reliable multitouch;
3. ergonomic touch targets;
4. clear connection/status feedback;
5. responsive layout across landscape phones;
6. visual clarity and consistency;
7. optional decorative polish.

No visual effect is allowed to increase perceived input latency, create frame drops, cause touch-region regressions, or complicate the gameplay hot path.

---

## 3. Visual direction

Target:

- clean;
- soft;
- premium;
- friendly;
- readable;
- controller-like without pretending to be a physical 3D render.

The final UI **does not require**:

- 3D rendering;
- realtime blur;
- heavy shadows;
- expensive depth effects;
- textured materials;
- glossy shaders;
- animated gradients;
- continuously animated decoration.

Simple fills, subtle gradients, lightweight outlines, and state color changes are preferred.

If an effect is not essentially free to render, omit it.

---

## 4. Locked palette

The final color family is:

- off-white / very light gray body;
- lavender primary controls;
- mint/green analog and utility accents;
- neutral gray inactive indicators;
- green active transport indicator.

Recommended tokens:

| Token | Value | Purpose |
|---|---:|---|
| `surface.base` | `#F4F3F7` | gameplay background |
| `surface.raised` | `#FAFAFC` | optional control highlight |
| `surface.outline` | `#DEDBE5` | rings / separators |
| `text.primary` | `#55525B` | dark text/icons |
| `text.onColor` | `#FAFAFC` | labels on colored controls |
| `lavender.base` | `#B7A5E2` | ABXY, D-pad, shoulders |
| `lavender.pressed` | `#9D89CF` | pressed state |
| `lavender.disabled` | `#CEC6DF` | unavailable/disabled |
| `mint.base` | `#92DFA0` | sticks and utility controls |
| `mint.pressed` | `#71C982` | pressed mint state |
| `mint.dark` | `#509660` | optional accent/ring |
| `indicator.active` | `#69D67A` | authoritative transport icon |
| `indicator.inactive` | `#B8BBC2` | inactive transport icon |
| `indicator.warning` | `#E9B35E` | battery warning |
| `indicator.critical` | `#D96C6C` | low battery/error |

Minor color tuning is allowed during implementation, but the white/lavender/mint identity must remain recognizable.

---

## 5. Locked spatial layout

The relationship between controls is fixed for v1:

```text
┌──────────────────────────────────────────────────────────────┐
│ LT    LB                 STATUS                  RB    RT    │
│                                                              │
│                       NATSX LOGO                             │
│                           ━                                  │
│                                                              │
│   LEFT STICK       utility      utility          Y           │
│                                                              │
│                    D-PAD       RIGHT STICK     X   B         │
│                                                              │
│                                                 A            │
└──────────────────────────────────────────────────────────────┘
```

Rules:

- LT and LB remain upper-left.
- RB and RT remain upper-right.
- large left stick remains on the left.
- D-pad remains lower-center-left.
- right stick remains lower-center-right.
- ABXY remains on the far right.
- utility buttons remain around the center.
- transport/battery status remains top-center.
- logo and connection lamp remain center-top.

Responsive adaptation may move controls by a few percent, but must not change this spatial relationship.

---

## 6. Figma geometry and fullscreen presentation

The current reference is Figma frame 1:2, **2400 × 1080**, documented with exact
bounds in [the source manifest](controller-figma/README.md). It supersedes the
older construction guide. Its 50px shoulder offsets are part of the design;
do not add another outer margin around the complete canvas.

`ControllerDesignViewport` fills the available window. Horizontal and vertical
anchors follow the complete window independently, while circle diameters,
button dimensions and local D-pad/ABXY offsets use one uniform scale. This
keeps circles round and the control groups coherent on other aspect ratios.
Saved custom positions remain normalized to the window. Android hides both
system bars and draws through display cutouts; insets do not shrink the canvas.

Analog plates have diameter 500, dark sockets 445, the movable light-green
third circle 380, and its inner circles 280 and 250. The complete third circle
and its two children move together. Visual travel is clamped to 60 reference
pixels radially, reaching the white plate while the dark socket stays fixed. Processing radii, calibration
and transmitted analog values remain independent of this visual travel.

All buttons have a white backplate following the same silhouette as the colored
face. Shoulder bevels use 12px rims; utility and circular keys use 6px rims.
LT/RT use the original contour for their plate instead of a separately expanded
rounded rectangle. Native concentric gradients prevent raster seams around the
analog circles. Shadows, bevels and pressed sprites are cached at layout time;
gameplay draws cached sprites without per-frame blur or bitmap allocation.

Connection messages distinguish pairing, USB permission, connecting and a
disconnected receiver. Only authenticated receiver authority clears the
message. The canonical application ID is `com.natsx.controller`, avoiding an
independent preview installation with separate pairing identity and runtime.

---

## 7. Top status area

The gameplay status area is compact and non-blocking.

Recommended order:

```text
Wi-Fi   Battery   Bluetooth   USB
```

Status icon height:

- approximately 3.5–4.5% viewport height.

Horizontal spacing:

- approximately 1.5–2.5% viewport width.

The status area must never overlap shoulder hitboxes.

---

## 8. Transport indicator semantics

Status icons represent the **authoritative transport currently driving gameplay**, not merely whether a transport socket exists.

Only the authoritative transport is active-green.

Examples:

### Wi-Fi authoritative

- Wi-Fi: green;
- Bluetooth: gray;
- USB: gray.

### Bluetooth authoritative

- Bluetooth: green;
- Wi-Fi: gray;
- USB: gray.

### USB authoritative

- USB: green;
- Wi-Fi: gray;
- Bluetooth: gray.

Standby/warm/connected-but-not-authoritative transports remain gray in the gameplay UI.

Detailed candidate/health state belongs in diagnostics, not the gameplay screen.

---

## 9. Connection lamp

A short pill-shaped indicator sits below the NATSX logo.

Recommended size:

- width: 5–7% viewport width;
- height: 1.2–1.8% viewport height.

Mapping:

| State | Lamp |
|---|---|
| USB authoritative | lavender/purple |
| Wi-Fi authoritative | green |
| Bluetooth authoritative | soft white |
| disconnected | neutral gray |
| reconnecting | current/target transport color with lightweight pulse |

Recommended colors:

- USB: `#AA8BE8`;
- Wi-Fi: `#69D67A`;
- Bluetooth: `#F6F6FA`;
- offline: `#B9BBC2`.

Because Bluetooth uses white, it must retain a subtle outline so it remains visible on the light background.

The lamp is informational only and has no touch target.

---

## 10. Battery indicator

The battery indicator represents the Android phone battery.

Default gameplay UI displays an icon only; percentage is reserved for diagnostics/settings unless later usability testing proves otherwise.

Suggested states:

- above 30%: green/normal;
- 15–30%: soft amber;
- below 15%: soft red;
- charging: charging glyph/state.

Battery updates must not trigger unnecessary high-frequency recomposition/redraw.

---

## 11. Button visual states

Visual state changes must be cheap and immediate.

### Idle

- base fill;
- normal outline;
- no continuous animation.

### Pressed

Preferred implementation:

- fill changes to pressed token;
- optional 1–2 dp equivalent visual offset or very small scale change only if already cheap in the current renderer;
- no Material ripple requirement;
- no blur animation.

Target response:

- visible feedback begins immediately on pointer down;
- transition duration, if animated at all: approximately 50–80 ms.

### Held

- remains visually pressed;
- no looping animation.

### Release

- returns to idle in approximately 70–120 ms;
- a direct state switch is acceptable if animation causes complexity or performance cost.

### Disabled/unavailable

- reduced contrast;
- no misleading active glow.

---

## 12. Analog visual states

### Idle

- cap centered;
- mint base.

### Touch/drag

- cap position follows the already-processed analog state;
- optional slight ring/outline emphasis.

### Critical rule

Visual stick position must use the same processed value used by the controller state.

Do not build a second independent analog-processing path for visuals.

This prevents mismatch between what the player sees and what the game receives.

No haptic is emitted continuously during analog drag.

---

## 13. D-pad states

The D-pad appears as one cross.

Internally:

- Up;
- Down;
- Left;
- Right.

On press, only the relevant arm(s) change state.

Examples:

- Up -> upper arm pressed;
- Up + Right -> upper and right arms pressed.

Diagonal input remains supported through simultaneous directional regions.

---

## 14. Touch hitbox rules

Visible geometry and touch geometry are separate.

Primary rule:

```text
touch target >= visual target
```

Touch expansion should improve comfort without creating accidental overlap.

Priority when expanded regions overlap:

1. pointer already owning a control;
2. active analog gesture region;
3. exact visible digital-control region;
4. expanded digital hit region.

Existing pointer ownership and multitouch rules remain authoritative.

---

## 15. Required multitouch behavior

The visual overhaul must preserve combinations including, at minimum:

- left stick + A;
- left stick + B;
- left stick + RT;
- left stick + RT + A;
- left stick + right stick;
- left stick + right stick + shoulder;
- D-pad + ABXY;
- simultaneous shoulder + face-button interaction.

Logo, lamp, and status icons must not consume gameplay touches.

---

## 16. Pairing and transient status UX

The current large gameplay message such as:

```text
USB — Pairing offer sent. Waiting for Windows response...
```

must not remain as a large permanent gameplay banner.

Normal transient connection status should use a compact center-top message such as:

```text
Pairing…
Reconnecting…
Connected via USB
```

A dedicated temporary overlay is allowed only when explicit user action is required, such as SAS/code confirmation.

That overlay must disappear after the action is complete.

---

## 17. Smart handover UX

Transport switching should be visible but unobtrusive.

Example:

```text
Wi-Fi authoritative
        ↓
USB becomes healthy
        ↓
Smart Connection Manager promotes USB
```

UI transition:

- Wi-Fi icon green -> gray;
- USB icon gray -> green;
- lamp green -> lavender.

No gameplay-blocking dialog.

No toast is required in normal gameplay mode.

Detailed handover reasons remain available in diagnostics.

---

## 18. Responsive layout rules

### 18.1 Landscape only

Gameplay remains landscape.

### 18.2 Safe-area aware

All important controls must avoid:

- display cutout;
- camera hole;
- navigation/gesture insets;
- excessively rounded screen corners.

### 18.3 Height-driven sizing

Major controls should primarily scale from usable viewport height.

Very wide phones should gain spacing rather than oversized controls.

### 18.4 Maximum scale

On tablets or unusually large displays, major controls should stop growing around approximately 1.20–1.25x the reference scale.

Extra space becomes margin.

### 18.5 Small-height devices

When height is constrained, reduce in this order:

1. decorative spacing;
2. logo/status spacing;
3. optional outlines/details;
4. visible control size only when necessary.

Do **not** aggressively shrink touch regions.

### 18.6 Spatial relationships stay locked

Responsive rules may adjust spacing, but must not relocate control groups into a fundamentally different controller arrangement.

---

## 19. Performance budget

The gameplay UI must remain lightweight.

Prefer:

- Android hardware-accelerated Canvas / existing custom gameplay renderer;
- solid fills;
- simple rounded paths;
- cached decorative gradients and sprite shadows only when visually valuable;
- cached geometry/paths;
- cached text metrics;
- minimal allocations during pointer movement;
- state-driven invalidation only where practical.

Avoid:

- realtime bitmap blur;
- software shadow layers on every frame;
- per-frame image decoding;
- shader-heavy effects;
- complex clipping stacks;
- continuous decorative animations;
- allocating new objects on every `ACTION_MOVE`;
- rebuilding static geometry every frame.

The UI redesign must not reduce existing input performance.

---

## 20. Animation budget

Animation is optional, not required.

If used:

- button press: 50–80 ms;
- release: 70–120 ms;
- transport color change: 100–180 ms;
- reconnect pulse: approximately 800–1000 ms.

No gameplay control needs animation longer than 200 ms.

If a transition complicates the hot path or causes frame instability, use an immediate state change instead.

---

## 21. Haptic behavior

Existing configurable haptics remain authoritative.

Digital buttons:

- haptic on initial pointer-down according to current setting;
- no repeated haptic every rendered frame while held.

Analog:

- no continuous drag haptics.

UI redesign must not add new unconditional vibration.

---

## 22. Accessibility and clarity

Minimum requirements:

- labels remain legible at normal landscape distance;
- active/inactive connection state must not rely on brightness alone where an icon shape already communicates transport identity;
- disconnected state remains understandable;
- important controls retain adequate contrast;
- status indicators remain secondary to gameplay controls.

Advanced accessibility options remain post-v1 unless required to correct a concrete usability issue.

---

## 23. State hierarchy

Visual importance:

### Tier 1 — gameplay controls

- sticks;
- ABXY;
- D-pad;
- shoulders.

### Tier 2 — utility controls

- View;
- Menu;
- LS;
- RS;
- optional Share/action.

### Tier 3 — system identity

- NATSX logo;
- connection lamp.

### Tier 4 — compact status

- active transport;
- battery;
- transient connection text.

The status system must never visually compete with the controls.

---

## 24. Implementation boundaries

This work is a **presentation-layer redesign plus status binding**.

It must not rewrite or weaken:

- `GamepadStateStore`;
- pointer ownership;
- analog processing;
- calibration;
- deadzones;
- response curves;
- haptic settings;
- pairing security;
- trusted-peer logic;
- Smart Connection Manager;
- transport selection;
- USB/Wi-Fi/Bluetooth protocol;
- session sequencing;
- reconnect logic.

Existing functional behavior should be reused and rebound into the new UI.

---

## 25. Final acceptance criteria

The UI redesign is complete only when all of the following are true:

- [ ] final white/lavender/mint palette is implemented;
- [ ] control layout follows this specification;
- [ ] all existing gamepad mappings still work;
- [ ] left and right analog behavior is unchanged functionally;
- [ ] D-pad diagonals still work;
- [ ] multitouch regression tests pass;
- [ ] enlarged touch regions remain comfortable without harmful overlap;
- [ ] Wi-Fi/Bluetooth/USB icons reflect the real authoritative transport;
- [ ] connection lamp reflects the authoritative transport color;
- [ ] phone battery state is shown correctly;
- [ ] disconnected and reconnecting states are clear without blocking controls;
- [ ] pairing no longer permanently occupies a large gameplay banner;
- [ ] safe-area behavior works across supported landscape aspect ratios;
- [ ] existing calibration/profile/settings behavior remains intact;
- [ ] no new input-latency regression is introduced;
- [ ] no meaningful gameplay-frame regression is introduced;
- [ ] Android CI and gameplay/input tests remain green.

---

## 26. v1 design decision

For v1, **good UI/UX does not mean maximum visual effects**.

The intended result is:

> a clean, attractive, comfortable controller interface that feels deliberate and polished while staying fast, predictable, and easy to use.

When visual fidelity conflicts with performance or control ergonomics, **performance and ergonomics win**.

## 27. Reference skin verification (2026-10-02)

- Local `assembleDebug`, `assembleDebugAndroidTest` and `lintDebug` pass.
- All 126 existing unit tests pass. Two temporary Robolectric/native Skia
  tests additionally render the production View and route real MotionEvents
  at 1870 × 841, 2400 × 1080 and 1920 × 1080.
- Four simultaneous pointers (both sticks + RT + A), independent A release,
  cancel neutralization, Up+Right D-pad, symmetric cutout insets and raster
  cache reuse across 60 state redraws pass.
- `SkinVerificationInstrumentation` preserves the same native device checks
  without adding production dependencies. The screenshot CI workflow is
  included but has not yet run on the remote branch.
- Native Skia previews were visually inspected. These checks establish geometry
  and input behavior, not physical-device GPU frame-time or battery measurements.
  Those remain open and the APK is a debug build, not a production release.

### 50px correction verification

The subsequent correction adds four geometry regression tests (exact native
artboard margins, cutout absorption, uniform 16:9 scaling, large-inset safety).
Native Skia checks also verify a clear cap gradient after the stipple-alpha
reset. Debug APK and instrumentation APK builds, lint, 130 repository unit
tests and two temporary native render/touch tests all pass (132 tests total). Device GPU timing
and extended physical gameplay remain unmeasured.

### Pairing overlay regression

The gameplay HUD refactor had removed construction of the Android pairing
overlay while leaving its listener registered. The receiver displayed its
comparison code, but Android silently skipped showing the prompt and could
not approve trust; authenticated input consequently never started. The full
screen code/Confirm/Reject overlay is restored above the gameplay HUD. It
releases current inputs, consumes gameplay touches while approval is pending,
and hides when confirmation ends. Native Activity checks cover visible codes,
both approval outcomes, and A press/release through the complete View hierarchy
after the overlay closes. The normal USB trust/handshake protocol is unchanged.

### Construction-guide finishing verification

Debug APK, instrumentation APK and lint pass. All 133 local checks pass: 130
repository unit tests plus native surface rendering/input and real Activity
pairing verification. Five measured contour rows pass at all three viewport
sizes. Physical GPU timing and extended gameplay remain device checks.
