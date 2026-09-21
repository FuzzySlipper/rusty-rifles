# Martial command implementation campaign

Den project: `rusty-rifles` · Aggregate campaign: **#8388**.

Created 2026-09-20: nine implementation/acceptance tasks. This index preserves the original sequence; the [acceptance record](../martial-command-acceptance.md) describes delivered behavior and verification. [Accepted design](../martial-command-design.md) records scope and provisional tuning decisions. [Current ownership](../gameplay-design.md) describes existing code; [foundation campaign](task-index.md) remains historical context. Den owns live status and dependencies.

Start with **#8389 (M01)**. The parent is an aggregate and should close only after final visible acceptance. M01 resolves the remaining coverage examples into a concrete tunable prototype; it is not a broad architecture study or a new approval gate.

| Key | Den task | Work | Depends on |
| --- | --- | --- | --- |
| M01 | #8389 | Define and prototype 3×3 formation coverage and weapon reach | — |
| M02 | #8390 | Establish six soldiers and a fixed vulnerable commander | #8389 (M01) |
| M03 | #8391 | Adopt three equipment slots and stateful martial weapons | #8389 (M01) |
| M04 | #8392 | Replace selected-enemy attacks with forward party orders | #8390 (M02), #8391 (M03) |
| M05 | #8393 | Implement automatic musket reload and timed bayonet orders | #8392 (M04) |
| M06 | #8394 | Add paused formation planning and committed repositioning | #8392 (M04) |
| M07 | #8395 | Group party abilities with individual readiness and cooldowns | #8392 (M04) |
| M08 | #8396 | Implement forward charge as a timed coordinated maneuver | #8393 (M05), #8394 (M06), #8395 (M07) |
| M09 | #8397 | Tune and visibly accept the martial command combat loop | #8396 (M08) |

M02 and M03 can proceed independently after M01. M05, M06 and M07 can proceed after ordinary orders, with ownership coordination around combat/projection files. Charge joins these strands. Every task feeds M09's playable acceptance.

## Slice expectations

Each task carries detailed acceptance in Den and includes relevant typed content, UI/projection and current-schema save/travel integration. Do not leave all persistence or usability to the final task. No historical save migration is required.

- Commander protection and weapon reach are distinct from offensive lane preference.
- Forward orders select eligible participants; each retains individual timings/cooldowns.
- Automatic reload and fixed bayonets belong to the individual musket.
- Formation planning pauses; execution resumes time, locks party motion and commits positions after the authored duration.
- Charge is a separate timed maneuver through existing movement admission.
- M09 verifies source/build, runtime launch and visible interactions separately and tunes authored values from real play.

The final scope excludes drummer/audio work, morale, multi-cell formations, independent soldier navigation, finite ordinary ammo, special ammo types, generic weapon/ability frameworks and new art. See the design for full boundaries.
