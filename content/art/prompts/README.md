# Image prompt recipes

Working generation inputs for [the exploratory art direction](../../../docs/art-direction.md).
Use the available GPT image-generation tool for generation and image editing.
This folder is plain editable prompt text, not a runtime loader or a new tool.

## Compose a request

Concatenate in this order:

1. [`shared-style.md`](shared-style.md)
2. **One** treatment: [`ink-wash.md`](ink-wash.md) (provisional default) or
   [`painted-cover.md`](painted-cover.md)
3. One asset contract: [`character-sprite.md`](character-sprite.md),
   [`prop-sprite.md`](prop-sprite.md), or [`material-texture.md`](material-texture.md)
4. A filled-in subject block below, or an equally concrete local asset brief.

Asset lighting/background/view requirements override general cover-art language.
Do not ask for a literal book cover, typography, decorative frame, paper backdrop,
or dramatic scene lighting when requesting a runtime asset. Fill every bracketed
field. Set the generation tool's transparency/reference options where available;
prompt text alone is not proof that an output meets the contract.

Keep a batch on the same treatment, palette, scale assumptions and light setup.
Change one meaningful variable for a comparison. Keep normal-map generation out
of the default request. Check the actual result in-engine before expanding a set.

## Starter subjects

### Shared character comparison

> Subject: an early-modern fantasy garrison sentry, an ordinary working soldier
> rather than an ornate hero. Worn ochre wool coat, ivory shirt, muted teal sash,
> simple dark leather belt and boots. One long gunpowder rifle held in a readable
> low-ready pose; one modest ammunition pouch. A distinctive but believable
> silhouette, restrained equipment and no modern tactical accessories.
> View: front, full body, both feet visible. Weapon silhouette separated from
> the torso; all extremities within the frame. Modest intrinsic form shading,
> no muzzle flash, lamp glow or environmental cast shadow.

Generate this same brief with each variant; do not change costume, pose or
subject between them. The worker and elf images are optional style anchors,
not instructions to make every actor resemble those characters.

### Billboard feature

> Subject: one freestanding early-modern repair bench with a thick worn timber
> top, sturdy legs, a small iron vise and two clearly arranged hand tools.
> Modest irregularities, no dense pile of equipment. Front three-quarter view
> with very restrained perspective, full legs visible. The center of the bench
> is the interaction focus. No wall, floor, room, floating labels or light source.

For a lantern use the same prop contract but request a separate isolated lantern;
keep any painted flame local to its glass, with no surrounding glow cloud.

### Voxel surface

> Subject: a repeatable field of warm, sooted brickwork with muted ochre/red
> variation and pale worn mortar. Bricks remain legible without dense cracks or
> photographic grain. Horizontal courses with mildly irregular hand-worked
> shapes. No arch, border, doorway, objects, text, lit corner or dominant stain.
> Repeat: both axes. Scale intent: several readable brick courses across a
> two-metre wall patch, to be checked against the configured voxel/world scale.

Limewash and worn stone floor variants should preserve this batch's palette and
mark density. Change the material description, not the shared art treatment.

## Directional continuation

After choosing a character image, use it as the identity reference for each
additional view. Reuse its exact subject brief and add:

> Depict the same individual in the same outfit and pose from [front / its left
> side / rear / its right side]. Preserve body proportions, equipment, rifle
> length, handedness, costume colors and wear placement. Turn the viewpoint,
> not the design. Keep the camera level, projection, full-body framing and sole
> baseline consistent with the reference. One view only; no contact sheet.

Name sides from the subject's perspective; confirm how runtime facing sectors
map to them. Do not mirror away handedness or asymmetric gear. Later diagonal
views use this same identity and contract if four views prove too abrupt.
