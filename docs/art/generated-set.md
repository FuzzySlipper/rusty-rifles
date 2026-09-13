# First playable art comparison

Built-in GPT image generation produced the selected PNGs under
`content/art/generated/`. Compose future calls from the shared style, one
named treatment and the sprite/material recipe in `content/art/prompts/`.
These are provisional experiments, not a final style decision.

## Selected prompt set

All calls used `stylized-concept`; companion views used `identity-preserve`
and treatment edits used `style-transfer`. Shared constraints: original
1970s/1980s fantasy paperback illustration, early-modern useful construction,
clear silhouettes, quiet local color, no pixel art, photographic microtexture,
text, frames, cinematic light or glossy rendering. Texture calls requested
opaque edge-to-edge seamless repeats, orthographic view and uniform neutral
illumination. Sprite calls requested full silhouettes, genuine alpha, neutral
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

Ink-and-wash requests used selective expressive contours, sparse hatching and
broad matte painted color. Painted-cover edits preserved the matched subject,
layout, silhouette and equipment while requesting painted boundaries, minimal
outline/hatching and controlled volume. The painted sentry is noticeably more
highlighted; judge this under changing runtime lights before promoting it.

The selected ink front supplied identity for three separate companion calls:
rear (rifle on viewer right), right profile (nose right, rifle on near side),
and left profile (nose left, rifle on far side, left hand at belt). They are
separate images, not mirrored copies. The import file carries each image's
own measured size and foot anchor, compensating for small framing differences.

## Controlled comparison scope

Ink-and-wash is the provisional complete directional set. Painted-cover
replaces all three surfaces, the crate and the sentry **front**. Bench, lantern
and other sentry views deliberately share the accepted ink images. This is a
matched front-view treatment study, not a claim of a complete painted cast.
Some painted bench/lantern/rear outputs had RGB checkerboard backgrounds;
a targeted transparency retry also failed. Those outputs were rejected and
are not runtime assets. Do not mistake a drawn checkerboard for alpha.

The six opaque textures were losslessly re-encoded as RGBA8 for Engine admission.
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
