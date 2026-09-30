# Rusty Rifles

Rusty Rifles is a procedural, first-person, real-time party dungeon crawler.
The party occupies one grid cell and faces one cardinal direction. Six soldiers
screen a commander; formation, weapon reach, reloads, bayonets, charges and shared
abilities make the party's positioning matter.

Explore a generated multi-floor expedition, manage shared supplies and equipment,
solve item and weight puzzles, fight mixed-size enemies, and save the whole run.
The current art treatment is a prototype: illustrated billboard stills over
textured voxel architecture with dynamic lights.

## Run

Install .NET 10, Node and pnpm, then install the Engine CLI using its published
installer:

```sh
curl -fsSL https://raw.githubusercontent.com/FuzzySlipper/rusty-engine/main/scripts/install-rusty.sh | bash
pnpm install --frozen-lockfile
rusty install
rusty dev --project src/Rifles.Game/Rifles.Game.csproj --live-debug --bind-host 0.0.0.0 --port 4420
```

Use an existing broker-owned session when one is running. The SDK/runtime pair
is pinned only in `Directory.Build.props`; `rusty status` reports it and
`rusty update` moves it. No adjacent Engine checkout is required.
The normal loader is CoreCLR. NativeAOT is an explicit release check.

Press P to begin. W/S step, A/D sidestep, Q/E turn, F uses the focused feature,
and T cycles it. Space fires, V orders melee, R reloads, B/N fix/unfix bayonets,
and C charges. K/L save/load. Menu opens inventory, formation, expedition settings,
rest and a field guide. The commander falling ends the run even if soldiers live.

## Source and checks

- `src/Rifles.Game`: C# product rules, domains and Engine integration.
- `src/Rifles.Procgen`: pure graph intent and bounded physical floor generation.
- `src/ui`: a DOM projection and input companion using the packaged SDK types.
- `tests/Game` and `tests/Procgen`: grouped product checks.
- `content/`: runtime definitions, tuning, art and audio.

```sh
bash scripts/check.sh
```

This builds both source and both check projects, typechecks the UI, runs the
checks and stages CoreCLR. Independent failures are collected by group and lane.
A failed build skips stale check executables. CI also deletes generated UI and
verifies that `rusty build` reconstructs it.

Export the current floor bank with the real product definitions:

```sh
dotnet run --project tests/Game -c Release -- --export-floors /tmp/rifles-floors
python3 scripts/render-floor-plan.py /tmp/rifles-floors/29-arrival.json /tmp/arrival.svg
python3 scripts/generate-audio.py
```

The first command exports the four-seed, three-floor bank while running checks.
The last regenerates the deterministic audio assets from `docs/audio/recipe.json`.
The committed floor bank is a labelled data snapshot, not current visible proof.

See [gameplay and ownership](docs/gameplay-design.md), [Engine integration](docs/engine-integration-notes.md),
[debug tools](docs/debug-tools.md) and [art direction](docs/art-direction.md).
Den project `rusty-rifles` owns task status, reviews and acceptance evidence;
repository documents describe current behavior. Historical milestone records are
available as `historical-milestone-N` Den documents and in Git history.
