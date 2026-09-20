# Rifles gameplay design

Status: campaign #8356 foundation (task #8357). Planned migration, not landed
owners. Later tasks update their sections as they land; task descriptions carry
planning authority until then.

This is one concrete single-player real-time blobber. One party occupies one
cell and faces one cardinal direction. No reusable kit, ruleset framework,
event bus, or scripting layer.

## Characters

- Authored archetypes (`content/definitions/`, e.g. party/character-options)
  define identity, base stats/resources, loadout/spell/resistance references.
  Party presets reference archetypes; they do not copy stat blocks.
- Runtime instances are distinct characters with durable instance IDs and actual
  Engine `EntityId`s. Multiple instances of one archetype are supported with
  distinct IDs. Party formation/selected member is party state referencing
  characters, not a second character set.
- Landed (#8358): `Characters/CharacterEntities` owns one `EntityStore` per
  party scope with `AttachStats`/`ReplaceStats`/`Detach` and a durable
  instance-id map; `RiflesCharacter` facade exposes attached `StatsComponent`
  (shared maximum `Stat` references, `rifles.*` ids) plus formation state.
  Factories assemble from admitted definitions with archetype-derived
  `EntityTypeId` (`rifles:member:<archetype>`, `rifles:enemy:<id>`,
  `rifles:ally`); wrapping attaches nothing. Enemies share stats via the same
  owner (motion/brain stay concrete); allies are real characters from an
  explicit stand-in definition. Action/inventory/progression attachment is
  #8359–#8361; floor-lifetime consolidation is #8363.

## Stats, effects, inventory

- Canonical stat/track ownership is Engine `StatsComponent` with shared maximum
  `Stat` references and named accessors. Formulas/rounding stay Rifles-owned.
  No duplicated long-only mirrors or shadow scalars. (Planned — #8358.)
- `EffectsComponent` contributions apply to the same `StatsComponent` combat
  reads; equipment attach/removal preserves per-source identity. No per-tick
  aggregate rebuild when nothing changed. (Planned — #8359, #8361.)
- Engine `InventoryStore` / `InventoryComponent` / `EquipmentComponent` bind to
  actual character/party/container entities with typed owner/item references.
  One ledger; Rifles owns capacities, grid layout, equip requirements, atomic
  transfer/swap semantics. (Planned — #8359.)

## Actions and commands

- Concrete combat/action owners expose typed attack/cast/remedy operations with
  character-attached action state. Input, UI, and AI call the same named
  operations; the product root handles lifecycle and admitted update ordering.
  Windup → effect → recovery, interruption, ammo charging, target
  disappearance, and friendly fire are preserved. (Planned — #8360.)
- UI input is parsed once at the boundary into explicit domain operations.
  No global `commandRevision`/inventory-revision/target-revision freshness
  gates; actual entity/item/target state is inspected when acted on.
  Expected unavailable gameplay returns an availability/result reason, not an
  exception. Broad `catch (Exception)` never relabels programming failures as
  rejection. (Planned — #8362.)

## Floors and travel

- One concrete active-floor aggregate owns the built scene, grid, features,
  floor actors, and resource lifetimes, mounted directly — no temporary
  build/capture/dispose on startup or travel, no routine reads through save
  capture. Pure procgen stays Engine/file/clock-free. (Planned — #8363.)
- Travelling party/run state is separate from floor-owned state. Departing
  floors freeze as compact retained state; party characters/equipment/
  spellbooks travel once without duplication. Resources bind/dispose in
  reference/retirement order. No trusted-intent hash gates on owned floor data;
  procgen correctness and offline artifact hashes remain. (Planned — #8363.)

## Saves

- One current-state schema over Engine `JsonProductStateCodec` with
  source-generated DTO metadata and `StatsComponentCapture` rebuild. No version
  numbers, historical readers, duplicate nested codecs, or synthetic
  active-party validation of retained floors. Development saves may break.
  Malformed-current-save and unknown-definition errors stay understandable;
  native cleanup on failed activation remains. (Planned — #8364.)

## Content and trust

- Immutable content is admitted once at the owning domain's boundary into typed
  validated records (Engine content services). Missing/duplicate authored
  references are rejected there with actionable errors.
- Runtime defaults to trust: admitted definitions, attached live state, Engine
  services, current-schema saves. No SHA/hash re-admission, no
  propose/validate/mutate or proposal/accept/commit per action, no snapshots or
  rollback around ordinary gameplay, no compatibility fingerprints. Concrete
  gameplay checks remain: identity lifetime, capacity, reach, equipped-state,
  busy/cooldown, delayed-impact recheck, track bounds, valid content refs,
  coherent save relationships, native lifetime, genuine duplicate/dangling IDs.
  This is a single-player game; there is no cheating/MITM/adversarial-content
  threat model.

## Ownership map (current → target)

| Concern | Current | Target (#) |
| --- | --- | --- |
| Archetypes/party | `CharacterArchetypeDefinition` catalogue in `character-options.json`; presets hold instance slots (`PresetMemberDefinition`: instance id + archetype ref + display name + position); `ResolvePreset` builds `MemberDefinition` inputs carrying archetype ids; `PartyDefinition` keeps formation + capacity only | Landed (#8283). Spells/resistances key by archetype; books key by instance; rifle-drill stats converged to archetypes (formation/loadout identity kept) |
| Entities/stats | `RiflesCharacter` over `EntityStore` entities; `StatsComponent` with shared maxima; `CharacterEntities` durable map | Landed (#8358) |
| Inventory | `ItemInventory` numeric `PackOwner` + string prefixes over `InventoryStore` | Bound components + typed owners (#8359) |
| Combat | `RiflesProduct.Combat/Enemies/CombatPresentation` partials + dictionaries | Concrete combat/action owner (#8360) |
| Magic | `MagicState` string-keyed books/conditions; per-tick recompute | Attached spellbook/conditions via `EffectsComponent` (#8361) |
| Commands | Global revisions + exception eligibility | Typed operations + availability contract (#8362) |
| Floors | `FloorFactory` build/capture/dispose; `Capture()` reads | Active-floor aggregate; frozen retained floors (#8363) |
| Saves | `RunCodec` schema11 + unused `ExpeditionCodec` schema9; synthetic validation | One current codec + one reconstruction path (#8364) |

## Engine basis

Matched pair `0.1.0-dev.1b208e9b33aa` (SDK + `.runtime/pair-1b208e9b33aa/`).
Verified in the packaged surface: `EntityStore` + `EntityTypeId`, optional
`Actor` facade, class components, `StatsComponent`, `EffectsComponent`,
`InventoryStore`/`InventoryEdit` + `InventoryComponent`/`EquipmentComponent`,
`EffectStackingPolicy`, `ProductStateStore<T>` + `IProductStateCodec<T>` /
`JsonProductStateCodec<T>`, `StatsComponentCapture`. No Engine checkout
dependency; no downstream Rust/P/Invoke/unsafe. Exact pair lives in
`Rifles.Game.csproj` / `scripts/dev.sh` / Den — not normative prose.
