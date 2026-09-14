# Rusty Rifles M9 restart observation

Session `ce475e4b-af97-44de-b356-54ebe83bb7bc` (`rusty-rifles`, browser
backend, slot-1) completed three ordinary Restart cycles. Each cycle was
allowed to settle, paused through the visible Pause control, and captured with
the metrics panel open.

## Original captures

1. [First settled restart](/home/agent/.local/state/crew-playtest/browser/ce475e4b-af97-44de-b356-54ebe83bb7bc/de4082df-987c-45d1-ad55-ff6e9351d36a.png)
2. [Second settled restart](/home/agent/.local/state/crew-playtest/browser/ce475e4b-af97-44de-b356-54ebe83bb7bc/81877887-0cda-4bfd-9590-f8ab859b84f2.png)
3. [Third settled restart](/home/agent/.local/state/crew-playtest/browser/ce475e4b-af97-44de-b356-54ebe83bb7bc/9fbdde05-8b08-4ff7-9c82-a8884833738d.png)

The renderer readout stayed constant across all three captures:

```text
Renderer: software | ANGLE (Google, Vulkan 1.3.0 (SwiftShader Device (Subzero)
(0x0000C0DE)) | SwiftShader driver | Google Inc. (Google)
Canvas: 320x180 px | CSS 1280x720 | DPR 0.3
Draws: 21 | triangles: 696 | live handles: 33
Live resources: geometry 26, material 23, texture 40
Defined textures: 40 | fallbacks: sprite 0, material 0
```

Submission pacing varied between the captures: 12.5 Hz / 66.60 ms / 0.30 ms,
12.0 Hz / 83.30 ms / 0.30 ms, and 10.0 Hz / 116.70 ms / 0.40 ms for
submission rate / last interval / sync submit. GPU timer remained unavailable.

## Neutral visual assessment

All three frames show the same Guard Hall spawn view: a warm tan ceiling,
red-orange brick wall with clear mortar lines, a muted tiled floor, and the
billboard figures standing in the room. The top-left party/combat HUD and the
right-side expedition panel remain legible; the collapsed “Inventory &
equipment” strip remains distinct at lower left. The metrics panel occupies
the upper-right over part of the expedition panel when open.

At the displayed scene, the visible figures appear to meet the floor
plane and no conspicuous floating is visible. The center and lower-left
figures have readable silhouettes, but the wall-adjacent sprite and the
smaller figures are visibly softened by the 320x180 backing resolution. This
capture cannot support a strong alpha-edge or grounding judgment. No obvious
transparent billboard rectangle is visible at this resolution. The warm
lighting separates the figures from the brick wall, though crowd readability
is reduced by the coarse rasterization.

The browser evidence is software SwiftShader at a 320x180 backing canvas
despite a 1280x720 CSS viewport. These captures therefore establish stable
resource counts and visible layout only; they do not establish accelerated
GPU rendering or a representative GPU frame rate.
