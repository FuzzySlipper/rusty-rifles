# Rusty Rifles

Rusty Rifles is a procedural, grid-based, first-person, real-time party
dungeon crawler in the blobber tradition. One party occupies one cell and
faces one cardinal direction. Preserve that direction when extending the game.

## Gameplay development direction

Preserve a coherent, conventional real-time blobber foundation, using games
such as Dungeon Master, Eye of the Beholder, and Grimrock as behavioral
references, and build Rifles' specific gameplay on it.
`docs/martial-command-design.md` owns the party, combat targeting, equipment,
ammunition and formation contracts. `docs/gameplay-design.md` describes current
behavior and owning code. Den owns implementation plans, task ordering, acceptance
and follow-up status.

Build this game directly. Do not introduce a reusable blobber kit, ruleset
framework, or abstraction layer for hypothetical games. Do not attempt a
complete port of an older game. Keep concrete domain owners loosely coupled
through small, explicit contracts so established behavior can be retuned or
replaced without rewriting unrelated systems.

## Art experiments

Start generated asset work from `docs/art-direction.md` and the editable
recipes in `docs/art/prompts/`. Use the shared style plus one named
treatment and an asset-specific contract; make deviations explicit experiments
instead of inventing an unrelated style for each image call. The provisional
default is ink and wash, with painted cover as a comparison, not a final style
decision. Generate actual art early in small coherent sets and judge it in-game.

The direction is retro book/game-cover illustration, not pixel art: textured
voxel levels, billboard clutter/interactables, simple directional enemy stills,
and dynamic level lighting on sprites. Do not require generated sprite normal
maps; verify simple Engine shading approaches first. Supplied concept briefs
and reference images are inspiration, not mandatory setting or gameplay rules.

## Ownership

- C# owns dungeon intent, rules, party formation, encounters, progression,
  action timing policy, and save meaning.
- Rusty Engine owns lifecycle and admitted simulation time, input delivery,
  rendering, camera, spatial/navigation mechanisms, resources, and persistence
  primitives. Use the safe packaged SDK.
- TypeScript is a DOM companion observing Engine projections. Do not add
  a browser game loop, renderer, gameplay authority, or custom transport.
- Keep the product root small; organize code by game domain. No downstream
  Rust, handwritten P/Invoke, unsafe product code, or Engine source dependency.
- If a needed Engine mechanism is missing, identify the owning upstream gap;
  do not disguise it with a local replacement.

## Code and dependencies

All source in this repository belongs to Rifles. Imported code is a one-time
transfer: do not maintain donor links, source-provenance documents, sync
scripts, parity obligations, or sibling project references.

The Engine pair is pinned once, by `RustyEnginePackageVersion` in
`Directory.Build.props`. The Engine `rusty` command installs (`rusty install`),
runs (`rusty dev`), reports (`rusty status`) and moves (`rusty update`) it.
`.runtime`, generated SDK output, and UI output are ignored.
See `docs/gameplay-design.md` for the gameplay ownership map and Engine basis.
`rusty dev` is the normal CoreCLR loader; NativeAOT is an explicit release
check only when requested.

## C# style and tuning

- Prefer simple, expressive, readable C# over clever tricks, dense expressions,
  implicit side effects, and speculative abstractions. Make intent and control
  flow obvious; use descriptive names and small cohesive methods.
- Avoid magic numbers and hard-coded significant gameplay choices. Put tunable
  values and authored definitions in versioned product files under `content/`
  (for example, `content/tuning/` and `content/definitions/`). This includes
  timing, movement, camera settings, generation parameters, party/member
  definitions, encounters, abilities, items, and balance values.
- Load files through Engine content services into typed, validated C# records
  at the owning domain's boundary. Pass those definitions explicitly to their
  consumers. Keep runtime behavior typed; do not scatter file reads, string
  lookups, or an untyped global configuration dictionary through game logic.
- Use named constants for genuine mathematical, format, or protocol invariants.
  Moving a balance value from a literal into a C# constant is not sufficient
  tuning support. Avoid silently substituting hard-coded defaults for missing
  or invalid authored configuration; report actionable validation errors.
- Keep each domain's configuration local to its purpose. Add abstraction only
  when a concrete game need justifies it; file-driven tuning does not require
  a generic rules engine, scripting language, or plugin system.
- When extending a domain that still has hard-coded starter values, move its
  significant choices into authored files rather than extending those literals.

## Work

Preserve existing edits. Keep pure procgen independent of Engine, UI, files,
and clocks; offline floor-export I/O belongs in the Game checks. Generation traces
describe generated content, not source-code ancestry.

Commit and push completed work by default so changes are backed up and the Git
history records when work was done. The owner has authorized including existing
changes in this repository's backups. Inspect the complete change set, preserve
it, and include all intended source/content changes; exclude secrets, local
configuration, dependencies, and generated output through sensible ignore rules.
Use concise descriptive commit messages and the normal current branch/upstream;
do not add a PR workflow or ask for commit/push permission again. If pushing
fails, retain the local commit and report the exact blocker. Never force-push
or discard work to resolve a push failure.

Den project ID: `rusty-rifles`; repository root: the current checkout.
Use that project for implementation plans and shared work. Den owns task status,
dependencies, campaign indexes and progress records.

Install dependencies with `pnpm install --frozen-lockfile` when needed. Use
focused checks during iteration. Run `bash scripts/check.sh` for changes that
affect integrated game behavior, shared contracts, or staging, or when the task
requires it; it covers the solution build, UI typecheck, procgen/game checks,
and CoreCLR staging. Documentation-only and other
isolated changes need checks relevant to their changed surface. Once relevant
checks pass, rerun or broaden them only for material changes, failures, or
unresolved concerns.

`rusty dev --project src/Rifles.Game/Rifles.Game.csproj --live-debug --bind-host 0.0.0.0 --port 4420` launches a standalone
session. `.den-serve.json` describes the same lane for broker-owned serving.
Use an existing broker session instead of launching a competing host.

Report build/test, runtime launch, and visible interaction evidence separately.
`docs/gameplay-design.md` is the current behavior and ownership map.
`docs/debug-tools.md` describes the packaged inspection controls.

## Documents and evidence

Local Markdown documents in this repository are for settled, durably useful
information: current behavior, ownership, contracts, authoring recipes and
repeatable commands. Put mostly historical context, milestones, implementation
plans, progress tracking, dated decisions, investigations, reviews, measurements
and other temporal or ephemeral records in Den documents or Board posts. Do not
keep local tracking copies or placeholder pages for records moved to Den.

Preserve historical material in Den before removing it locally. Record original
capture and sidecar paths there; keep acceptance screenshots and raw test
transcripts out of the repository. The floor bank under `docs/generated/` is an
explicitly labelled generated data snapshot, not acceptance proof. Preserve Git
history. Avoid volatile Engine versions in prose: the only pair pin belongs in
`Directory.Build.props`.

`content/` holds admitted runtime content. Editable art recipes, references and
unapproved mesh/style experiments belong under `docs/art/`. Preserve experiment
bytes and provenance when relocating them; in-game evaluation decides adoption.
