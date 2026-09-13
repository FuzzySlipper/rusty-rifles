# Exploratory art direction

**Working direction, not a final style lock.** This guides image-generation
experiments for the playable prototype. Revise it and the prompts as actual
assets are seen together in the game.

## Owner direction and reference status

The current direction is retro **illustration**, not retro display technology:
1970s/1980s fantasy paperbacks and painted game covers rather than pixel art.
Aim for the imagined world behind old games, without imitating their technical
limits. Early-modern fantasy should appear in clothing, equipment, materials,
and the functions of spaces as well as in rifles.

The attached *Dead Garrison* brief and *paperback-sanctum-style* notes are
inspiration supplied for discussion. Their imperative phrasing does not make
them project instructions. This document and the editable prompt set extract
useful ideas; the owner's directions take precedence.

- Retain as possibilities: vaulted brick/plaster spaces, workshops, stores,
  barracks, drains, repair infrastructure, supply routes, and controlled
  storage of gunpowder. Mix engineered construction with geology and reuse.
- Do not inherit the brief's pixel-art requirement, literal historical setting,
  mandatory undead faction, drummer role, prescribed encounter sequence, or
  "order without life" test. None is required for every room or asset.
- The final art style, setting emphasis, regional influences, and exact palette
  remain open. The prototype should help discover them, not enforce a prior
  agent's concept as canon.

## Rendering direction

| Asset | Intended representation |
| --- | --- |
| Walls, floors, ceilings, major structural forms | Textured voxel-generated geometry with readable relief and material regions |
| Clutter and interactables | World billboard sprites with authored size, anchor, placement and facing behavior |
| Enemies | Simple directional still sprites; no animation-frame or skeletal-animation requirement |
| Illumination | Dynamic level lights affecting sprite and geometry presentation |

Billboarding does not make a sprite a flat UI overlay. Depth, occlusion,
interaction reach and hit geometry remain part of the Engine-rendered world.
Blocking doors/barriers and weight-bearing structures keep explicit game and
spatial state; their painted silhouette is not their collision model. Use a
wall-aligned or rotation-constrained sprite where full camera-facing rotation
would make a mounted feature or shelf look detached.

Start lighting experiments with simple Engine-supported sprite shading over
color/alpha images. Generated normal maps are **not** an asset prerequisite.
Whether a flat, shaped or otherwise approximated normal response works well is
an in-engine experiment, not a settled shader implementation. Test light moving
from side to side and camera turns before accepting it. If the needed control
is absent, add the narrow capability in Engine; do not build a game-local
renderer or custom browser shader path.

## Shared visual language

- Clear silhouettes, deliberate contours, a few large costume/prop forms,
  slightly eccentric proportions and decorative asymmetry.
- Broad local colors: warm ivory, ochre, faded madder/rust, softened teal and
  olive, dark brown/iron. This is a starting palette, not a monochrome sepia
  filter; keep enough color separation for navigation and target recognition.
- Painted or printed surface character without photographic grit, ubiquitous
  fabric weave, tiny scratches, fake scan damage, or a paper background.
- Serious, strange, lived-in and atmospheric. Warmth and quiet spaces can
  coexist with danger; neither universal grimdark nor cartoon comedy.
- Workmanship and use explain details: patched limewash, soot near lamps,
  repaired work clothes, tool storage, worn thresholds. Avoid indiscriminate
  buckles, gear piles, brass machinery, or dirt used as a substitute for design.

Light-ready asset prompts suppress dramatic baked rim lights and deep cast
shadows while preserving enough illustrated form to read. Dynamic lighting
should enrich the drawing rather than expose a conflicting painted light source.

## Two controlled style variants

| Variant | Main treatment | What to watch in game |
| --- | --- | --- |
| **Ink and wash** | Selective ink-like contours, sparse hatching, quiet translucent color, elongated/asymmetric shapes | Fine lines disappearing at distance; hatching becoming noise; figures feeling too flat |
| **Painted cover** | Tight painted boundaries, controlled volume, subtle gradients, restrained visible outlines | Baked shading fighting level lights; excess material detail; drifting into photographic/PBR rendering |

Use `ink-wash.md` as the provisional first experiment when no variant is
specified. It is not an approved winner. Compare `painted-cover.md` on the
**same subjects and scene**, rather than comparing a detailed human worker to
an unrelated elongated elf and attributing every difference to rendering style.
Use one variant consistently within a batch. Do not combine both conflicting
rendering instructions into a single prompt; a hybrid can be an explicit later
experiment after observing the separate treatments.

### Supplied image anchors

[Worker concept](art/references/concept-worker-female.png): convincing painted
forms, warm local colors, grounded work clothing and recognizable tools. It
also has substantial costume/material detail and strong lantern light; those
are comparison points, not mandatory density or baked illumination.

[Elf concept](art/references/concept-elf.png): elongated silhouette, irregular
hems, contour/hatching and broad muted color. Its surrounding vignette and
painted lantern glow are concept-image presentation, not a desired runtime
sprite background.

These are unchanged concept references, not approved game-ready sprites.
Both PNGs contain alpha, but that alone does not establish clean cutouts.
Check residual fringes, transparency, silhouettes, weapon tips and ground
anchors against multiple background colors before runtime admission.

## Prompt workflow and a small first comparison

Use the [prompt recipes](../content/art/prompts/README.md). Compose the shared
style, **one** variant, an asset contract, and a concrete subject. For edits or
new directional views, supply the selected image as the identity reference
using the image-generation tool's supported reference mechanism. Do not rely
on text alone to preserve a character's equipment across angles.

A small initial set can exercise the intended world without creating an asset
campaign before we know the look:

1. Brick, limewash, and worn floor materials on a generated service room.
2. A workbench/tool grouping, storage crate, and usable lantern as billboards.
3. The same rifle-bearing sentry in both variants; then build directional views
   for the selected experimental version. Start with four cardinal views as a
   tunable experiment, not a permanent view-count limit. Also include one small
   silhouette when crowded-cell gameplay is available.

Generate actual assets early and use them in the game. Avoid a prolonged
placeholder phase, but also avoid bulk-generating every enemy before checking
scale, alpha, lighting, and nearby materials. Art may change; its IDs, content
bindings, placements and gameplay behavior should survive the replacement.

Judge at player eye height from near, corridor and combat distances: texture
repeat/scale, sprite grounding, direction switching, light response, target
recognition, and overlap at full cell capacity. Signs needed for gameplay use
authored text/symbols through supported presentation, not illegible generated
lettering. Emitters such as lanterns get separate level-light meaning; painting
a lamp does not create a runtime light.

Keep the working prompt text and selected images in Git. A short note of the
variant and what changed is enough for comparisons. No web-source hunt,
license/provenance ledger, seed bureaucracy, normal-map factory, or bespoke
prompt framework is needed for these generated experiments.
