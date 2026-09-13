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
Save schema is now 7; older prototype saves are not silently upgraded by
inventing the missing expedition intent.

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
