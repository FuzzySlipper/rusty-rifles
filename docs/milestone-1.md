# Durable expedition skeleton

Milestone 1 is the first playable exploration foundation, tasks A01–A06. It uses
SDK/runtime pair `0.1.0-dev.2e4255bd3ad5`. Combat, inventory, richer generation
and the generated art ensemble belong to the subsequent campaign milestones.

## Controls and state

W/S step forward/backward, A/D sidestep, Q/E turn. Hold a key to repeat; release
stops after the current action. The current update retains at most one movement
request, with the last Engine-admitted direction winning. Requests do not build
up across updates. Focus clears, pause, and UI commands suppress new movement;
an already started action finishes when simulation continues.

P toggles explicit pause; K saves and L loads. The DOM buttons select a member,
pause/resume, save/load and restart. Opening or focusing the UI does not pause
the world. F uses the focused feature, R cycles eligible features. Inspect the
entrance lantern to extinguish/light it. Inspecting the green exit records arrival;
travel to further floors is milestone 8. The clockwork porter is a moving actor
using the party's grid contract; its simple primitive appearance is replaced by
the art work in milestone 2.

## Domain boundaries

- `GameDefinitions` reads admitted content through Engine `Content` references
  into typed domain records. Errors identify the asset and invalid field.
  Dev-host file watching reloads the product after source/content edits.
- `DungeonFloor` holds resolved cells, room-light anchors, endpoints and the
  generation identity. Loading never reruns the generator. Pure procgen still
  takes typed inputs and performs no file I/O.
- `MovementGrid` owns exclusive occupancy/reservations and blocked-edge policy;
  Engine navigation admits every translation and rechecks it at completion.
  Source occupancy (and future hit ownership) stays at the source during
  transit; the destination and undirected edge are reserved. Completion commits
  the destination once. Cancellation or a newly blocked edge releases the
  reservation, keeping the actor at its source. Shared-cell size packing is
  deliberately deferred to milestone 5, extending this same grid.
- `ExplorationState` stores logical pose and action progress. Product motion
  projects a visual pose without using it for admission; Engine camera samples
  interpolate presentation. Pause freezes action time. Snapshot restore preserves
  pending moves and rebuilds reservations before replacing live state.
- `WorldFeatures` supplies stable target/revision, reach and eligibility facts
  to Engine `InteractionFocus`. Engine spatial rays determine visibility against
  the same voxel session. Use revalidates fresh facts; the UI never authorizes it.
- UI payloads use `context.intents.claim` and `rifles.command.v1`. A product
  command revision rejects duplicate/stale UI actions. Loading/restarting changes
  that revision; target revisions additionally reject changed features.

## Saves and identities

Engine `ProductStateStore` stores `expedition/current` under the host-selected
persistence root (normally `.runtime/persistence`). K/Save replaces that slot;
L/Load explicitly opens it, including after the host/browser is restarted.
Runtime saves are local user data and ignored by Git; authored content is tracked.

The schema stores expedition GUID, floor/party/object IDs and the next allocation
counter, resolved floor, party roster and vitality, selected member, party/porter
motion and route, pause, and lantern/exit state. Object IDs are scoped to the
expedition; future spawns allocate monotonically and do not depend on roster
indices. Member IDs are scoped to that expedition's persisted roster, which is
validated independently of the current starter file. Light presentation IDs
are allocated afresh per live resource so a replacement scene can be prepared
while the previous scene still exists. They are not gameplay identities.

A load constructs and validates the candidate state, occupancy and reservations
before replacement. Incompatible schema, invalid pose/roster/identity, impossible
reservation, and missing save errors leave the previous live state usable and
show feedback. A tuning change that invalidates saved timing or geometry can be
rejected; this initial schema does not promise migrations for future rules.

## Tuning

- `content/tuning/exploration.json`: step/turn durations, grid/eye/ceiling scale,
  Engine chunk/navigation bounds and camera projection/interpolation delay.
- `content/tuning/generation.json`: seed, graph intent/rules and typed quotas.
- `content/tuning/appearance.json`: initial surface colors/roughness/emission and
  room lights.
- `content/definitions/party.json`: starter identity/name/formation/vitality.
- `content/definitions/exploration-features.json`: porter pace/appearance, lantern
  appearance/offset/light and feature reach/focus cone.

Generated images, textures and final sprite style remain the next milestone;
start those tasks from the existing art direction and prompt recipes.
