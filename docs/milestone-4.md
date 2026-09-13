# Milestone 4 — the first mixed fight

The gate puzzle now leads to a melee raider and a rifle-armed musketeer.
Load a rifle with **T**, select a visible enemy in Combat, and attack with
**Space**. Both ranks can fire; melee requires a living front-row member.
Equip a weapon through Inventory to change that member's attack. Each member
acts independently. Bolt spends that member's resource; remedies use the same
windup/commit/recovery lifecycle.

The enemy behind another body cannot be selected through it. Committed attacks
aim at the cell selected during windup: moving clear can make a shot miss.
The attack originates from the actor's logical position at weapon commit. A move
that has already completed changes that origin; an unfinished move still uses
its source cell. This permits movement during windup without historical shots
from a cell the actor has left. Walls, closed gates, allies, and nearer enemies stop deliveries. Formation
members share the party's firing origin. World friendly fire defaults off in
`content/definitions/combat.json`; friendly bodies still block shots.

Rifles start empty. Reload commits one shot from the character's actual pack
into the unique rifle. Loaded state follows the rifle through transfers and
throws. Interrupting windup spends nothing; committed effects are never refunded
and their recovery cannot be skipped with Interrupt. Changing equipment cancels
an incompatible pending weapon action. Death stops that actor's actions and
movement, releases its reservation, and retains belongings. Defeat stops party
movement; Load or Restart provides recovery.

Throw selected sends one real inventory item through flight. Toss onto plate
aims at the existing pressure plate, allowing the iron weight to operate the
gate. The default impact policy leaves throws recoverable; the authored
`recoverThrownItems` switch can instead consume them. Bolts expire on impact or
range exhaustion. A moving actor owns its source cell until its step commits.

The game feeds only Engine-admitted fixed steps to combat. Engine segment casts
choose the first geometry/body hit. Engine weighted planar paths route toward
free contact cells using an overlay of occupied and reserved cells; MovementGrid
still admits and reserves every step. There is no product pathfinder or second
clock. Damage is deterministic and file-authored; no random roll is required for
this baseline.

Save schema 4 includes injured/dead enemies and allies, pending member/enemy
actions, loaded rifles, projectile position/direction, and flight/drop inventory
owners. Restoration validates these before binding replacement resources.
Recovery snapshots do not recommit effects. Art is still the provisional
existing directional sentry set; combat scales communicate windup and remains.
The next art pass can distinguish enemy silhouettes without changing actors.

## Tuning and scope

`content/definitions/combat.json` owns timings, ranges, damage, projectile speeds,
resource costs, enemy health/defense, awareness, movement/cadence, placement
distance, loot, body dimensions, and presentation scales. Item ownership and
capacity remain Engine inventory state. Party tracks and enemy vitality use
Engine exact mechanics.

This milestone adds basic opponents. Size-aware group occupancy, expanded
perception/tactical AI, richer generation, spell families, and floor travel
remain later campaign work. Inspecting the existing exit does not depart the
floor. ProductContent bundle-to-render-resource expansion remains Engine work;
this milestone uses the already installed matched package/runtime.

## Validation

Validation results and original runtime captures are recorded in
`docs/evidence/milestone-4/README.md`. The earlier milestone-3 native drag gesture
acceptance remains separate from its working inventory transfer commands.
