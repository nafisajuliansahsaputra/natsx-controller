# Controller Figma source

Source: https://www.figma.com/design/pMygEy47TwjmVKEenQGzRN/Untitled?node-id=1-2

The shared node 1:116 is the RB background; the complete reference is frame 1:2, 2400 × 1080, background #F5F5F5. Native SVG exports retain the original paths and filter effects. They are rasterized with Inkscape at their intrinsic dimensions into Android assets/controller; no network or SVG engine is required during gameplay. Plus Jakarta Sans Bold is bundled under the accompanying SIL Open Font License.

| Element | Reference bounds / center |
| --- | --- |
| Left / right analog | Centers (325,540), (1500,780); plates 500×500 |
| D-pad | Center (900,780); outer plate 500×500; arms 160×160 |
| LT / LB / RB / RT | (50,50), (450,50), (1600,50), (2000,50); 350×150 |
| Back / Menu | (699,250), (1514,250); 187.301×75 |
| LS / RS | (771,375), (1441,375); 187.301×75 |
| Guide | (965,375); 469.301×75 |
| Y / X / B / A | Centers (2074.91,378.91), (1915.91,539.91), (2235.91,539.91), (2074.91,700.91); colored diameter 177.82 |

LB/RB and utility keys gain a matching drop shadow; silhouettes, placements and labels follow the reference. Native rounded rectangles use Figma's affine transforms and are unioned before bevel rendering. Full-size keys use 12px inset shadows and 6px drop spread, utility keys use proportionate 6px/3px depth. These effects and pressed states are baked during layout. The hot path draws cached bitmaps and D-pad feedback paths only.

Regenerate each PNG with `inkscape NAME.svg --export-type=png --export-filename=android/app/src/main/assets/controller/NAME.png` at the SVG's intrinsic dimensions.

Uniform viewport fitting, safe insets, saved user layout positions, analog calibration/travel, authoritative gamepad state, transports and pairing remain independent of skin rendering. Existing custom layouts intentionally retain their saved placements; restore the default layout in settings to see the complete Figma placement.

Validation: clean Android build, lint and 130 repository unit tests passed. Native Android API 29 instrumentation passed rendering, safe insets, four independent pointers, D-pad diagonals, release/cancel, cached redraws, and Activity comparison-code confirm/reject/input unblock. Native Skia API 35 verification repeated the render/touch checks at three viewport sizes and the Activity pairing flow (two additional local regression tests).

The downloadable NATSX Figma preview uses application ID com.natsx.controller.figma and a separate label. The previous ephemeral debug signing key is unavailable, so this preview installs beside the working version. Repository builds retain com.natsx.controller; no transport/protocol changes are needed.
