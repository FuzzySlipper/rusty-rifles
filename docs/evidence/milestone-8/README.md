# Milestone 8 visible evidence

These are original managed-browser captures with the supervised input scripts
and compact observation receipts. JSON receipts retain script/capture IDs and
original paths. Tests used ordinary keyboard and DOM button controls; read-only
floor/debug facts helped route planning. This establishes functional interaction,
not unaided puzzle discovery, GPU frame rate or continuous collision quality.

| Evidence | Observed behavior |
| --- | --- |
| `panel` | Separate expedition panel and discovered map |
| `defeat` | Defeat result and recovery controls |
| `rifle-fight`, `spark-melee` | Rifle, spell and melee actions in the first floor |
| `stores-travel` | Descent with travelling party state |
| `key-save` | Key/weight collection and restart/load recovery |
| `plate-throw`, `gate-open` | Thrown weight and keyed route opened |
| `host-restart-load` | Save restored after full host and browser restart |
| `use-button` | Corrected feature-button command admitted |
| `stores-bulwark`, `stores-rest`, `stores-exit` | Mixed combat, recovery consumable and second descent approach |
| `redoubt-arrival` | Third floor reached, living party, 56 XP, all three floors visited |

Additional accepted sequences:

- `redoubt-sapper`, `redoubt-bulwark`, `redoubt-runner`, `redoubt-raider`: four final-floor foes cleared.
- `redoubt-key`, `redoubt-pickup`, `redoubt-plate`, `redoubt-gate`: final key, thrown weight and open route.
- `redoubt-final-rifles`: both rifle foes dead, 104 XP, living party.
- `cleared-floor-recovery`, `final-exit`: crashed browser recovered from save and ordinary return to the exit.
- `completion`: first capture shows Complete and 134 XP. Its second capture still shows the restarted run; this immediate load observation is not a passing restoration.
- `completed-load`, `completed-reward`: settled Load restores Complete/134 XP; starting another seed preserves the prior ending. The script's first new-run label says hard but its actual profile was Normal; use `hard-profile` for Hard evidence.
- `hard-profile`: explicit Hard selection, save/restart/load retains Hard and seed 314159.
- `fresh-placement`: fresh hard floor traversed from arrival to the east passage.

No full Hard run or GPU performance result is claimed. Development reloads and
one managed-browser crash were recovered through saves. See the milestone
report for the complete acceptance boundary.
