# White controller design

The right analog now uses the same `imgEllipse5` white plate/glow as the left, per the owner's bevel correction. Separate cap images and the common socket remain intact. Native render checks compare their outer bevels at matching radial positions across all three aspect ratios.

Source: https://www.figma.com/design/3XXaIyhGotYr3ktNj5sjUo/Untitled?node-id=1-2

Frame 1:2 is 2400 × 1080, background #FAFAFA. Implementation follows its high-fidelity design context. The 25 original static layers and 13 key faces are exported directly through the Figma Plugin API as PNG at scale 1, retaining effects and the node transforms. `white-assets.json` records each node, layout bounds and rendered bounds. These exports are bundled under `android/app/src/main/assets/controller/figma-white`; no network request or SVG renderer is used during gameplay.

Original key face exports use the supplied #C3B1E1 / #B2EBB2 colors, Plus Jakarta Sans Bold, 48px face labels, 24px LS/RS labels and 30px original utility icons. The exported faces preserve the exact gray #767676 drop depth, white 50% inset highlight and black 25% inset shadow. Previously flat LB/RB and utility keys gain matching white bevel and depth, with their affine silhouettes unioned to avoid seams.

Analog exports retain separate left/right cap images and fixed 500px plates / 445px sockets. The complete 380px cap, its 280px ring and 250px center move together. Existing 110px visual travel, 180/160px input processing radii and touch ownership remain unchanged. D-pad node exports already contain rotation/reflection and are composed once at their original bounds.

Uniform control dimensions use the existing viewport with independently adapted anchors. Saved custom positions remain supported. Thin panel contours share the light bar's authoritative committed transport color. Input protocol, USB takeover and pairing behavior are unchanged.

Verification uses the actual Android View/Canvas at three aspect ratios, validates native rendering, movement, connection colors, multitouch, release/cancel, pairing and Activity input to authenticated USB packets. An updated APK uses the restored existing debug signing identity so it can update in place. Production signing and physical Windows/game verification remain separate work.
