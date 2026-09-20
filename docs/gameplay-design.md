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
  #8359–#8361; floor-lifetime consolidation landed in #8363.

## Stats, effects, inventory

- Canonical stat/track ownership is Engine `StatsComponent` with shared maximum
  `Stat` references and named accessors. Formulas/rounding stay Rifles-owned.
  No duplicated long-only mirrors or shadow scalars. (Landed — #8358.)
- Landed (#8359, #8361): `EffectsComponent` contributions apply to the same
  `StatsComponent` combat reads; equipment, advancement, and condition
  attach/removal preserve per-source identity. No per-tick aggregate rebuild
  when nothing changed. Spellbooks attach to character entities (archetypes
  without starting spells carry none); rest travels with the party, outside
  the magic snapshot. An entity-lifetime marker keeps restores onto live
  entities from duplicating effect instances or stat modifiers.
- Landed (#8359): every ledger owner binds an entity facade — members on
  character entities, the party pack on a `rifles:party` entity, anchors and
  flights on container entities, enemy packs on their live enemy entity
  (`BindMembers` + `BindRemaining`). Mutations take typed `ItemRef` /
  `InventoryOwner` references; interior prefix dispatch is gone (UI tokens and
  ledger reads stay strings at the edge). Equipment contributions attach per
  source on the shared `StatsComponent` (aggregate push retired; live-stat
  equip policy). One ledger; Rifles owns capacities, grid layout, equip
  requirements, atomic transfer/swap semantics. Travelling packs restore
  by identity into each floor's world (#8363).

## Actions and commands

- Landed (#8360): `RiflesCombat` owns member action states (attached to
  character entities, no dictionary), live enemy/flight/drop/loaded state
  (`FlightState` mutates in place; snapshots freeze DTOs at save), and the
  named attack/cast/remedy operations input, UI, and AI call. The product
  root keeps lifecycle, update ordering, and defeat aftermath; a `CombatScope`
  carries floor services plus explicit message/sound/rest/feature callables.
  Windup → effect → recovery, interruption, ammo charging, impact rechecks,
  and friendly fire are preserved. Allies share stats but need no action
  machinery; their authored source stays deferred.
- Landed (#8362): UI input parses once at the boundary (shared options)
  into named domain operations returning `GameOutcome` reasons. No global
  `commandRevision` or UI-passed inventory-revision gates; ledger atomicity
  and feature impact revisions still recheck genuinely at act time. Input
  catches only `InvalidDataException`; programming failures propagate.
  Rendered spell availability is the execution check (`SpellAvailability`).
  UI payloads carry no revisions except feature impact revisions.

## Floors and travel

- Landed (#8363): one concrete active-floor aggregate owns the built scene,
  grid, features, floor actors, and resource lifetimes, mounted directly —
  no temporary build/capture/dispose on startup or travel, no routine reads
  through save capture (live getters for dressing/door/lever/exit). Pure
  procgen stays Engine/file/clock-free.
- Landed (#8363): travelling party/run state is separate from floor-owned
  state. Departing floors freeze as compact retained state at the transition;
  the party object, books, and entity markers travel once (floor-local keys
  detach, facades rebind); packs and condition timing restore by identity.
  Retained returns validate the thawed merge and adopt live party/books.
  Resources bind/dispose in reference/retirement order. The intent graph
  re-hash on owned floor data is gone (identity plus seed suffice);
  procgen correctness and offline artifact hashes remain.

## Saves

- Landed (#8364): one current-state schema over Engine `JsonProductStateCodec`
  with source-generated DTO metadata (`RunSaveJsonContext`, string enums).
  No version numbers, historical readers, or duplicate nested codecs;
  retained floors validate as floors, never as synthetic active expeditions.
  Stats persist modifier-free via `StatsComponentCapture` and rebuild with
  authored bases intact; equipment/development/conditions re-apply through
  their owning flows. Experience and rewards are trusted as coherent current
  values, never re-derived from kill history. Development saves may break.
  Malformed-current-save and unknown-definition errors stay understandable;
  native cleanup on failed activation remains.

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
| Inventory | `ItemInventory` numeric `PackOwner` ledger + bound components + typed owners | Landed (#8359); ledger re-key follows #8363 |
| Combat | `RiflesProduct` combat partials + action dictionaries | Landed `RiflesCombat` owner (#8360) |
| Magic | `MagicState` detached books/conditions; per-tick recompute | Landed attached books/conditions + per-source stats (#8361) |
| Commands | Global revisions + exception eligibility | Landed outcomes + shared availability (#8362) |
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
