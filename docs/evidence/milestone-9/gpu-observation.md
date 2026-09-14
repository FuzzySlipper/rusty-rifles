# Final GPU observation

Wolf session `18d35adb-0eed-4574-b563-39d381945788` was the only owned active
client after the root stopped its software browser. The independent playtester
and root inspected the original [ready Guard Hall frame](gpu-guard-hall.png).
It shows 1280×720 output, repeated brick/floor textures, warm tan ceiling light,
and a central full-body sprite whose feet meet the floor. No obvious alpha
rectangle surrounds it. Foreground sprites and some scene space are obscured
by the prototype HUD. This is a neutral warm view, not a complete directional
or lighting sweep.

Root readbacks from the same sole-client interval report accelerated AMD,
1280×720 backing/CSS and 58.77 then 58.53 submissions/s, with render sequence
advancing 12123 → 18013. See `gpu-metrics.json`, `gpu-metrics-later.json` and
`performance.md`. The generic browser adapter string is not a physical GPU model.

The observer unpaused using P; combat continued and Warden fell before the
final pause. Native Restart click attempts did not visibly restart the game.
No claim is made that those clicks reached their intended DOM target. Root
subsequently used an ordinary DOM-assisted Restart to leave a fresh paused run.
Directional/corridor comparisons and audio were not independently observed in
this bounded GPU pass. Earlier milestone art observations remain separate;
this report does not relabel them as new captures. Bundled audio admission is
recorded in `audio.json`, without a claimed listening assessment.

Original artifacts are under
`/home/dev/dsh-crew/experiments/wolf-den-srv/controller/state/18d35adb-0eed-4574-b563-39d381945788/`:

- `254cb60a-c720-4542-ab5b-21b43b7301bf.png`: neutral paused view copied above.
- `c02fbef2-127d-4797-bece-1a756060a52a.png`: unpaused P probe.
- `06dc0558-8986-4702-930c-a878c842ce0e.png`: final paused combat state.
- `3cea167b-c8f9-44a8-b3b6-10ddf9abb1a9.png`: unchanged state after click attempts.

Cleanup: observer reported `released: true`, `local_capture_stopped: true`,
`errors: []`; root independently confirmed zero occupied slots at 13:25:30 UTC.
