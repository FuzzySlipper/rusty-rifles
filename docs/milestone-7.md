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
