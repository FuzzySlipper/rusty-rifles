# Rusty Rifles

Rusty Rifles is a procedural, grid-based, first-person, real-time party
dungeon crawler in the blobber tradition. One party occupies one cell and
faces one cardinal direction. Preserve that direction when extending the game.

## Gameplay development direction

First establish a coherent, conventional real-time blobber foundation using
games such as Dungeon Master, Eye of the Beholder, and Grimrock as behavioral
references. Once those basics are established, customize them toward Rifles'
specific gameplay. The owner-approved scope is in `docs/campaign-strategy.md`;
`docs/campaign/task-index.md` maps Den campaign #8187 and its implementation tasks.

Build this game directly. Do not introduce a reusable blobber kit, ruleset
framework, or abstraction layer for hypothetical games. Do not attempt a
complete port of an older game. Keep concrete domain owners loosely coupled
through small, explicit contracts so established behavior can be retuned or
replaced without rewriting unrelated systems.

## Art experiments

Start generated asset work from `docs/art-direction.md` and the editable
recipes in `content/art/prompts/`. Use the shared style plus one named
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

The installed development pair is SDK `0.1.0-dev.8c20a96d10ef` and
`.runtime/pair-8c20a96d10ef/runtime-pack`. Keep the package and runtime matched.
Use `scripts/install-engine.sh` to install external artifacts on another
checkout. `.runtime`, generated SDK composition, and UI output are ignored.
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
- The initial bootstrap still contains hard-coded starter values. As those
  domains are developed in the campaign, move their significant values into
  authored files instead of extending those hard-coded definitions.

## Work

Preserve existing edits. Keep pure procgen independent of Engine, UI, files,
and clocks; offline artifact I/O belongs in Artifacts/Tool. Generation traces
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

Den project ID: `rusty-rifles`; repository root: `/home/dev/rusty-rifles`.
Use that project for the implementation campaign and shared work; Den owns live
task status and dependencies, while the repo index records the initial plan.

Run `pnpm install --frozen-lockfile` once, then `bash scripts/check.sh` for the
solution build, UI typecheck, procgen/game checks, offline tool self-check,
and CoreCLR staging. Use focused checks during iteration.

`bash scripts/dev.sh --bind-host 0.0.0.0 --port 4420` launches a standalone
session. `.den-serve.json` describes the same lane for broker-owned serving.
Use an existing broker session instead of launching a competing host.

Report build/test, runtime launch, and visible interaction evidence separately.
Milestone 1 provides exploration, typed tuning, shared movement, feature focus,
party controls and Engine-backed saves; see `docs/milestone-1.md`. Milestone 2
adds generated art, lit sprites, texture mapping and comparison controls; see
`docs/milestone-2.md`. Milestone 3 adds party presets, equipment, inventory, world items and a gate puzzle;
see `docs/milestone-3.md`. Milestone 4 adds action phases, rifles, throws/bolts, mixed enemies and combat saves;
see `docs/milestone-4.md`. Milestone 5 adds mixed-size crowd slots, bounded Engine paths,
perception/search/patrol and rifle positioning; see `docs/milestone-5.md`. Richer procgen,
spells and multi-floor progression remain campaign work.
