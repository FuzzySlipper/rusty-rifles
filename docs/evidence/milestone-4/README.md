# Milestone 4 runtime evidence

## Automated checks

`bash scripts/check.sh` passed after the final implementation and HUD edits:
Release solution build, UI typecheck, procgen/game checks, artifact self-check,
and atomic CoreCLR staging. No warnings or errors.

Focused additions exercise action commit-once behavior, coarse admitted time,
recovery restore, cancellation and callback exceptions; real Engine inventory
transfer/consume/restore (including equipped unique-item flight transfer and
ammunition overdraw); and combat save admission, including malformed projectile
ownership, actors, and loaded-item references. Existing source-cell movement,
reservation, party, inventory, and gate checks remain passing.

## Browser interaction

The `rusty-rifles` browser profile ran on the ordinary broker-served product.
Its Chromium software-rendered canvas settled to 320×180 backing pixels at
1280×720 CSS size. These images establish visible behavior, not GPU performance.
Original PNGs are copied without modification; `captures.json` preserves their
capture, script, session, and original-path identities.

- `weight-landed.png`: the iron weight left the opened crate, traveled, and
  produced pressure-plate weight 50/50. The original item remained recoverable.
- `combat-load.png`: the unlocked gate, loaded rifle, inventory, pose, and pause
  survived ordinary Save/Load.
- `first-shot.png`: selecting the visible raider and firing reduced it from
  32 to 15 vitality and emptied the rifle.
- `dry-rifle.png`, `reload-interrupted.png`: distinct feedback for dry fire and
  cancelled reload; the interrupted windup did not spend another round.
- `pending-reload-restored.png`: a partially completed reload survived Save/Load.
- `raider-dead.png`: the raider approached, dealt melee damage, died to the
  next shot, released its cell, and exposed the musketeer's firing lane.
- `bolt-paused-in-flight.png`, `bolt-flight-restored.png`: Mender spent resource
  once and retained bolt recovery with the projectile paused before impact.
- `victory.png`: resumed bolt killed the musketeer. Both enemies are dead;
  Warden died to incoming fire while the other three members survived. His
  inventory remained available. The enemy's loading/firing and subsequent
  damage were visible in the combat readout.

The above receipts preceded the final narrower HUD, corrected enemy facing,
and blue orb presentation. A second ordinary-control setup on the final code
repeated loading, key use, crate-to-plate throw, gate opening, and target
selection, then saved a paused fight for the GPU observer.

Some supervised sequences initially failed because a selector matched both a
roster button and an inventory-owner button, or because world reconstruction
outlasted a short wait. They were corrected to stable `data-member` selectors
and a longer load wait. These are not counted as completed scenarios. Native
canvas focus can intercept browser pointer activation; ordinary keyboard
Enter on focused controls was used where needed. No game-state injection or
debug commands were used.

## Remote GPU

The final-code Wolf run restored the prepared encounter and verified native
Space/T/Space: raider vitality 32 → 15 → 0 and rifle/ammo 1/7 → 0/7 → 1/6 → 0/6.
The original frames show the narrower HUD, dynamically lit directional sprite,
and textured corridor. Native musketeer selection and completion of the entire
mixed fight were not established in that run; the party died during observation
and UI probing. The full mixed-fight completion evidence is the browser run
above. See [the GPU report](gpu-report.md) for indexed originals and limitations.
The GPU session was stopped and released cleanly.

## Boundaries and review

First-hit collision and weighted navigation use the matched Engine SDK/runtime
`0.1.0-dev.8c20a96d10ef`. Inventory state remains Engine InventoryWorld; vitality
uses exact tracks; product action policy receives admitted fixed steps. The
bundle/render-resource expansion remains separate Engine work.

A source review identified under-validation of restored flight ownership; this
was fixed with focused rejection tests. A suggested movement-origin issue was
assessed against the actual rule: an unfinished move retains its source cell,
but a completed move changes the logical origin used by a later weapon commit.
That behavior is intentional and documented in `docs/milestone-4.md`; attacks
never originate from an interpolated presentation position.

Milestone-3 native drag gesture acceptance remains outstanding. These combat
receipts exercise normal inventory commands and do not claim that gesture was
verified. Size-aware enemy groups and tactical AI remain later milestones.
