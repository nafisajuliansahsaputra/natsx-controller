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

Every key now has a white backplate following its original contour: 12px shoulder rims and 6px utility/circular rims. LT/RT no longer combine mismatched expanded rectangles. Native rounded rectangles retain the Figma affine transforms and are unioned before bevel rendering. Gradients, shadows and pressed states are cached during layout; gameplay draws cached bitmaps.

The analog is rendered with concentric native circles: fixed white plate diameter 500 and dark socket 445; movable third light-green circle 380, with its inner 280/250 circles attached. Radial visual travel is limited to 32.5px so the complete moving cap stays inside the socket. Existing input calibration and processing radii are unchanged. Original SVG exports remain the provenance for paths, colors and assets.

The canvas fills the window with independently adapted anchors and uniform control dimensions; circle shapes and D-pad/ABXY spacing stay coherent. Android hides system bars and permits drawing through cutouts. Saved custom layouts retain their placements; restore the default layout in settings to see Figma's defaults.

Builds use the canonical `com.natsx.controller` identity. The earlier separate `NATSX Figma` preview had independent trust/preferences and could run alongside the original foreground service. Remove both older installations before installing this signed build and pair again once: the earlier ephemeral debug signing key is unavailable. Connection text makes pairing, USB permission and disconnected/connecting states visible instead of implying local touch is receiver readiness.

Validation: clean APK/instrumentation build and lint passed, as did all 134 repository unit tests. Two additional local native Skia/API 35 tests passed render/multitouch/release/cancel at 2400×1080, 1870×841 and 1920×1080, third-circle movement/edge input/white bevel checks, fullscreen cutout configuration, Activity comparison-code confirmation/rejection, and authenticated USB-output checks. `UsbInputVerification` dispatches real Activity touches through the store, realtime publisher and USB sender and decodes authenticated frames in an output loopback. This validates packet production; a physical phone-to-Windows USB/game check remains necessary.
