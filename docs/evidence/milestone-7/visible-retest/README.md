# Milestone 7 visible retest — 2026-09-14

The generated-floor interactions pass the bounded retest below. This combines
ordinary game controls, original managed-browser captures, and separately
identified read-only Engine telemetry. It does not certify every generated seed,
complete an expedition victory, or establish GPU performance.

## Conditions

- Product baseline `ace3a4e`, plus the navigation-overlay and compact-debug-output
  fixes committed with this evidence. Exact Engine pair: `03ac310b95c2`.
- Maintained crew-services `playtest`, managed Chromium. CSS viewport 1280×720;
  renderer backing canvas 320×180. These captures are intentionally low resolution.
- Arrival uses the normal seed-29 configuration. Stores uses the retained
  [standalone scenario](stores-scenario.json), selecting the existing Stores rules
  and seed `6871969167356124647`. It matches the exported bank floor identity
  `1fe52f5726bdf805e22c8e645ece43a897cef40546044b97b3b3ac4896423ae2`.
  This selects a test floor; it does not implement M8 named-floor travel.
- Enemy, damage, inventory, resource, generation and movement definitions were
  unchanged. No teleportation, invulnerability, fabricated items or state-setting
  debug commands were used. Pause was used between observations.
- Keyboard holds drove WASD/QE/F; ordinary HUD buttons and inventory destinations
  were activated with Enter through the browser accessibility surface. Route
  scripts used the floor map and visible HUD coordinates to choose movement.
  This is map/DOM-assisted play, not blind human discovery or native drag proof.
- Owned sessions were released. Default generation tuning was restored afterward.

## Observations

| Interaction | Visible result and supporting evidence |
| --- | --- |
| Arrival bridge | [Approach](arrival-bridge-1.png), [crest](arrival-bridge-2.png), and [descent](arrival-bridge-3.png) show traversable textured geometry. The HUD records Level 1 at (106,98), then Level 0 at (107,98). The final party cell is (109,98): an enemy blocks further entry. No solid-surface crossing was established in these sampled frames; this is not continuous-video proof of flawless clipping. |
| Concealed route | F discovers and then [opens the barrier](secret-fixed-3.png); ordinary movement [crosses into the branch](secret-fixed-4.png), ending (89,98). [Save/restart/load](secret-save-3.png) restores that position and the opened route. [Retained gate facts](arrival-restored-population.json) agree. |
| Supplies and key route | [Arrival recovery supplies](supply-pickup-2.png) transfer into the Warden's pack. The alternate corridor route reaches (100,112) with starting party health intact; ranged shots stop or miss en route. [Generated key and iron weight](key-weight-pickup-1.png) transfer into Blade's pack using ordinary inventory destinations. |
| Counterweight latch | The gate initially remains closed. [The actual iron weight occupies the plate](counterweight-2.png), with the visible inventory reporting 50 mass. After walking away to its handle, F [opens the latch](gate-save-1.png), and the party [crosses it](gate-save-2.png) to (113,91). |
| Gate persistence | The first post-load capture `gate-save-3.png` is **too early** and still shows restart spawn. It is not a restoration pass. The first visible HUD observation in `gate-restored-route.js` subsequently reads (113,91); [later retained state](hazard-restored-population.json) confirms the opened gate. Scene reconstruction needs more than the script's initial 600 ms wait. |
| Pit consequence and escape | [Entering (101,85), Level -1](pit-success-1.png) subtracts exactly four health from each member: 28/32/26/28 becomes 24/28/22/24. [An ordinary backward step](pit-success-2.png) reaches (101,86), Level 0, with the same health. |
| Timed drain | From that run, [the pulse](drain-success-2.png) reduces the other three members to 24/18/20; enemy melee also damages Warden. F produces “Drain shut off.” [After another 6.5 seconds](drain-success-4.png), those three remain 24/18/20, while melee continues affecting Warden. |
| Hazard persistence | [Save/restart/load](drain-save-1.png), waiting for “Expedition restored,” restores the paused run at (100,85). [Retained hazard state](stores-final-population.json) remains disabled. The attempted step away was blocked by an enemy; it is not claimed as a movement pass. |
| Engine admission | [Final Stores navigation readback](stores-final-navigation.json) compares 1,536 steps completely, with zero mismatches. This is runtime structural evidence, separate from the images. |

The raw labels in some exploratory scripts were predictions. `drain-success`
labels “pit-entered” and “pit-climbed-out” refer to the drain test, not the adjacent
lowered pit. `gate-restored-route` ends with the old label “plate-arrival,” but
actually reaches the drain approach. Use the observations above and recorded
HUD text, not those labels, for acceptance.

## Defects found and corrected

An enemy replan while approaching the secret route failed with
`Spatial.ReplaceNavigationTraversal returned status 0` ([original failure](navigation-failure-2.png)).
Closed doors had already been removed from the Engine navigation projection,
but the large-enemy restriction overlay could still include their cells. The
product now excludes closed doors from that overlay. A fresh-runtime replay
opened, crossed and saved the secret route without the failure.

The larger Stores floor also exceeded the generated debug-result size limit
when pretty-printed. Floor and population commands now emit compact JSON;
the real floor read succeeds at 38,413 bytes. No replacement Engine transport
or pathfinding implementation was added.

## Limits retained

The pit and some unlit geometry have very dark/black faces, and the stepped
height treatment remains visually coarse. Those need further art/lighting
iteration. The original bridge clipping report is not reproduced in the sampled
corrected traversal; all angles and continuous transitions are not certified.

This uses keyboard activation for HUD/inventory. The separately documented
native inventory-drag and browser-binding recovery limitations remain open.
Standing idle under normal enemy attacks can kill the party; earlier exploratory
runs did so, and were restarted rather than counted as successful escapes.

[Manifest](manifest.json) records script IDs, original capture IDs, SHA-256 hashes,
and visible DOM observations. PNGs are unmodified captures; adjacent JS files
preserve the inputs. The full repository check was rerun after restoring default
content; see [check output](check.log). The offline twelve-floor bank remains the
broader generation evidence and does not become a twelve-floor visible playtest.
