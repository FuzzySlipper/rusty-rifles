# Milestone 9 — foundation closeout

The last campaign stage refines the integrated game rather than adding a game
kit or replacing the underlying rules. Den tasks: I01 #8246, I02 #8247,
I03 #8248, I04 #8249, I05 #8250.

## Changes

Inventory has its own lower-left panel. Opening it keeps the member selection,
health and primary controls at their normal width. Health updates preserve
member buttons and formation selections. A native drag retains its DOM source
until drop or cancellation; captured revisions still let C# reject stale
ownership and reach. Click transfers use the same authoritative transaction.
Out-of-sight targets disable attack/throw buttons, and target tooltips identify
cell/crowd position. AI navigation diagnostics no longer dominate enemy labels.

Five short procedural sound cues cover rifle fire, reload, spells, impacts and
item interactions. Engine opens retained clips from the `game-audio` content
bundle and owns transient playback. Emission occurs at committed actions;
restoring a snapshot does not replay a combat log. The recipe is editable in
`content/audio/recipe.json`, rebuilt with `python3 scripts/generate-audio.py`;
playback mix is in `content/definitions/audio.json`. These are prototype sounds,
not a final acoustic treatment. `rifles.audio.read` exposes Engine admission
and realization diagnostics without emitting sound.

The pressure plate uses a darker authored color to distinguish it from the
floor. It remains a simple untextured mechanism marker. Textured voxels and
lit directional stills retain the provisional ink-and-wash/painted treatments.

Save admission now checks the combined patrol/dressing obstruction layout and
rejects personal gates placed on protected entrances, exits, grants, connectors
or other mechanisms. The 12-floor regression bank combines actual static
placement, hazards, gates and mixed-size encounter exclusions. Existing reload,
projectile, cast, reward and retained-floor checks remain in the normal suite.

## Verification

`bash scripts/check.sh` passes: UI typecheck, solution build, procgen/game
checks, offline tool self-check and CoreCLR staging. The build uses SDK/runtime
`0.1.0-dev.03ac310b95c2`. Evidence and the remaining runtime acceptance are
recorded in `docs/evidence/milestone-9/`.

A live click transfer moved two rounds from Warden's eight to Blade's pack,
leaving six and two respectively. The separate inventory panel was observed
in the managed Chromium browser. That backend does not expose a held-pointer
drag; click evidence alone does not prove native drag acceptance.

## Performance scope

Measurements must distinguish CPU generation, Engine/C# updates and remote
renderer submissions. The generation bank deliberately runs candidate and
resolved generation separately; its total is not one product floor's cost.
GPU timer values unavailable in Firefox must not be represented as zero-cost
GPU rendering. See the evidence report for measured budgets and hardware.


Dungeon material catalogs are now owned for the product lifetime and reused by
art treatment and voxel size. Floor activation and preview construction borrow
them; scene teardown releases scene owners, and product shutdown releases the
catalogs before their source art. Identical complete HUD snapshots are skipped,
and the elapsed time is projected at its displayed whole-second precision.
Engine still retains the last complete projection for browser attachment.

The owner confirmed native inventory transfers and responsive movement/clicks
with the full HUD. Browser-local measurements separate DOM callback cost from
Engine asset admission: steady DOM p95 0.6 ms and delivery p95 119 ms, while an
initial resource-response callback took about eight seconds. See
[evidence](evidence/milestone-9/browser-ui-timing.md); the latter remains an
Engine startup/recovery cost, not a claimed downstream fix.
