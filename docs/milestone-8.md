# Milestone 8 — complete expedition loop

## Floor travel foundation (H01)

Floors activate from their resolved expedition identities. The party's current
members, inventory, spellbooks, loaded weapons and conditions travel together;
only floor-owned actors, items, geometry, mechanisms and effects sleep in a
retained floor. Inactive floors freeze. Sounds are consumed on the active floor
and never replayed from a saved event queue.

Travel requires standing on the corresponding exit/entrance, with a living
party and no unfinished party action, movement, rest or projectile flight.
The destination is validated and its Engine scene prepared before replacing
the active scene. Occupied arrivals reject; they never teleport another actor
or partially replace the run. Returning restores the retained content.

Run saves contain the active floor plus all retained floors. Cross-floor object
and reward IDs are unique. Party packs are stored once, and generated keys can
be carried away from their source floor. Open-container state is saved; travel
closes the previous floor's inventory view. Earlier single-floor saves use the
previous schema and are not silently treated as complete run saves.

The full repository check passed after this foundation. A managed-browser
ordinary-control run killed three arrival enemies, reached the descent, entered
Stores, and returned to (113,98). The three enemies remained dead; Warden's death
and Blade's 26 health persisted. Save/restart/load restored the same changed run.
Original captures and input scripts are in [milestone-8 evidence](evidence/milestone-8/).
This is keyboard/DOM-assisted functional evidence, not GPU performance evidence.

The later implementation below adds the run rules and player panel. The full
three-floor visible run and completion-save sequence are accepted below.

## Run rules and player panel

`content/definitions/expedition.json` owns the goal, finale text/reward, map
reveal distance, suggested seed increment and normal/hard profiles. The current
chain visits Supply Approach, Flooded Stores and Inner Redoubt. Securing the
redoubt requires clearing its garrison and standing on its exit after all floors
have been visited, with actions and flights settled. Completion pauses the run
and awards the finale experience once. Saves validate the completion facts and
expected total experience together; loading cannot award it again.

A distinct expedition panel exposes routes, objective progress, result, retry,
load and new-seed/profile controls. The discovered map records only nearby cells
with Engine line of sight, plus observed route landmarks and journal clues.
Unvisited floors and undiscovered secret gates are absent. Known floor maps are
retained across travel and saving. The DOM draws the supplied discovered map;
it does not compute visibility, run a game loop or own progression.

Retry keeps the current seed/profile but creates a new run identity and fresh
party/floor objects. New expedition accepts an unsigned 64-bit seed as text and
an authored profile. Neither operation overwrites the previous save. Failed
loads or generation leave the old active world available. Defeat freezes the
party's run and exposes the same recovery controls.

Normal uses standard incoming damage and a 2.2 shot allowance against generated
encounter vitality; hard uses 1.35 incoming damage and a 1.5 allowance. Both are
file values. The incoming multiplier applies after protection to weapon, spell
and hazard damage. Supplies are resolved when first visiting a floor, so revisits
never refill reserves or silently change the selected difficulty.

## Content and state checks

The integrated catalogue has six enemy archetypes (raider, musketeer, runner,
sapper, bulwark, marksman), eleven spells and eight authored room plans. Six
additional gameplay motifs are keyed routes, counterweights, secret passages,
pulsing drains, pit/climb transitions and return shortcuts: fourteen distinct
room/puzzle motifs in total, rather than counting palette variants as content.
The three floor roles select different graph rules and room functions.

RunCodec checks cover reload windup/recovery and exactly-once ammunition cost;
cast windup/recovery and exactly-once resource cost; an active projectile and
moving enemy; open-container state; map/profile retention; malformed floor/map
references; and forged completion/experience rejection. Existing action, spell,
combat and inventory checks remain the lower-level settlement coverage.


## Integration corrections

Ordinary play exposed a feature-button payload mismatch: projected feature IDs
were strings but the command contract takes an unsigned integer. The button now
passes the typed numeric target/revision, and a visible gate interaction confirms
that the command is admitted. The map follows floor changes while preserving
an explicit map selection within the current floor.

The seed-29 redoubt's friendly patrol occupied a protected direct route out of
the arrival room. An alternate route existed, but this was poor placement.
Patrol endpoints and solid dressing now avoid routes, thresholds, grants and
height connectors, and generation checks connectivity with all those objects
occupied. A twelve-floor seed bank covers the resulting placements. Existing
saved placements remain intact rather than being silently moved on load.


## Complete-run acceptance

The normal seed-29 expedition was completed through keyboard movement and DOM
controls in the managed Chromium session. The party visited Supply Approach,
Flooded Stores and Inner Redoubt; used rifles, melee, Spark, Mend and rest;
collected and carried keys; threw counterweights onto generated plates; opened
both protected gates; and cleared all six final-floor enemies. Earlier floors
retained their changed encounters, items and maps. All four members survived
at the final exit (113,85): Warden 26/40, Blade 32/32, Seeker 26/26, Mender 28/28.

Completion raised experience from 104 to 134 exactly once. Save, restart and
load restored `Complete`, the final exit and 134 XP. The first immediate load
attempt after restart did not restore before its observation; a separate settled
Load did. A new seed-314159 expedition started with fresh party development and
left the completed save intact. Loading recovered the prior 134-XP ending.
Then explicitly selecting Hard and saving replaced that save; restart/load
retained Hard, seed 314159 and the same party state. Fresh-floor movement reached
the east passage at (104,98), beyond the arrival-room dressing.

The normal run used its starting rifle ammunition plus a partial floor-cache
pickup; it finished with the rifle empty, while other members still held reserve
shots. Reloads, front-member healing, melee and rest were useful choices. Normal
and hard supply/damage parameters are file-authored; Hard received a short
movement/reload and persistence check, not a complete-run balance certification.
The replay set is seeds 29, 30, 67 and 314159 for placement checks, with the
complete normal run and hard seed-314159 recovery flow retained visibly.

Development-watch reloads interrupted the run, and the long-lived managed
browser eventually crashed. Recovery loaded the saved changed floor each time;
failed or interrupted scripts are not counted as completed gameplay. This was
an assisted functional playtest with read-only floor facts for route planning.
It does not certify unaided puzzle discovery, GPU performance, final visual
quality, or continuous clipping behavior. Some bright untextured floor regions
remain visible in the prototype. The existing arrival puzzle is now labelled
separately from generated route gates to avoid contradictory-looking readouts.

See the [evidence index](evidence/milestone-8/README.md) for original captures,
script IDs and observations. Full repository checks pass, including twelve
route-preserving dressing floors, cross-domain save checks, UI typechecking,
solution build, offline artifact checks and CoreCLR staging.
