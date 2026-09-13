# Crowds and tactical enemies

Enemies share the party's step grid. A living actor keeps its source cell and
source footprint until a reserved step commits. Rendering interpolates between
that actor's source and destination anchors; hit bodies use the logical source.
Death releases occupancy and leaves the same enemy's recoverable belongings.

`content/definitions/crowds.json` authors small, medium and cell-filling large
footprints. Admission checks capacity, quadrant masks, actual rectangle bounds,
faction/sharing policy, and edge clearance. Existing anchors stay stable when a
neighbor leaves or dies. Arrivals select a fitting anchor and interpolate to it;
there is no per-frame reshuffle or multi-cell giant system. Exclusive party,
allied exploration actors and furniture retain their existing cell ownership.

Reservations retain a conservative departure lane across source and destination
anchors, so a rear slot cannot walk through a stationary front slot. A blocked
rear waiter yields to the actor that must clear its lane. Reservations have
bounded waiting and explicit blocked outcomes. Opposing edge
moves and swaps are rejected. Cancellation and death clear reservations and
waiting intent. Engine weighted paths use a freshly replaced, size-specific
occupancy overlay; retained path results are copied before another query.
Rotating decision order, per-step query limits, candidate limits and retry
cooldowns bound navigation work. A narrower gate blocks the large footprint.

Enemy policy has patrol, pursuit, search and return states. Sight uses an authored
cone and Engine opaque-geometry/furniture traces. Actor sight occlusion is authored
separately (off in this encounter). Shot admission checks all bodies,
so seeing the party does not imply a clear shot through another enemy. Gunfire,
party footsteps and the opening gate's alarm create immediate hearing events.
Knowledge retains a last-known cell, expires on admitted time, and never tracks
a hidden party's live cell. Search rotates at the last-known location and returns
to the patrol after its authored deadline.

Rifle users seek legal firing positions, keep a preferred range, reload real
inventory ammunition and have a finite retreat allowance per engagement. A
cornered rifle user holds ground instead of refusing to act forever. Exhausted
ammunition falls back to melee. Melee and ranged attacks still use the milestone
4 action phases, body traces, damage and inventory owners.

`content/definitions/combat.json` contains six archetypes and a separate encounter
spawn list. Raider, musketeer, fast powder runner, noise-sensitive sapper,
cell-filling bulwark and longer-range marksman differ in footprint, attention,
movement, resilience and engagement range. Multiple spawn identities may reference
the same archetype. The initial encounter includes a medium-plus-two-small group.
Patrol offsets resolve onto the generated floor; this is an authored test roster,
not the later procedural encounter distribution pass.

The roster reuses the existing GPT-generated directional sentry stills at authored
scales in both art treatments. This tests crowd placement and dynamic lighting;
six distinct final creature art sets remain an art choice, not a new renderer.

Save schema 5 stores enemy instance identities, individual anchors, in-flight
reservations, patrol routes, last-known targets, search/retry timers and retreat
budgets alongside existing combat facts. Transient sound events are not replayed.
Older prototype saves are rejected by the schema boundary rather than guessed.

The combat panel exposes each enemy's action or attention state and movement
outcome. Select an individually visible enemy, then use Space to attack and T to
reload. The existing gate puzzle, four-character inventory, pause, save and load
controls remain available.

## Validation

`bash scripts/check.sh` passes: solution build, UI typecheck, game/procgen checks,
offline artifact self-check and CoreCLR staging. Focused checks cover incompatible
factions, masks versus capacity, edge clearance, bounded contention/cancellation,
exact reservation anchors, awareness expiry/search/return, retreat budgets and
multiple saved instances of one archetype.

The ordinary-control browser run opened the existing gate puzzle, targeted and
killed the raider with two rifle shots, then observed the musketeer advance and
reload while smaller enemies continued through the corridor. This exposed and
fixed pursuit to an occupied last-known cell: it now queries reachable adjacent
contact cells instead of asking Engine to path into the occupied goal.

A paused save retained moving enemies and a musketeer reload with 0.9 seconds of
windup remaining. Advancing the fight produced a shot and reduced Warden vitality
from 25 to 14; Load restored 25 vitality, the prior enemy positions, selected
sapper and 0.9-second reload. Original captures and their identifiers are in
`docs/evidence/milestone-5/`. Browser captures use the local software-rendered
320×180 canvas and do not establish accelerated visual quality.

The native Wolf GPU run separately observed lit, individually readable sprites
at the corridor mouth and after backing into the entrance room. It did not clear
the whole encounter or certify every animation frame; see the bounded report in
`docs/evidence/milestone-5/gpu-report.md`. A subsequent source check found the
rear-slot departure case, added conservative lane reservation, and verified that
the front actor can clear the way without being starved by its blocked neighbor.

The final browser repeat loaded a newly written checkpoint under the lane rules,
backstepped into the entrance room, observed continuing raider/musketeer/runner/
sapper movement, and loaded the paused checkpoint again. Its separate
`final-browser-captures.json` index records the final admission behavior. An earlier
in-flight test save that crossed an occupied entry slot was rejected rather than
partially restored; it was replaced through ordinary Save under the final rules.
