# Input investigation and generated art bundles

The reported failure was W/A/S/D and Q/E in Brave after clicking the view and
obtaining pointer lock. Restart still worked and enemies continued moving.
This distinguishes gameplay keyboard delivery from DOM command delivery and
simulation progress.

## Changes

The matched Engine SDK/runtime is now `0.1.0-dev.03ac310b95c2`. This includes
upstream bundle rendering, retained resource publication, and browser recovery
changes. Generated dungeon textures, world sprites and item sprites use the
`generated-art` ProductContent bundle through Engine content references. The
inventory's DOM atlas remains a packaged UI asset because content references
are not browser image URLs.

All six movement controls now declare both pressed and held mappings. Engine
retains press edges between admitted simulation steps; a held-only mapping can
miss a press followed by release before the next snapshot. Rifles still admits
at most one movement action per update and uses its existing action recovery;
it does not maintain a second held-key state or input transport.

## Investigation limits

Before the update, headless Chromium accepted a 30 ms E tap. A subsequent 600 ms
hold ended at the same facing; that capture alone cannot distinguish no turn
from a complete rotation.
The complete Brave failure was not reproduced by that observation. The previous
host diagnostics show repeated ready/degraded transitions and attachment
replacement while simulation progress continued. The preserved transition
extract is in `evidence/input-and-bundles/previous-host-transitions.json`.
Those transitions are a recovery clue, not proof of the cause of the user's
keyboard failure. The new pair and press mappings must not be represented as a
confirmed Brave-specific fix without a matching browser reproduction.

## Verification

`bash scripts/check.sh` passed: UI typecheck, solution build, game/procgen
checks, artifact tool self-check, and CoreCLR staging. The generated bundle
manifest contains 16 files (34,515,360 bytes). The matched runtime started
successfully through the existing broker on port 37300.

The independent Wolf GPU test confirmed Q/E turns, W/S movement, refocus, and
P pause/resume through visible state changes. Textured level surfaces and world
sprites rendered. A/D were attempted against corridor walls and were not
positively verified. See [the GPU report](evidence/input-and-bundles/gpu-report.md)
for original captures and session details. The updated host stayed ready during
this run; `updated-host-transitions.json` preserves its status records.
Brave and pointer-lock readback remain unverified by the Wolf test.
