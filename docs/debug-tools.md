# Runtime debug tools

The top-right **Debug** toolbar hosts the packaged Engine UI. Press Escape to
release the game cursor, then use **Open debug console** or **Show metrics**.
The buttons are also keyboard accessible with Tab and Enter. **Hide metrics**
and **Close debug console** remove the readouts when finished.

The console provides Engine's generated command catalog, completion, response
transcript with Copy, diagnostics, and product/runtime lane telemetry. Useful
commands for delayed controls or camera jumps:

- `engine.renderer`: compact renderer timing, pacing, canvas and resources.
- `engine.renderer.detail`: renderer admission, callback and cadence detail.
- `engine.renderer.presentation`: submitted presentation revisions and camera.
- `engine.renderer.status`: metrics visibility and the latest renderer facts.

Compare renderer submission/pacing with the console's **Product/runtime lane**
input admission latency, input queue age, worker phases and in-flight operation.
The diagnostics log can reveal browser degradation or attachment recovery.
Smooth-looking animation alone does not establish timely input admission.
Unavailable GPU facts remain unavailable; software-browser measurements should
not be interpreted as measurements of the player's GPU.

Rifles only mounts `mountLiveDebugPanel` and `mountRendererMetricsWidget` and
uses the packaged `createLiveDebugHttpTransport`. Engine owns command semantics,
diagnostic collection, polling and HTTP routes. Closing/remounting the UI
releases its requests and widgets. The console is separate from the expedition
panel's gameplay-key guard, so typing commands cannot trigger game actions.
Renderer visibility follows Engine state, including console show/hide commands.

This adds investigation tools; it does not establish the cause or resolution of
the reported Brave input delays and transient camera jumps.

## Verification

`bash scripts/check.sh` passed (UI typecheck, solution build, game/procgen
checks, artifact self-check and CoreCLR staging); the final layout adjustment
also passed UI typechecking. In the served browser, DOM click/keyboard actions
opened the Engine console, executed `engine.renderer`, and showed renderer
metrics alongside its response. Original evidence:
[console and metrics](evidence/debug-tools/console-and-metrics.png).
The test used the crew-services headless Chromium backend. Initial raw pointer
clicks did not activate the toolbar; successful DOM-targeted activation is
recorded separately and is not proof that the reported Brave input fault is
fixed.

Closing/reopening the console and hiding metrics were also verified; a second
`engine.renderer.status` command succeeded after remount. See
[remounted console](evidence/debug-tools/remounted-console.png). Both owned
playtest sessions were stopped afterward.

## C# phase profiling

`rifles.profile.start` clears and enables a bounded C# phase capture.
`rifles.profile.read` returns mean, p50, p95 and maximum milliseconds per phase;
`rifles.profile.stop` freezes and returns it. Collection is off by default and
retains at most 2,048 samples per phase. These commands do not change simulation
or presentation cadence. `TotalUpdate` includes the other phases: do not add it
to them. Phases include synchronous Engine calls made from C#, but exclude
post-callback output conversion, network delivery and browser execution.

For the idle entrance/patrol scenario on 2026-09-13, the first 1,854 sampled
updates averaged 1.35 ms total (p95 1.95 ms). UI projection averaged 0.69 ms,
simulation 0.10 ms, camera/light 0.18 ms, feature focus 0.21 ms and appearance
publication 0.17 ms. A prior ten-second process sample showed the game worker
using 13% of one CPU core and the host 0.3%, with the simulation at 60 Hz.
The final retained capture is [here](evidence/cpu-profile/csharp-phase-profile.json);
the [process/Engine samples](evidence/cpu-profile/process-and-engine-samples.json)
provide the underlying telemetry. Maxima and means describe that sample window,
not all possible combat or inventory workloads.

The browser had not supplied a new renderer snapshot after the profiling build
was loaded, so this phase capture does not by itself reproduce the user's
browser failure. It identifies the largest C# cost without attributing the
remote browser's 100–170 ms submission intervals to it.

`rifles.profile.ui false` freezes only the game HUD for diagnostic isolation;
`rifles.profile.ui true` restores it. The default is enabled. This deliberately
leaves stale HUD state and must not be left disabled after a test.

Subsequent testing with the user's affected Brave session localized the slowdown
to full-rate HUD publication; see [the investigation](hud-performance.md).
Normal HUD refresh timing is authored in `content/tuning/hud.json`. Simulation,
camera and world presentation retain their original Engine-driven cadence.

## Expedition intent

`rifles.expedition.read` returns the current resolved floor graphs, roles,
connectors and identity. It reads the plan used by the expedition save and does
not regenerate or travel. See [milestone 7](milestone-7.md) for current scope.

`rifles.floor.read` returns the played floor's resolved room functions, cells,
thresholds and geometry identity. Use it to inspect room fit and reproduce
layout issues; this is not a visibility or current-frame observation.

## Generated-floor inspection

`rifles.floor.read` returns the current saved geometry, routes, height edges and
architecture. `rifles.floor.population` returns resolved feature and population
facts. `rifles.floor.validate` checks the initial key/counterweight/handle
progression model; it does not solve combat or move the party.
`rifles.floor.navigation 20000` compares a bounded set of actual Engine step
admissions with floor cells, heights and current closed barriers. A partial
budget reports `Complete: false`; it must not be treated as a complete audit.
See `docs/milestone-7.md` for offline JSON/SVG exports and evidence limits.

## Browser-local UI timing

The Debug toolbar's **UI timing** button samples this browser's product DOM
callback and delivery intervals. Click again to refresh the readout. Where the
browser supports Long Animation Frames, it also lists the five longest frames,
script locations, forced layout time and frame age. These are local observations;
they do not use the shared renderer-metrics snapshot. Large old startup samples
must not be mistaken for a current per-frame cost.

The callback timer excludes Engine decoding and later layout/paint/GPU work.
An increasing callback age can mean no projected state changed (including a
paused run), or stalled delivery; inspect host diagnostics and actual game
response before deciding which. Identical full projections are suppressed.

If changed UI files are staged but a normal refresh still displays old controls,
read `/product-ui/debug.js` from the demo URL. Restart the broker-owned session
with `den-serve restart rusty-rifles -repo /absolute/path/to/rusty-rifles` when
the host still serves old modules; do not launch a competing host.
