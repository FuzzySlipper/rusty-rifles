# Classic blobber foundation campaign

**Discussion draft — 2026-09-13.** This proposes the campaign for `rusty-rifles`;
no Den tasks have been created. Review the gameplay and visual direction first,
then turn the agreed work packages into dependency-ordered tasks.

## Destination

Build a small, complete, replayable dungeon expedition: create or select a
four-person party, explore several generated floors, fight in real time,
manage equipment and supplies, cast spells, solve item-based obstacles, and
reach an ending. Save, quit, and resume the actual expedition. It should be
pleasant enough to replay with another seed and structurally ready for Rifles'
more specific gameplay.

Classic blobbers supply a shared vocabulary, not a fidelity target. Rifles is
an early-modern world whose player combat will commonly use gunpowder rifles.
**Ranged combat is a foundation, not an optional layer over melee.** The first
combat milestone includes ammunition, reload/recovery timing, line of fire,
and a ranged opponent. Detailed historical weapon simulation can wait.

A useful initial content budget is **three floors, six to eight enemy archetypes,
eight to twelve spells, and twelve to twenty reusable room/puzzle motifs**.
These are proposed planning targets, not engine limits or settled balance.
Depth comes from combinations of reliable systems, not a large asset count.

## Starting point and durable boundaries

The current repo has one generated open dungeon, grid stepping/turning, Engine
voxel/navigation presentation, four party vitality tracks, and a readout.
It also has substantial graph/layout/routing, stateful motif, repair, and
artifact tooling. Inventory, combat, AI, spells, texture/sprite presentation,
and complete saves are still campaign work.

Build concrete C# domains in this product: expedition, party, inventory,
exploration, combat, enemies, spells, interactions, and level generation.
Use small typed calls between owners; avoid a universal event bus, rules DSL,
module framework, or reusable blobber kit. No complete old-game port.

- **Engine mechanisms:** admitted time/input, rendering and resources, camera,
  spatial queries/navigation, visibility, content, persistence, and available
  mechanics helpers. Reuse these instead of rebuilding them downstream.
- **Game decisions:** legal actions, occupancy/formation rules, combat formulas,
  tactical AI, item meaning, progression, dungeon intent, and authored content.
- **File tuning:** typed, validated definitions under `content/`. Movement,
  action phases, size footprints, material assignments, item/spell/enemy data,
  party defaults, and generator budgets must be editable without recompiling
  behavior. Load through Engine content; pass definitions to their owners.
- **Durable state:** stable actor/item/feature IDs, resolved floor artifacts,
  and explicit snapshots. Extend these as features land. Reloading a save must
  not regenerate different rooms, reroll loot, or replay already-applied hits.
- **UI:** DOM inventory and controls send supported Engine-admitted commands and
  render C# projections. Pointer state and drag previews are UI-only; item
  ownership and action validity remain in C#.

The packaged SDK already exposes planar/weighted navigation, traversal overlays,
ray queries, directional voxel materials, atlas sprites, content reads,
persistence, and managed mechanics. Exact behavior still needs focused early
integration checks; a method name is not proof of a complete gameplay contract.
See [Engine integration notes](engine-integration-notes.md). If a needed
mechanism is absent, identify the narrow Engine-owned dependency before
scheduling its consumer. Do not create a local pathfinder or renderer to hide it.

## Gameplay contracts to establish early

### One shared grid, real time, and readable movement

The party and enemies use the same logical cells, edges, floor transitions,
and door states. Turning, movement, attacks, reloads, spells, and use actions
advance from Engine simulation facts with authored durations and interruption
rules. No turn settlement masquerading as real time and no browser clock.

A committed action has explicit start, effect/commit, and recovery semantics.
A held movement key can repeat predictably; bounded input buffering must not
queue a long unintended walk. Presentation may interpolate translation and
turns, but rendered positions never become another movement authority.
Define which cell/edge is occupied during transit, when a moving target can be
hit, and how death, interruption, floor changes, and pause release reservations.

### Enemies sharing cells

Proposed baseline: each cell has authored formation positions and a capacity
budget. A small enemy might consume one unit, a medium enemy two, and a large
enemy the entire four-unit cell. These are example data values. **Capacity is
necessary but not sufficient:** a valid footprint must fit the available
positions and entrance edge without overlapping another actor.

Compatible allied enemies may share a cell; hostile actors cannot overlap the
party or each other. Sharing eligibility is explicit policy, not an accidental
same-species restriction inherited from a reference. Give each enemy an
individual identity, health, action state, and stable within-cell placement. Reserve destination capacity and any contested edge
before movement; settle competing requests in a stable order. Reject blocked
moves without shuffling actors randomly. Repacking after a death or departure
uses smooth presentation toward valid placements, not free movement between
logical cells. Initial large enemies fit one cell; multi-cell giants are a
separate extension, not required to prove size-dependent stacking.

Path queries must account for actor size, doors, route permissions, and current
occupancy through Engine-supported mechanisms. Product admission revalidates
the next step. Specify waiting, alternate routes, and bounded replanning so
crowds do not oscillate or permanently deadlock a corridor. Seek reachable melee contact positions or
valid firing positions, not a path into the occupied party cell. Validate narrow
passages, opposing traffic, swaps, partial groups, and large/small mixtures.
Attacks and selection target the visible individual; melee reach and firing
lanes use its footprint rather than pretending a group is one giant target.

### Ranged, melee, spells, and combat feedback

Use a common action/damage/effect path for player weapons, enemy attacks,
spells, and thrown items, without turning it into a generic rules language.
Weapons select delivery and authored policy: melee reach, direct shot, or
travelling projectile. Direct shots need not simulate fast physical bullets;
travelling bolts and thrown objects must have meaningful travel and impact.

Resolve target eligibility, line of fire, range, intervening actors and
features, hit/defense, damage, statuses, and death consistently. Make friendly
fire/body-blocking policy explicit and data-tunable. Walls and closed doors
must actually block shots; transparent UI selection cannot bypass occlusion.
Rear party members can contribute with ranged attacks and spells; melee reach
is an authored weapon/formation rule. A miss, blocked shot, dry weapon,
interrupted reload, and successful hit must
be distinguishable. Combat includes reload opportunities, mixed melee/ranged
pressure, retreat, and enemy windups, not only damage-per-second trading.

### Party, inventory, and items in the world

Start with classic per-character slot inventories and equipment positions,
not a spatial packing minigame. Support identity-bearing items and fungible
stacks, stack split/merge, capacity, equipment requirements, ammunition,
consumables, comparison, and transfers between characters and containers.
Character handling includes roster/formation changes, statistics and derived
values, injury/status, death and recovery, and a small advancement choice.

Dragging is a proposed transfer, not an immediate removal. Keep the item at
its committed owner until C# accepts the destination. Handle cancellation,
stale targets, full containers, incompatible slots, split amounts, and changes
while the world continues running. Provide a click/select alternative using
the same commands. Never duplicate or lose an item on a failed drop.

Pickup, drop, throw, equip, consume, load ammunition, and use-on-target share
item identity and transfer rules. World placements have reachable anchors:
floor quadrants, shelves/alcoves, sockets, and containers. Include keys/locks,
weighted plates, levers, doors, traps, and thrown objects activating a feature.
Validate interaction distance, visibility, target state, and item requirements
at commit time. Required items must not be consumable into an unrecoverable
progression dead end without an intentional recovery route.

## Detailed generation: intent to a playable place

Extend the existing pipeline rather than inventing another generator:

1. **Expedition intent:** floor roles, main route, loops, branches, shortcuts,
   optional rewards, safe spaces, resource pressure, and inter-floor links.
2. **Room and corridor grammar:** varied footprints and proportions, junctions,
   dead ends with purpose, sightline lengths, ranged arenas, close ambushes,
   thresholds, alcoves, and landmarks. Avoid a graph of identical square rooms.
3. **Stateful motifs:** locks/keys, weighted mechanisms, timed hazards, secret
   walls, recoverable detours, and puzzles with clear clues. Compose bounded
   motifs with prerequisites and effects, not arbitrary unsolved random logic.
4. **Grid realization:** preserve intended connections and protected barriers;
   place doors, pits/bridges, stairs, collision and navigation from the same
   resolved plan. Start with stacked planar floors and explicit connectors.
5. **Voxel detailing:** walls/floors/ceilings, trim, recesses, supports, damage,
   material regions, and light fixtures. Separate logical cell size from voxel
   detail resolution. Detail must not accidentally close a route, create a
   shortcut, or obscure an interaction. Destructible terrain is not required.
6. **Population:** place encounters by space/size capacity and tactical role;
   allocate loot, ammo, healing, spell resources, clues, and useful world items
   with a budget informed by the intended route and difficulty.
7. **Validation and repair:** graph solvability plus traversable realized
   geometry, real gate separation, item accessibility/recovery, size-compatible
   enemy routes, spawn safety, and viable resource budgets. Check incidental
   corridor crossings and decorative gaps, not just declared route tags.
8. **Inspect and replay:** preserve accepted resolved artifacts; expose why a
   candidate was rejected and allow bounded repair/regeneration. Keep a small
   regression seed bank and export useful failures for focused investigation.

The current built-flow validator checks declared graph/route relationships;
it does not establish that every intended lock is physically enforced by the
running game. Stateful motif analysis is a useful starting point, not a
certificate that arbitrary composed levels cannot be bypassed or softlocked.

## Early visual exploration using inexpensive assets

The upcoming visual discussion should choose a small visual brief before mass
asset generation. Then make **one representative room and one mixed enemy
encounter** using the actual generator, item placements, lighting, and combat
systems. Promote useful authored content directly into the expedition.

Use GPT image generation for a small coherent texture set and transparent,
non-animated enemy sprites. Define tile scale, repeat/seam treatment, palette,
lighting assumptions, silhouette, ground anchor, apparent size, alpha edges,
and import settings. Asset definitions control material/atlas assignments;
changing a look must not require changing gameplay code.

World textures are applied to Engine-rendered voxel geometry. Enemies are
Engine world sprites with depth/occlusion and stable size/grounding, not DOM
images over the canvas. Begin with a single static facing image per archetype;
directional static variants remain an option after visual review. No skeletal
animation or sprite animation campaign. Transform movement, readable windups,
selection, impact flashes/effects, and modest audio can communicate state.

Evaluate textures at corridor distance and sprite groups at full cell
capacity: repetition, visual scale, overlap, target selection, alpha sorting,
light/shadow consistency, muzzle/projectile readability, and large-vs-small
silhouettes. Do this early enough to change art direction cheaply. Keep actual
accepted image files and runtime assets in Git, with compact asset/import
metadata; no large automated asset factory or source-copy provenance system.

## Proposed milestones and work packages

Approximately **63 focused implementation tasks** after refinement. The counts
below size the discussion; they are not Den records or a fixed task quota.
Split by coherent player behavior and ownership, not by file or testing layer.
Each feature includes its tuning, presentation, state/snapshot changes, and
focused validation instead of creating separate implementation/proof campaigns.

| Milestone | Candidate work packages | Playable result | Rough tasks |
| --- | --- | --- | --- |
| **1. Durable expedition skeleton** | Typed file content/admission; stable identities and base saves; shared cells/edges and movement reservations; smooth party controls; authoritative UI actions; required Engine contract checks | Walk, turn, pause, save/resume, and retune the existing dungeon | 6 |
| **2. First visual room** | Agreed art brief and image samples; texture import/materials; static world sprites; size/alpha/occlusion checks; representative room/light comparison | Inspect generated textured geometry and small/large static enemies in the actual host | 5 |
| **3. Characters, items, and hands-on exploration** | Party/formation and derived stats; inventories/stacks; equipment; drag/drop plus click transfers; world pickup/drop/throw; item use/consumables; containers/alcoves; doors/keys/plates/levers | Equip a party, move real items around, and solve a small physical obstacle | 8 |
| **4. First ranged-and-melee fight** | Action phases/recovery; targeting/visibility; hit/damage/death; rifle ammo/reload; shots/projectiles/throws; minimal melee and ranged opponents; combat feedback/HUD | Complete a fight using ranged attacks from the outset, with melee pressure and recoverable loot | 7 |
| **5. Crowds and tactical enemies** | Size footprints/capacity; stable slots and smooth repacking; reservation contention; Engine path queries/replanning; perception/noise/last-known targets; patrol/pursuit/search/return; ranged positioning/reload/retreat; mixed-group encounters | Small enemies share cells, large enemies constrain routes, and mixed enemies navigate and fight credibly | 8 |
| **6. Spells and party development** | Spell definitions/selection; cast/interrupt/resources; projectile and area effects; buffs/debuffs/resistances; utility/world spells; party injury/recovery/rest; advancement and meaningful loadouts | Magic, ranged weapons, and melee share combat rules; characters develop across an expedition | 7 |
| **7. Rich procedural floors** | Expedition graphs; varied room catalogs; loop/corridor tactics; doors/barriers/secrets; stateful puzzle composition; pits/bridges/stairs; voxel detail/material regions; encounter placement; supplies/loot/clues; realized validation/repair/seed inspection | Generate varied, detailed, solvable floors with purposeful combat spaces and item puzzles | 10 |
| **8. Complete expedition loop** | Persistent inter-floor travel/backtracking; full actor/item/action saves; objectives/finale/rewards; death/retry/new-run flow; difficulty/resource tuning; route/map/journal guidance; content integration across all floors | Play a complete multi-floor run, stop and resume it, then start a different seed | 7 |
| **9. Readability and closure** | Combat/inventory usability; visual/audio consistency; populated-world performance; seed/save regressions; end-to-end playtest and repairs | A stable, replayable foundation ready for Rifles-specific changes | 5 |

**Dependencies, not nine isolated handoffs:** milestone 1 enables 2 and 3.
Milestone 4 uses the real items/characters from 3; milestone 5 extends its
enemies; milestone 6 extends the same action/effect system. Generation design
and catalogs in 7 can begin after 1, but finished puzzle/population validation
needs 3–6. Milestone 8 integrates 3–7. Saves, visual evaluation, and practical
playtests evolve throughout; 8 and 9 complete them rather than introduce them.
The art brief is the prerequisite for 2's generated assets, not a reason to
block definitions, movement, or inventory work.

## Verification and campaign discipline

Keep authored test rooms as real selectable game content for movement,
stacking, doors/items, firing lanes, and spells. Also test generated expeditions:
passing a test room alone is not enough. The same commands, state owners,
content definitions, and Engine presentation serve both.

A task is finished when its behavior is wired into the running game, the
significant values are file-tuned, the necessary save contract is extended,
and relevant failure paths work. A mechanic hidden behind a debug-only call
or a test that bypasses the product is not the finished feature. Focus tests
on conservation/atomic item transfers, action timing, capacity reservations,
line-of-fire, generation reachability and gates, and complete save round-trips.
Do not build an exhaustive bespoke test framework or perform full-game
acceptance for every small change.

At each meaningful milestone, build/stage the paired C# product and play it
through ordinary controls. Distinguish source/test evidence from browser/GPU
observation. Capture before/after visual experiments with unchanged viewpoints
where useful. Include UI focus loss, cancelled drags, pause during an action,
death during transit, and save/load with live enemies when those systems land.

Finish the campaign with a complete no-debug expedition, a mid-run save/resume,
a different generated run, and mixed-size/mixed-range encounters. Validate a
bounded seed set and inspect failures; do not claim every seed is certified.
Measure populated-floor pacing/resource use and generation time before setting
performance budgets for the chosen test hardware.

Commit and push completed changes by default. Once this draft is agreed, Den
will own task ordering/status and task-linked evidence; this document remains
the campaign intent rather than a chronological progress log.

## Reference-code use

The local research games are **conceptual explainers**, not code to port or
frameworks to inherit. Use a narrow map for the behavior at hand, query the
index where useful, then inspect exact source and its callers. Incomplete or
turn-driven references cannot establish real-time behavior by themselves.
See [reference navigation](references/README.md) and its three maps. These
maps document research navigation, not provenance obligations for the earlier
one-time code transfers into Rifles.

## Defaults to discuss before task creation

- Four-member 2×2 party; enemy sizes pack within one cell, with multi-cell
  creatures deferred. Should any first-stage encounter require a larger footprint?
- Classic slot inventory, selectable known spells/hotbar, and lightweight
  advancement. Rune-combination casting, skill trees, crafting, and economy can
  wait unless they are important to the desired early play.
- World time continues during inventory and casting choices; an explicit pause
  stops simulation. Confirm whether an optional pause-on-inventory mode is wanted.
- Ranged is mandatory in the first fight; use a simple rifle with loaded state,
  ammo and reload timing. Defer misfires, detailed powder handling, and historical
  ballistic simulation. Choose body-blocking/friendly-fire policy explicitly.
- Visual brief next: material palette, texture treatment, sprite proportions,
  and mood. No art generation or large enemy roster before that discussion.

Free movement, destructible worlds, multiplayer, a universal game kit, a full
legacy-game port, procedural narrative, and complex physics are outside this
foundation campaign. Its durability comes from clear ownership and tunable
content, not from solving every possible future variant now.
