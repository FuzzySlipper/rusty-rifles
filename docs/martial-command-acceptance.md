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

## Remaining visible matrix

- Three forward lanes and fallback onto one exposed target.
- Weapon reach, automatic reload, interruption and fixed/unfixed penalties.
- Unique abilities with independent provider readiness.
- Formation cancel, execute, fixed commander, movement locks and unaffected soldiers acting.
- Charge contact, obstruction, lost target and no repeated impact.
- Commander exposure and defeat while soldiers remain alive.
- Timed save/resume, restart and whole-run floor travel.

No new art, audio, drummer, morale or separate soldier navigation is part of this campaign.
