# Milestone 3: characters and hands-on exploration

The expedition now has selectable starter parties, living-member formation
swaps, equipment, packs, world items and a small gate puzzle. Balanced patrol
starts with an injured Warden and a Seeker low on resource; rifle drill changes
base statistics and supplies additional rifles. Presets restart the expedition.

Open **Inventory & equipment**. Select an item and a destination, or drag it
onto a pack or equipment slot. The amount field controls stack transfers.
Select a character in the roster to equip them or use a remedy on them. A rifle
uses both hands; displaced gear stays in the same pack and still counts toward
capacity. Rear members remain eligible for ranged attacks and casting; melee
reach is front-row policy. Combat itself is the next milestone.

Nearby floor quadrants, the workbench shelf, crate and weighted plate are real
inventory owners. An occupied anchor accepts only a compatible stack merge.
Open the crate before transferring its contents. Moving or loading closes the
active container panel; opening it again does not reroll its contents.

The brass key starts on the northwest floor quadrant. The crate holds an iron
weight. Carry the key to the gate's approach, unlock it, set its lever, and leave
sufficient weight on the plate. The key is reusable. Removing weight or switching
the lever off closes the door once its cell is clear, including in-flight
reservations. The closed voxel volume blocks the Engine's navigation and rays.

## Ownership and tuning

Engine `InventoryWorld` is the sole live ownership/quantity ledger. Transfers,
equipment displacement and consumption use detached candidates. UI drag state
is a proposal; it never removes an item optimistically. Commands recheck revision,
owner, capacity, slot/requirements and world reach. Definition/equipment sources
feed Engine statistics; vitality and resource use Engine tracks.

- `character-options.json`: roster presets, starting condition, base statistics.
- `items.json`: typed item kinds, capacities, equipment, requirements, ammunition
  classification, effects/costs, and common or preset-specific loadouts.
- `item-exploration.json`: resolved-anchor placement inputs, reach and puzzle weight.
- `item-art.json`: generated atlas sampling, physical dimensions and foot pivots.

World locations and mechanism state are separate from inventory ownership. This
lets richer procgen supply resolved anchors later without replacing item rules.
The existing reference maps informed corner/alcove placement and conservation;
Engine candidates replace donor rollback/cursor ownership patterns. No donor
runtime, item ledger, pathfinder or browser gameplay authority was copied.

Save schema 3 stores resolved anchors, mechanism state, stable owners/items,
stack quantities, equipment, party formation and resource tracks. It reconstructs
an Engine inventory world before publishing replacements. Inventory revision
counters are fresh after load; the product command revision invalidates old UI
proposals. Saves from earlier schemas are rejected, not silently reinterpreted.

The item atlas follows the provisional ink-and-wash treatment. The gate and
pressure plate use simple Engine geometry/materials. B05 lighting acceptance
subsequently resumed on pair `8c20a96d10ef`; see `docs/milestone-2.md`. No
renderer or shader workaround was introduced.

## Verification

[Verification and original captures](evidence/milestone-3/README.md) separate
Engine/domain checks, browser interaction and remote GPU observations. The full
check script passes. Browser play verified item transfers, restoration,
equipment, weighted gate traversal/blocking and save/load. Wolf verified the
room rendering and keyboard movement. Physical drag gestures remain an explicit
acceptance limitation of the currently exposed playtest controls.
