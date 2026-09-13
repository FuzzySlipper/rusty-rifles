# Milestone 2: art in the generated dungeon

The existing expedition now presents generated brick, limewash and slate with
world sprites: a usable lantern, inspectable bench/crate, a patrolling rifle
sentry and a smaller stationary sentry. The sentries are visual/occupancy
actors; combat and inventory remain later campaign work.

Open **Art comparison** in the expedition panel. Switch treatment keeps the
same world and gameplay state. Move light cycles authored positions around the
entrance ensemble. Toggle room lights removes the product-owned fill. The packaged host still
adds neutral lighting; this is not yet a lantern-only comparison. Normal movement/turning changes actor
views; pause holds patrol motion while comparison controls still work.

See [the selected art set](art/generated-set.md) for the exact scope of the
painted comparison and rejected variants. Ink-and-wash remains provisional.

## Durable boundaries

- Engine owns sprite billboarding, cutout/depth, synthetic normals, dynamic
  lights, retained resource handles and authored voxel material projection.
  No generated normal maps, custom shaders or DOM world objects are used.
- C# selects the directional image from actor facing and viewer position.
  Each view has a file-authored size and foot pivot. Bench facing is fixed;
  floor props and sentries rotate around world Y.
- Props and the stationary sentry have explicit saved identities and grid
  occupancy, separate from image alpha. Their resolved cells are saved with
  the expedition; swapping art does not regenerate the world.
- Every possible texture is selected during product creation, before Engine
  closes resource selection. Appearances remain alive while snapshots refer
  to them. Material swaps update the retained voxel presentation before old
  resources are released; load publishes replacements before disposing old art.
- Save schema 2 includes the new resolved dressing. Schema-1 saves from the
  primitive milestone are rejected by the Engine schema check. New saves retain
  all earlier expedition state plus the dressing identities and cells.
- Art treatment and light comparison positions are presentation settings;
  they do not affect combat time or saved expedition meaning.

## Tuning

`world-art.json`: sprite paths, physical sizes, pivots, facing, cutout,
synthetic-normal strength, comparison scale, dressing offsets and light
positions. `appearance.json`: voxel material roles, texture sampling/repeat,
voxel subdivision, fill lighting. Repeat dimensions/origins are world metres,
converted to Engine voxel coordinates independently of logical cell size. `exploration-features.json`: gameplay reach,
focus angles, patrol timing and lantern light intensity/range.

Verification and original host captures belong in `docs/evidence/milestone-2/`.

## Lighting acceptance resumed

B05 (#8203) now uses paired Engine `0.1.0-dev.8c20a96d10ef`, consuming
completed Engine #8256. Packaged configuration disables the default world rig
and leaves viewmodel lighting neutral. Product-owned room and lantern lights
control voxel and synthetic-normal sprite shading.

Browser controls and remote Wolf keyboard navigation verified room-fill removal,
three lantern positions, both treatments and changed viewing positions. Ink and
wash remains the provisional choice: readable silhouettes and cloth detail,
with runtime brightness responding to the moved lantern. Painted cover stays an
explicit comparison, not a complete second directional asset set. Brick repeats
remain conspicuous and the simple pressure plate looks flat; these are retained
art-development observations, not reasons to add normal maps or a renderer.

[Original captures and verification](evidence/lighting-resume/README.md).
