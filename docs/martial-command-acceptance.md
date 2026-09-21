# Martial command acceptance — campaign #8388

This is the running acceptance record. Den owns completion status; this document does not certify unfinished checks.

## Source checkpoints

- `4223611`: typed formation coverage and martial reach.
- `25ea092`: protected central commander, six soldiers, three equipment slots and stateful muskets.
- `10af92e`: forward Fire/Melee orders and paused, timed formation planning. Full repository check passed.
- `8fc3097`: automatic reload, fix/unfix bayonets and unique provider-based abilities. Full `bash scripts/check.sh` passed, including UI, solution, procgen/game checks, offline tool and CoreCLR staging. Initial `pnpm install --frozen-lockfile` was completed once for the campaign.

## Runtime findings

An isolated `10af92e` checkout was served through the existing broker on port 37301. Headless browser session `d4487d87-624f-4f94-bc34-fbc72961de9d` showed seven members with the commander centered and Fire/Melee controls without enemy selection. It did not complete formation acceptance.

The simulation had been running before browser attachment, so the party had already taken damage by the first observation. Time after browser connection is not time since the expedition began. Fresh runs now use the authored `startPaused` setting so the player explicitly resumes before combat advances. Saves retain their own pause state.

Restart produced `CSHARP_APPEARANCE_IN_USE`: the previous published frame still referenced floor appearances when `Mount` disposed them. The fix clears the published appearance snapshot before disposing the old floor, using the same Engine lifecycle rule as shutdown. Live restart/load/travel verification remains required.

The original diagnostic is [preserved here](evidence/martial-command/restart-failure-diagnostics.json), with original [fallen-party](evidence/martial-command/pre-fix-fallen-party.png) and [restart-attempt](evidence/martial-command/pre-fix-restart-failure.png) captures. Browser captures and event journal remain under `/home/agent/.local/state/crew-playtest/browser/d4487d87-624f-4f94-bc34-fbc72961de9d/`. That session was stopped and its slot released.

## Remaining visible matrix

- Three forward lanes and fallback onto one exposed target.
- Weapon reach, automatic reload, interruption and fixed/unfixed penalties.
- Unique abilities with independent provider readiness.
- Formation cancel, execute, fixed commander, movement locks and unaffected soldiers acting.
- Charge contact, obstruction, lost target and no repeated impact.
- Commander exposure and defeat while soldiers remain alive.
- Timed save/resume, restart and whole-run floor travel.

No new art, audio, drummer, morale or separate soldier navigation is part of this campaign.
