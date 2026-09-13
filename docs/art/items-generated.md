# Ink-and-wash items atlas

`content/art/generated/items/ink-wash-items-atlas.png` is the selected first
inventory and world-billboard experiment. It uses the provisional ink-and-wash
treatment: restrained wash colour, selective dark contour, and neutral diffuse
form shading. It is deliberately a compact, coherent equipment set rather
than a final inventory taxonomy.

The PNG is 1776 by 888 pixels, RGBA, with a genuinely transparent background.
It is an exact four-column by two-row atlas: each cell is 444 by 444 pixels.
The atlas contains no normal map. For world billboards, use the Engine's simple
synthetic surface normal for the billboard facing direction; UI icons need no
lighting normal.

| Item | Cell origin px | UV rectangle `(u0, v0, u1, v1)` | Alpha footprint in its cell, px |
| --- | ---: | --- | --- |
| Short flintlock rifle | `0, 0` | `(0, 0, .25, .5)` | `75 x 317 + 202, 71` |
| Sheathless knife | `444, 0` | `(.25, 0, .5, .5)` | `49 x 291 + 204, 90` |
| Folded buff leather coat | `888, 0` | `(.5, 0, .75, .5)` | `269 x 253 + 101, 117` |
| Brass key | `1332, 0` | `(.75, 0, 1, .5)` | `113 x 246 + 163, 118` |
| Corked restorative bottle | `0, 444` | `(0, .5, .25, 1)` | `172 x 226 + 135, 99` |
| Tied bag of lead shot | `444, 444` | `(.25, .5, .5, 1)` | `204 x 238 + 128, 88` |
| Squat iron balance weight | `888, 444` | `(.5, .5, .75, 1)` | `145 x 204 + 155, 115` |
| Iron lever | `1332, 444` | `(.75, .5, 1, 1)` | `145 x 219 + 161, 102` |

The footprints are the non-transparent extent measured at 10% alpha, written
as `width x height + left, top` relative to the 444-pixel cell. Each has at
least 71 pixels of vertical clearance and 74 pixels of horizontal clearance;
the cells can therefore be sampled independently without adjacent-object
bleed. Use a one-pixel transparent inset when building mipmaps or texture
padding.

For UI, sample a full cell and display it at 72 to 128 pixels square; retain
the transparent margin rather than tightly trimming every icon. For world
billboards, begin with these authored physical sizes and tune them with pickup
reach and player-eye-height evidence: rifle `0.95 m` tall; knife `0.24 m`;
coat `0.55 m` wide; key `0.16 m` tall; bottle `0.20 m` tall; shot bag `0.28 m`
wide; balance weight `0.18 m` tall; lever `0.72 m` tall including its base.
Treat those as initial presentation values and move any adopted values into the
owning typed content definition rather than treating this note as runtime
tuning authority.

The set omits a door and pressure plate. The lever is a visible mechanism; a
door and pressure plate need their own world-readable art experiment after the
first interaction layout is seen in game.
