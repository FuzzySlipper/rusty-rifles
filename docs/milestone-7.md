# Milestone 7: rich procedural floors

G01 (#8195) starts this milestone with retained expedition graph intent.
The later room, routing, puzzle, traversal, detailing and population tasks
remain separate; this is not a completed three-floor playable expedition.

`content/tuning/generation.json` authors the expedition's entrance/objective,
floor roles and GraphCore rule combinations, floor connectors and per-floor /
total graph budgets. The initial plan contains:

| Floor | Role | Graph choice |
| --- | --- | --- |
| Supply Approach | Approach | Direct path versus optional cache detour |
| Flooded Stores | Supply crossroads | Key route and hazard/preparation branch |
| Inner Redoubt | Finale | Preparation gate and return shortcut |

The pure `ExpeditionGenerator` composes existing GraphCore rules; physical
layout still uses `DungeonGenerator`. It namespaces node, edge and item IDs by
floor and preserves authored connector IDs. It validates individual goals and
the connected expedition using the existing item-aware graph validator.
Unreachable objectives/floors and exhausted budgets produce explicit diagnostics
and no partial accepted result. Generation does not retry indefinitely.
Floor seeds are stable by authored identity, independent of catalogue ordering.

The actual product generates this intent at startup, uses its entrance graph
for the current physical floor, and saves the complete resolved plan. Every
played floor carries its graph/floor identity. Save validation checks that link,
connectors, budgets and progression without regenerating from current content.
G01 introduced schema 7; G02 below advances it to schema 8. Older prototype
saves are not silently upgraded by inventing missing resolved data.

Use `rifles.expedition.read` in the live debug console to inspect the current
resolved plan. This is read-only and does not move the party. See the
[staged runtime graph](evidence/milestone-7/g01-graph.md) and
[full readback](evidence/milestone-7/g01-resolved-intent.json).

Validation: `bash scripts/check.sh` passes. Focused checks cover four seeds,
structurally different roles, item-aware objective reachability, catalogue
reordering, total quotas, rejected rule termination, corrupted connectors and
objectives, identity mismatch, and codec roundtrip after tuning changes. The
staged runtime's debug command returned all three resolved roles (3/6/5 nodes).
That readback verifies live integration, not physical realization of the other
floors or their eventual gate/puzzle semantics.

During this build the independent browser playtest also exercised a real
inventory stack transfer in the rendered entrance scene. The
[C04 acceptance recheck](evidence/milestone-7/inventory-acceptance.md) preserves
its evidence and the still-unavailable native drag gesture separately.

## G02: functional room catalogue (#8200)

Eight editable templates in `content/definitions/rooms.json` supply guard,
receiving, powder, records, mess, watch, drainage and redoubt spaces. Dot/hash
plans describe walkable cells and solid recesses/piers; explicit outward exits
are thresholds, and content sockets remain tied to actual floor cells. These
are reusable room shapes, not copied levels. The campaign's 12–20 combined
room/puzzle motifs are still a later aggregate target.

Each template declares compatible graph-node kinds. Seeded catalogue matching
selects the first eligible fit, independent of catalogue order, under a per-room
candidate budget. The product catalogue constrains shapes to the reserved layout
envelope; existing generic catalogues retain materialized-bound validation.
Generated identities now include actual placed cells and sockets, so changing a
plan under the same template ID changes its geometry identity.

The played floor retains resolved room names/functions, graph nodes, actual cells
and thresholds. The HUD reports its current room and `rifles.floor.read` exposes
that resolved geometry. Save schema 8 stores it alongside the G01 intent; earlier
prototype save schemas are not silently upgraded. See the
[actual floor plan](evidence/milestone-7/g02-floor-plan.svg) and
[live readback](evidence/milestone-7/g02-resolved-floor.json).

Full `scripts/check.sh` passed. All eight templates independently fit/connect;
approach and redoubt floors generate across four test seeds, exercising seven
templates through role-based selection. Checks also cover catalogue reordering,
invalid interior thresholds, oversized shapes, per-room exhaustion and saved
room/graph coverage. A known stores-floor routing rejection occurs with both
this catalogue and the previous square template; the test reports it explicitly
rather than counting that floor as accepted. The concrete case is handed to
G03 (#8216). G02 does not claim all expedition graphs are physically realized.

The focused source review found and prompted two corrections: threshold steps
must leave the plan/shape envelope, including at the shared catalogue boundary;
and the complete saved floor manifest now has an integrity identity separate
from the source layout identity. Room metadata, graph/region associations,
thresholds and cells are covered without consulting a changed live catalogue.
Internal-pier exits and edited saved manifests have dedicated rejection checks.
The full check suite passed after these corrections.

GPU observation confirmed the textured Guard Hall and its wall return/recess.
The shared party was already defeated. A normal reset attempt then hit the
playtest service's command-channel EOF, so this batch does not claim a fresh
living-party movement test. All owned sessions were released; see the
[observation and limitation](evidence/milestone-7/g02-gpu-observation.md).

## G03–G10: composed floor implementation

The current schema is **9**. A save retains corridor centerlines and widened
cells, room heights and directed step connectors, architecture facts, generated
features and their state, accepted encounter placements, and initial supply
placements. Runtime inventory, drops, enemy state and admitted hazard phase
remain authoritative; loading does not generate replacements from a seed.

### Routing and physical features

Routing checks room thresholds, crossing and adjacency. Open passages sharing a
room may form junctions; protected passage bodies stay separated. Bounded layout
and exit-assignment repairs handle layouts that cannot be routed initially.
`generation.json` authors the width, length, path, ordering and layout budgets.
Width is the maximum realized width of an open straight run: room thresholds,
bends and protected passages remain narrow. Every added floor cell is retained
and checked for connection and separation. Route scores expose straight runs,
bends, flanks, thresholds and purposeful dead ends.

Non-open graph routes create real voxel barriers and navigation exclusions.
A locked passage requires its matching reusable inventory key and weight on its
counterweight plate. The supplied weight can be carried, placed or thrown; the
latch stays open so it can be recovered. Concealed panels provide a mortar/draught
clue, then a discover/open interaction. Reveal magic discovers nearby visible
handles. Feature-targeted utility magic uses the same reach and prerequisites as
normal use. Return shortcuts have a handle on their authored return side and
latch open after use.

Hazard rooms contain a timed scalding drain and reachable shutoff. Pulse period,
active interval and damage are file-authored. Each pulse hits an occupying party
once, using Engine-admitted time; its phase, hit flag and disabled state survive
saving. The physical progression inspector checks access to keys, weight
sources, plates, handles and the objective before accepting the composition.

### Heights, architecture and population

Raised rooms, raised bridge cells and lowered recoverable pits use adjacent
planar Engine navigation cells at different Y coordinates. Connector clearance
uses the existing shared movement reservations and actor footprints; narrow
bridges exclude larger enemies. Entering a pit applies authored damage, and an
adjacent step climbs out. Stair treads and lowered pit floors are voxel geometry.
This stage supports one walkable height per X/Z coordinate, not simultaneously
stacked rooms. Travel between the expedition's named floors remains M8 work.

Budgeted architecture recipes add trim, recesses, supports, damage and material
regions using the existing generated textures. Reserved thresholds, features and
connectors remain clear; carved walls retain backing and cannot be attacked
from multiple sides by competing detail cuts. Resolved facts are saved rather
than regenerated on load.

Encounter groups select suitable functional rooms, legal size placements and
reachable attack positions. The melee patrol and ranged watch are required;
the heavy guard is optional where no suitable room remains. Arrival safety,
room/group limits, difficulty and search budgets are authored. Rejections are
retained. Ammunition allowance follows the accepted enemies' vitality, with
finite recovery supplies placed on useful routes. Unlocked resource-grant rooms
receive recovery caches; only graph items required by a barrier become keys. Insufficient resource budgets
reject explicitly.

### Inspection and evidence

- `rifles.floor.read`: resolved floor, routes, heights and architecture.
- `rifles.floor.population`: retained gates, keys, plates, hazards, supplies and encounter placements.
- `rifles.floor.validate`: initial physical key/plate/handle progression model.
- `rifles.floor.navigation 20000`: bounded comparison with live Engine step admission, including closed doors and heights.

The [twelve-floor bank](evidence/milestone-7/floor-bank/README.md) contains actual
resolved JSON, readable SVG plans and inspection reports for expedition seeds
0, 1, 29 and 83. It exercises all eight room templates, repair attempts, heights,
protected routes, required combat roles and supplies. This supersedes the G02
Stores-routing limitation. These are offline plans, not game captures.

Reproduce exports with:

```sh
dotnet run --project tests/Game -c Release -- --export-floors /tmp/rifles-floors
python3 scripts/render-floor-plan.py /tmp/rifles-floors/29-arrival.json /tmp/arrival.svg
```

The full repository check passes. Live startup exposed and corrected duplicate
voxel transaction addresses and unused material bindings. The served arrival
floor matches the exported manifest. The retained [live navigation readback](evidence/milestone-7/m7-live-navigation.json)
reports a complete comparison with no mismatches; [population](evidence/milestone-7/m7-live-population.json)
and [progression](evidence/milestone-7/m7-live-progression.json) readbacks establish
runtime integration separately from offline checks.

**Visible acceptance completed on 2026-09-14.** The bounded
[retest and original captures](evidence/milestone-7/visible-retest/README.md)
cover arrival bridge traversal, secret discovery/opening, generated key and
counterweight inventory interaction, gate traversal, pit damage and escape,
timed drain shutoff, and save restoration. Normal enemies remained active.
A navigation-overlay failure discovered during play was corrected and retested;
large floor debug output was compacted to fit the SDK result limit. The final
Stores Engine comparison is complete with zero mismatches across 1,536 steps.

These are managed-browser, keyboard/DOM-assisted observations, separate from
GPU performance evidence. The original clipping report was not reproduced in
the sampled corrected bridge traversal; pit faces remain dark and the stepped
geometry needs visual polish. Native inventory dragging and Engine browser-binding
recovery remain separately documented limitations. Stores was selected through
a temporary standalone test configuration; default content was restored and
named-floor travel remains M8. The earlier
[runtime observation](evidence/milestone-7/runtime/README.md) records superseded
pool/input blockers. Crew-services #8266 completed the four-slot expansion.
