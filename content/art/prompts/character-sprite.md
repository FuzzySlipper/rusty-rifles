Asset contract: one isolated full-body character still image for a dynamically
lit world sprite in a first-person grid dungeon. Exactly one individual and
one specified view, without an animation sequence or multi-view sheet.

Use near-orthographic, level-camera framing with little perspective distortion.
Keep feet, head, weapon tips and accessories fully visible, with clear margin.
Both soles share a consistent baseline; the ground anchor is centered beneath
the stance. Pose should read while standing in a group without very wide limbs
or a weapon obscuring the entire neighboring silhouette.

Output on a genuinely transparent background: no painted backdrop, black or
white matte, checkerboard, floor patch, vignette, cast shadow, detached glow or
selection outline. Preserve fine contours without fuzzy background fringes.

Use neutral, diffuse, low-contrast illumination. Preserve local colors and
modest intrinsic form shading while suppressing strong baked directional
shadows, rim lights, colored illumination and glossy hotspots. Runtime level
lights supply changing illumination. Do not produce a normal map or a map grid.
Emissive objects, if explicitly requested, must not paint a glow across the
whole character or transparent surround.

Follow the supplied subject, camera direction and identity reference precisely.
For related views, preserve handedness, gear placement, proportions and framing.
