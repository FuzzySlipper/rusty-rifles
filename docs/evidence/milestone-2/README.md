# Milestone 2 verification

Tested 2026-09-13 with SDK/runtime pair `0.1.0-dev.2e4255bd3ad5`,
CoreCLR product, seed 29 and the repository's authored art definitions.

`bash scripts/check.sh` passed: UI typecheck, Release solution build with zero
warnings/errors, procgen checks, game/snapshot/directional-art checks, offline
tool self-check and CoreCLR staging. All 15 selected PNGs pass the alpha/format
inspection recorded in `asset-admission.json`. Direction selection covers all
four actor facings and four relative viewer sides; saved dressing and matching
style definitions are checked separately from rendering.

## Original host captures

Images are unmodified captures, not mockups or resized evidence. `captures.json`
retains the capture IDs for the comparison and final placement images.

- `gpu-ink-room.png`: full-resolution GPU host view of repeated brick/slate,
  two distinct sentry instances, relative scale, cutout and depth.
- `gpu-room-fill-off.png`, `gpu-light-moved.png`: same paused scene after
  disabling product fill and moving its light. The host's neutral light rig
  remains. The old “Lantern only” label in these images was corrected to
  “Room fill off”; these are not proof of lantern-only lighting.
- `saved.png`, `restarted.png`, `restored.png`: browser save/restart/load sequence
  restores Seeker selection, paused 213-second expedition and actor placement
  after restart visibly resets to Warden and zero seconds. Script:
  `save-restart-load.js`, run `ecab6dcc-7b90-42fb-83ac-ece7ef77558b`.
- `painted-comparison.png`: successful live treatment swap on the same restored
  expedition. Front sentry, crate and surfaces have painted counterparts;
  remaining directional views, bench and lantern deliberately share ink art.
- `east-grounded.png`, `south-props.png`: native E key turns in final source;
  bench stays fixed while actors/props face the camera. Script: `turn-props.js`,
  run `703490d8-2a57-467e-9335-f0ca69f4270a`.
- `grounded-ensemble.png`: subsequent native S steps reveal all three grounded
  props, nearby/far surfaces and entrance-lantern targeting. This supersedes
  an earlier floating-lantern placement found during review.

Browser interaction also exercised forward/back/sidestep, turning, character
selection and pause. Save/load and treatment replacement published live world
art successfully without stale appearance errors. Browser observations had no
page errors; software WebGL reported ReadPixels performance warnings.

## Limits and disposition

Ink-and-wash remains provisional: it gives the complete directional set and
quieter shading. Painted front art is more highlighted. Final lighting judgment
is blocked on Engine #8256, linked to B05 #8203: the packaged host cannot yet
disable its default bright neutral world rig through product configuration.
Engine synthetic normals are used; no generated normal maps or local shaders.

The software browser renders a 320x180 backing canvas into 1280x720 under the
Engine software policy. Its captures establish interaction/placement, not
full-resolution texture quality. GPU captures resolve the art more clearly,
but that lane intermittently lost browser input transport; failed GPU
restart/load/style attempts were excluded. The successful browser sequence is
the save/load evidence. All test sessions were released; the dev server remains
available for owner inspection. Combat and detailed procgen are later work.
