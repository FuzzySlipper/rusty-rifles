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

The remaining M8 work is objectives/finale, defeat/new-seed flow, difficulty,
discovered maps/notes and complete three-floor play acceptance.
