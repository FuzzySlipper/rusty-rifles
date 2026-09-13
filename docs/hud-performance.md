# HUD publication and Brave responsiveness

On 2026-09-13, the affected remote Brave session rendered around 10 Hz, delayed
buttons and repeatedly replaced browser attachments. The simulation remained
60 Hz. C# phase profiling measured total update mean 1.28 ms and p95 1.75 ms;
this was not a 100 ms simulation callback.

A six-second output sample contained 361 full HUD snapshots: 4.48 MB of 5.05 MB
of output. TCP sampling showed the browser receiver window closing and hundreds
of kilobytes queued. This establishes downstream pressure, not a slow LAN or a
specific expensive browser function.

In the controlled test, only game HUD publication was disabled. The user
refreshed and confirmed responsive movement away from spawn. Fresh renderer
telemetry showed 119.54 Hz with 8.3 ms frame intervals and the socket send queue
was empty. See the raw [renderer snapshot](evidence/cpu-profile/hud-disabled-renderer.json)
and [C# profile with the original HUD](evidence/cpu-profile/hud-unbounded-csharp.json).
This localizes the problem to the HUD publication/consumption path; it does not
separately quantify decoding versus DOM work or prove an Engine recovery defect.

The correction bounds periodic HUD snapshots using the positive, finite
`refreshSeconds` in `content/tuning/hud.json` (initially 0.1 seconds). It uses
admitted Engine time even while gameplay is paused. Initial state, lifecycle
publication and command feedback remain immediate. Held movement does not force
full HUD snapshots each simulation step. A delayed update publishes the latest
state once, without a catch-up burst. Simulation and world/camera presentation
are unchanged. Spell target options now rebuild only when roster names/IDs or
selection change, instead of on every party timer/vitality change.

Validation: the complete `scripts/check.sh` passed, including cadence checks for
immediate command feedback, periodic publication, catch-up and invalid tuning.
The user refreshed with the full HUD and confirmed it "stays responsive".
Fresh renderer snapshots measured approximately 119–120 Hz. The retained
[full-HUD samples](evidence/cpu-profile/hud-bounded-renderer.json) include later
stale snapshots: render sequence 5451 stopped advancing, so they do not prove
uninterrupted rendering over the entire capture.

A separate failure remains: the user's latest diagnostics show
`BROWSER_HOST_TRANSPORT_FAILED: runtime control replacement did not provide a
fresh input binding`, with runtime control revision advancing from 1 to 2.
The Engine browser host emits this when input recovery's `replaceControl`
response cannot complete binding recovery. It is not the product Restart method
(which resets game state without replacing the Engine control binding). The
trigger for that recovery is not established here. Preserve this as a separate
Engine input-recovery investigation; no downstream transport workaround was
added. A browser refresh is needed after that failed binding. The HUD slowdown
fix is confirmed by human interaction, but uninterrupted session recovery is
not claimed.
