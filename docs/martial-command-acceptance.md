# Martial command acceptance — campaign #8388

This is the running acceptance record. Den owns completion status; this document does not certify unfinished checks.

## Source checkpoints

- `4223611`: typed formation coverage and martial reach.
- `25ea092`: protected central commander, six soldiers, three equipment slots and stateful muskets.
- `10af92e`: forward Fire/Melee orders and paused, timed formation planning. Full repository check passed.
- `8fc3097`: automatic reload, fix/unfix bayonets and unique provider-based abilities. Full `bash scripts/check.sh` passed, including UI, solution, procgen/game checks, offline tool and CoreCLR staging. Initial `pnpm install --frozen-lockfile` was completed once for the campaign.

- `83357c1`: coordinated forward charge, normalized movement saves, partial recovery saves, authored drills, paused fresh runs, and restart appearance retirement. Full `bash scripts/check.sh` passed. Independent bounded charge review found no runtime/save blocker. Charge countdown currently reports normalized action time rather than slower wall time under a slow condition.

## Runtime findings

An isolated `10af92e` checkout was served through the existing broker on port 37301. Headless browser session `d4487d87-624f-4f94-bc34-fbc72961de9d` showed seven members with the commander centered and Fire/Melee controls without enemy selection. It did not complete formation acceptance.

The simulation had been running before browser attachment, so the party had already taken damage by the first observation. Time after browser connection is not time since the expedition began. Fresh runs now use the authored `startPaused` setting so the player explicitly resumes before combat advances. Saves retain their own pause state.

Restart produced `CSHARP_APPEARANCE_IN_USE`: the previous published frame still referenced floor appearances when `Mount` disposed them. The fix clears the published appearance snapshot before disposing the old floor, using the same Engine lifecycle rule as shutdown. Live restart/load/travel verification remains required.

The original diagnostic is [preserved here](evidence/martial-command/restart-failure-diagnostics.json), with original [fallen-party](evidence/martial-command/pre-fix-fallen-party.png) and [restart-attempt](evidence/martial-command/pre-fix-restart-failure.png) captures. Browser captures and event journal remain under `/home/agent/.local/state/crew-playtest/browser/d4487d87-624f-4f94-bc34-fbc72961de9d/`. That session was stopped and its slot released.

## Visible progress on `83357c1`

Headless Chromium session `7899d968-0521-480d-bccc-6e0faf5c76bb` confirmed
[fresh paused arrival](evidence/martial-command/fresh-paused.png), the complete
order surface, and [successful Restart](evidence/martial-command/restart-paused.png)
back to a living paused party. Engine diagnostics after Restart reported zero
warnings and errors. The fixed commander rejected repositioning, the
[large planner displayed a two-soldier draft](evidence/martial-command/formation-draft.png),
and [Cancel restored the paused status](evidence/martial-command/formation-cancel-paused.png).
These are original captures. The center tile's repeated commander label is being
replaced with its current health; this minor display change is newer than these
captures.

## Visible progress on `a499e9a`

Session `78aedf08-451f-45f5-8499-cf33c0467421` verified ordinary paused-menu
[Save](evidence/martial-command/fresh-save.png) and
[Load](evidence/martial-command/fresh-load.png) after the drop-ledger fix.
The authored scenarios use the normal game commands after debug setup:

- [Three-lane volley](evidence/martial-command/three-lane-volley.png): runner
  killed, raider damaged, and one rifle miss. Three distinct target selections
  remain uncertain from this capture alone.
- [Automatic reload](evidence/martial-command/automatic-reload.png) loaded
  Warden and both musketeers after the volley.
- [Concentrated fire](evidence/martial-command/concentrated-fire.png) killed
  the single exposed raider through multiple rifle contributions.
- [Melee contact](evidence/martial-command/melee-contact.png) after turning
  toward the enemy dealt 20 then 12 damage. The earlier forward order correctly
  found no exposed target after the enemy moved outside that facing.
- [Commander defeat](evidence/martial-command/commander-defeat-survivors.png):
  Commander reached 0/35 while five soldiers remained alive, confirmed by HUD
  DOM health readback. Blade was the authored front-center casualty.
- [Formation execution](evidence/martial-command/formation-execution-locks.png)
  retained the party cell and North facing despite forward, strafe and turn
  inputs. Two unaffected soldiers started Fire while the changed pair remained
  committed. Ordinary [Save](evidence/martial-command/formation-execution-save.png)
  and [Load](evidence/martial-command/formation-execution-load.png) both retained
  the paused 1.9s remaining transition. After resuming, the
  [completed formation](evidence/martial-command/formation-execution-complete.png)
  placed Seeker front-left and Warden rear-left without moving or turning the
  party. The unaffected Fire order was admitted; damage from that particular
  volley was not clearly attributable in the capture.

The fresh charge-clear drill rejected C with
`No forward target is reachable at a legal charge stop.` The
[original capture](evidence/martial-command/charge-rejected-investigation.png)
is retained for investigation; charge contact is not yet accepted. Browser
page errors were empty. These observations used DOM assistance and timed native
input batches, with gameplay paused between observations.

Source investigation found that an immediately deciding enemy can reserve the
next approach cell before C arrives. With the original far-side crowd position,
the nearest remaining charge stop was outside bayonet reach. The contact drill
now places the enemy on its near side, two cells ahead, with its normal 0.4s
decision interval initially remaining. This permits one ordinary charge step
without altering normal enemy tuning. Investigation also found a separate
bug: charge planning computed trace exposure but checked reach alone. It now
requires both an unobstructed first hit and authored reach. Visible replay is
still required.

## Remaining visible matrix

The first drill activation and ordinary paused-menu Save both rejected the fresh
floor with `Invalid saved combat inventory owners.` The original
[Save failure](evidence/martial-command/pre-fix-fresh-save-failure.png) confirms
this was not limited to drill setup. Fresh floor construction created generated
key, plate and supply inventories but did not pass their drop positions into
combat. The fix carries that existing ledger into fresh combat, including new
travel destinations. All six drill snapshots pass normal save admission;
live Save/Load and the successfully activated drills are recorded above.

- Three distinct forward target selections in one volley.
- Reload interruption, fixed/unfixed penalties and final readiness.
- Unique abilities with independent provider readiness.
- Formation cancel, execute, fixed commander, movement locks and unaffected soldiers acting.
- Charge contact, obstruction, lost target and no repeated impact.
- Timed save/resume and whole-run floor travel.

No new art, audio, drummer, morale or separate soldier navigation is part of this campaign.
