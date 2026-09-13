# Rusty Rifles

A Rusty Engine game taking shape as a procedural, grid-based, first-person,
real-time party dungeon crawler: one party, one cell, four facing directions.

The [campaign strategy draft](docs/campaign-strategy.md) proposes the classic
blobber foundation, with ranged combat as a first-class system and early visual
experiments. [Reference-code maps](docs/references/README.md) provide focused
navigation of the local research games.

The bootstrap includes a generated walkable dungeon with an exit tile,
quarter-turn exploration, a four-member formation and vitality model, and
a DOM party readout. Engine owns rendering, navigation, camera, input, and
the simulation clock. Game policy lives in ordinary C#.

## Run

Requires .NET 10, Node/pnpm, and the matched Rusty Engine SDK/runtime artifacts.
The Engine pair is already installed locally in this checkout. On a fresh
checkout, install version `0.1.0-dev.2e4255bd3ad5` explicitly:

```bash
bash scripts/install-engine.sh /absolute/path/to/runtime-pack /absolute/path/to/Rusty.Engine.0.1.0-dev.2e4255bd3ad5.nupkg
pnpm install --frozen-lockfile
bash scripts/dev.sh --bind-host 0.0.0.0 --port 4420
```

Open the URL printed by the host. Click the game view to focus it.
W/S step forward/back; A/D sidestep; Q/E turn 90 degrees. Each key press
requests one action with a short recovery interval. The green floor tile
marks the exit. The initial seed is configured in `RiflesProduct.cs`.

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

The checks cover deterministic generation, validation and repair, artifact
round-trips/rejection, connected game floors, directional actions and recovery,
and party vitality/snapshot behavior. They also build and stage the normal
Engine CoreCLR product. The offline toolkit's richer locked/stateful motifs
are available for future level design; the game currently uses an open detour
graph and its resolved room/corridor grid.

This is a starting game repository. Combat, enemies, equipment/inventory,
spells, doors and interactions, campaign progression, persistent saves,
and finished art are not implemented. Camera steps currently snap between
grid poses. The party has a snapshot contract, but no save/load UI or store
is wired yet.
