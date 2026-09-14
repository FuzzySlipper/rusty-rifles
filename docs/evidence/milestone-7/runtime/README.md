# M7 runtime observation, 2026-09-14 UTC

Implementation checkpoint: 06f9c5c, followed by the resource-cache correction.
The owner confirmed ordinary eastward traversal over the height changes, but
reported geometry clipping. The subsequent FloorSurface correction clamps
camera/sprite support above the authored voxel treads and has focused regression
coverage. A clean visible retest is still required.

Wolf session `3558f2ca-9790-47aa-8f23-e85ede53d3d6`, slot-2, profile
`rusty-rifles-gpu`, rendered the textured Guard Hall, material variation/recess,
item bag and billboard enemies. [Original initial capture](gpu-initial.png).
The party was defeated. The worker failed with a harness Bad Request; root
continued the owned session. Native pointer attempts aimed at the visible
Restart button instead acquired canvas pointer lock. Firefox Find/keyboard
focus navigation scrolled the panel, but did not establish a successful reset.
Thus these captures do not verify new traversal, puzzles, hazards or save/load.
No game state or camera was fabricated for evidence.

The GPU session was cleanly [released](gpu-release.json). A following managed
browser start returned `pool_busy`; both available slots belonged to other
projects. No unrelated sessions were stopped. The requested expansion to four
slots is tracked by crew-services #8266: remote targets must be provisioned at
idle before restarting the service. Merely changing the configured number
would not provide four usable targets.

Separate live Engine navigation audit and population/progression readbacks are
in the parent evidence directory. The arrival manifest matches the exported
29-arrival floor. The bounded [C# profile](update-profile.json) measured a
1.97 ms p95 update in the observed defeated-party scene; this is not a benchmark
of active combat or the player's GPU.
