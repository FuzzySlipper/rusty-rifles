# Lighting acceptance — 2026-09-13

Paired SDK/runtime `0.1.0-dev.8c20a96d10ef` (verified release archive and pair
manifest). Live `product-bootstrap.json` reports world default lights disabled
and viewmodel default lights neutral. The game retains its owned point lights
and Engine synthetic sprite normals; no generated maps or local shader.

`bash scripts/check.sh` passes after the pin/content-read changes. The running
broker host uses the new pair. `browser-diagnostics.json` records an open output
and input transport with zero warnings/errors at its read time; it is not a
claim about all later host activity. Ordinary software WebGL warnings remain.

## Visible comparisons

Browser session `f8e56116-bd6f-4fd1-aecc-1b762b2e7a6e` (slot-2) used DOM-assisted
keyboard activation of the actual art controls. Room fill off, moved light,
style switching and camera movement all reached the product.

Wolf GPU session `f946a767-5aba-4cd0-9a25-894a1f880abc` (slot-1) used native
Tab/Shift-Tab/Enter to reach and activate the same controls. Pointer activation
was uncertain; no pointer-success claim is made. W/S/A movement changed camera
position. Both owned sessions were stopped with successful release receipts.

- [Lantern position 1](gpu-lantern.png): warm figures remain readable with room
  fill off; walls/floor show local falloff rather than the old neutral rig.
- [Position 2](gpu-left.png): the foreground figure becomes darker as the source
  moves left. [Position 3](gpu-right.png) changes its illumination again. The
  patrol itself moves during real time, so use the fixed foreground figure and
  architecture for light comparison.
- [Painted treatment settled](painted-settled.png) and
  [one cell farther away](painted-distance.png): the scene retains grounded
  sprites, repeating surfaces and the two figure scales. The painted treatment
  intentionally reuses some ink directional views.
- [Lateral camera position](side-angle.png): the billboard stays grounded as
  view position changes; texture repetition is visible at the near corner.

The first `gpu-painted` checkpoint was captured before the style readback had
settled and still displays ink-wash. It is retained as timing evidence, not
accepted as painted output; `painted-settled` is the comparison to use.

Ink-and-wash remains provisional: linework and clothing remain legible under
warm changing illumination. The illustrated form shading does not produce an
obvious detached bright silhouette in these comparisons. Repeated brick courses
and the flat geometric plate are still apparent. This accepts the early lighting
and style experiment, not final art direction, shadow quality or crowd/combat art.

[captures.json](captures.json) indexes untouched original PNG copies, capture
IDs and supervised scripts. GPU stream evidence does not assert frame-to-engine
correlation or isolated performance.
