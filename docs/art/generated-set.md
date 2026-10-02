# Generated world art

Runtime PNGs live under `content/art/generated/`. Compose generation requests
from the shared style, one named treatment and the sprite/material recipe in
`docs/art/prompts/`. This document describes the asset set and import contracts;
generation history and comparison results belong in Den.

## Asset briefs

Shared authoring constraints: original 1970s/1980s fantasy paperback
illustration, early-modern useful construction, clear silhouettes,
quiet local color, no pixel art, photographic microtexture,
text, frames, cinematic light or glossy rendering. For textures, request
opaque edge-to-edge seamless repeats, orthographic view and uniform neutral
illumination. For sprites, request full silhouettes, genuine alpha, neutral
form shading, no floor, cast shadow, halo or vignette.

| Asset ID | Concrete subject and scale intent |
| --- | --- |
| brick | Hand-fired rust-red brick, ivory lime mortar, eight staggered courses per two-metre square |
| limewash | Warm ivory/ochre matte limewash, quiet trowel marks, no exposed brick or landmark stain; two-metre repeat |
| floor | Four-by-four teal-grey/olive-grey square slate slabs with narrow warm grey joints; two-metre repeat |
| bench | Ochre wooden workbench, dark iron vice, hand plane and folded olive cloth; front view with modest visible top, 0.9m tall |
| crate | Closed ochre plank crate, two dark iron straps, three-quarter view; 0.75m tall |
| lantern | Unlit dark iron rectangular lantern, ivory horn panes, loop and stout base; 0.5m tall |
| sentry-front | Brown-skinned woman, short dark hair, olive soft cap, rust knee coat, ivory shirt, teal breeches, cuff boots; rifle upright in anatomical right hand, left hand at belt; 1.8m tall |

Ink-and-wash uses selective expressive contours, sparse hatching and broad
matte painted color. For painted-cover edits, preserve the matched subject,
layout, silhouette and equipment while requesting painted boundaries, minimal
outline/hatching and controlled volume. Evaluate highlights under changing
runtime lights.

The ink sentry's companion views preserve the front image's identity:
rear (rifle on viewer right), right profile (nose right, rifle on near side),
and left profile (nose left, rifle on far side, left hand at belt). Use separate
images rather than mirrored copies. The import file carries each image's
own measured size and foot anchor, compensating for small framing differences.

## Treatment bindings and import

Ink-and-wash supplies the complete directional set. Painted-cover
replaces all three surfaces, the crate and the sentry **front**. Bench, lantern
and other sentry views deliberately share the accepted ink images. The painted
comparison therefore covers the matched front view rather than a complete
directional cast. A drawn checkerboard is not transparency; validate
actual alpha before runtime admission.

The six opaque textures use RGBA8 for Engine admission.
Accepted sprites are RGBA with zero-alpha background and high-alpha interiors.
Authored 0.5 cutout avoids translucent interiors and gives Engine depth writing
for overlap. Generated RGB beneath zero alpha can look like a vignette in a
preview that ignores alpha; it is not a runtime background. Check edges in the
host against ivory, brick and slate, especially the rifle, cap and lantern loop.
The two sentry instances use the same identity at different authored scales;
they exercise individual view selection and depth before enemy variety.

`content/definitions/world-art.json` owns image IDs, paths, dimensions in world
units, pivots, facing modes, synthetic lighting and room placement tuning.
`content/tuning/appearance.json` owns voxel texture bindings and repeats.
Texture hashes are Engine authored-catalog content references, not a source
provenance ledger. Replace an image and update its import dimensions/anchor
(and its texture hash if applicable); gameplay identities remain independent.
