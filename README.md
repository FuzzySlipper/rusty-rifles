# Rusty Rifles

A Rusty Engine game taking shape as a procedural, grid-based, first-person,
real-time party dungeon crawler: one party, one cell, four facing directions.

The [campaign strategy](docs/campaign-strategy.md) defines the classic
blobber foundation, with ranged combat as a first-class system and early visual
experiments. The [task campaign](docs/campaign/task-index.md) maps all 63 Den
implementation tasks and their dependencies. [Reference-code maps](docs/references/README.md) provide focused
navigation of the local research games.
The [working art direction](docs/art-direction.md) and
[image prompt recipes](content/art/prompts/README.md) keep generated assets
coherent while exploring ink/wash and painted-cover treatments.

The playable foundation includes a generated walkable dungeon, quarter-turn
exploration, four-member party controls, reachable world interactions and
Engine-backed saves. [Milestone 2](docs/milestone-2.md) adds generated textured
voxels, grounded prop sprites, directional sentries and dynamic-light art
comparisons. [Milestone 3](docs/milestone-3.md) adds starter-party presets,
equipment and inventory, restorative items, world storage and a key/lever/plate
gate puzzle. [Milestone 4](docs/milestone-4.md) adds real-time rifle/melee combat,
throws, bolts and enemy loot. [Milestone 5](docs/milestone-5.md) adds mixed-size
crowds, sight/noise awareness, patrol/search and tactical rifle enemies. Engine owns rendering, navigation, camera, input, inventory and
the simulation clock. Game policy lives in ordinary C#.
[Milestone 6](docs/milestone-6.md) adds spells, conditions, rest and advancement;
[Milestone 7](docs/milestone-7.md) composes height-aware floors, encounters,
item puzzles and route supplies. [Milestone 8](docs/milestone-8.md) connects
three retained floors with whole-run saves, discovered maps, objectives,
defeat/retry and file-authored difficulty profiles.
[Milestone 9](docs/milestone-9.md) refines the separate inventory panel, stable
party controls, target readability, bundled sound cues and resource reuse.
Its evidence distinguishes source checks, visible play and owner-browser tests.

## Run

Requires .NET 10, Node/pnpm, and the matched Rusty Engine SDK/runtime artifacts.
The Engine pair is already installed locally in this checkout. On a fresh
checkout, install version `0.1.0-dev.03ac310b95c2` explicitly:

```bash
bash scripts/install-engine.sh /absolute/path/to/runtime-pack /absolute/path/to/Rusty.Engine.0.1.0-dev.03ac310b95c2.nupkg
pnpm install --frozen-lockfile
bash scripts/dev.sh --bind-host 0.0.0.0 --port 4420
```

Open the URL printed by the host. Click the game view to focus it.
W/S step forward/back; A/D sidestep; Q/E turn 90 degrees. Each key press
requests one action with a short recovery interval. Select a member and a visible
enemy, reload with T, and attack with Space; rifles start empty. Use the separate
inventory panel to transfer or equip items by drag or by select/quantity/click.
Escape cancels a pending drag. P pauses explicitly; opening inventory does not.
K saves and L loads. The expedition panel holds travel, the discovered map,
retry and new-run controls. The green floor tile
marks the exit. The initial seed is configured in `content/tuning/generation.json`.

For broker-owned serving, `.den-serve.json` uses the same development command:

```bash
den-serve up rusty-rifles -repo /absolute/path/to/rusty-rifles
```

No sibling source checkout is needed to build or run. Engine binaries, NuGet
artifacts, node modules, generated UI, and SDK composition stay untracked.

## Layout

| Location | Responsibility |
| --- | --- |
| `src/Rifles.Game` | Product entry, grid exploration, party state, Engine scene and UI projection |
| `src/Rifles.Procgen` | Pure graph construction, room/catalog placement, routing, validation, scoring and repair |
| `src/Rifles.Procgen/Workbench` | Stateful dungeon motifs, route/separation reasoning, repair and candidate experiments |
| `src/Rifles.Procgen.Artifacts` | Strict artifact codecs and atomic offline output |
| `src/Rifles.Procgen.Tool` | Offline generation, candidate inspection, trial banks and repair commands |
| `src/ui` | Thin TypeScript DOM companion |
| `content` | Authored game assets |
| `tests` | Procgen and game behavior checks |

All imported source is local Rifles code, ready to customize independently.

## Check and experiment

```bash
bash scripts/check.sh

dotnet run --project src/Rifles.Procgen.Tool -c Release -- generate-workbench \
  --seed 29 --motif branching-complex \
  --out artifacts/complex-29.json --receipt artifacts/complex-29.receipt.json
```

The checks cover deterministic generation and repair, artifact rejection,
movement and occupancy, combat and spell settlement, inventory ownership,
complete expedition saves and generated route preservation. They also build
and stage the normal Engine CoreCLR product. The offline toolkit can export
floor inspection artifacts; the game composes the authored expedition's floor
roles, routes, mechanisms, encounters and supplies from the same definitions.
