# Engine integration

The SDK/runtime pair is pinned only by `RustyEnginePackageVersion` in
`Directory.Build.props`. Use `rusty install`, `rusty status`, `rusty update` and
`rusty dev`; products do not depend on an adjacent Engine source checkout.
CoreCLR is the ordinary loader. NativeAOT is an explicit release check.

## Renderer and host

Engine's native wgpu renderer draws the world and streams frames to the
browser; the browser hosts the DOM companion. Engine also has a native window
surface. Stream and window are presentation surfaces of the native renderer,
not parallel product renderers.
A root-page health check sees the generic `Rusty Product Host` shell; the product
title arrives after mounting. The serve manifest therefore probes `/` using
that shell identity, rather than polling an uncached product JavaScript module.
The broker command includes the usual user pnpm/npm binary directories so a
service launch can rebuild the UI, even when it does not inherit a login shell.

Lit billboard sprites, directional voxel textures and dynamic room/spell lights
are owned by Engine. When moving the pair, evaluate sprite fog/shadows,
MSAA/encoding and CSS-pixel viewport scaling in actual scenes; compiling alone
does not prove visual parity.
Direct point lighting can leave away-facing corridor and prop surfaces black.
The authored ambient fill in `tuning/appearance.json` keeps their textures
readable; the room-light comparison toggle switches that fill and fixed lights
together. Rifles opts into Engine scene shadows for the animated skeleton and
its hand-parented musket. Mesh import, textured materials, animation, named joint
attachments, shadow maps and muzzle particles remain Engine-owned.
Native device audio is the default and uses Kira's linear attenuation. A browser
connection alone does not prove that the host device is audible to the remote
observer.

The SDK supplies `RustyEngineProductUiTypes`, the required UI port and packaged
live-debug declarations. `scripts/build-ui.mjs` typechecks against these files.
The product declares UI source/build/input dependencies in its project; Engine's
single build graph compiles the UI and stages it. Atlas copies run before
`BuildRustyEngineProductUi`. Generated UI and SDK output are ignored.

## Input and inspection

Engine delivers pressed and held mappings and owns focus, input claims and
lifecycle suspension. Queued input is processed before live-debug requests;
focus loss clears holds, and lifecycle-paused input is dropped. Rifles still
rejects movement during action recovery, charge or player pause. A delivered key
must be distinguished from mapped intent, admission and committed cell movement.
Use Engine's current `control/claim` and playtest assist controls for unattended
input, and retain before/after product observations.

Rifles registers packaged `PlaytestDebugModule`, `EntityStoreDebugModule` and
`InteractionDebugModule`. Ordinary world use and assisted `interaction.use` go
through the same `WorldInteraction` owner. Target-ID assistance removes reticle
precision only; it does not grant reach, availability or stale-revision bypass.
Observe actions and their live durations through `playtest.*`, not a browser loop.

## Boundary decisions

`MovementGrid` supplies Rifles' cell/slot/faction reservation policy over Engine
step admission. It does not duplicate Engine paths or spatial queries: a blobber
party's logical source, shared crowd slots and destination commitment are game
rules. Engine owns navigation search and footprint step admission.

`DungeonScene.SetDoor` publishes a navigation replacement without closed-door cells.
`NextStep` supplies a temporary traversal overlay for current actor occupancy,
excluding door cells because they are already absent from navigation. Replacing
that overlay per decision reflects the current mover's exclusions. The query
clears it in a `finally` block: Engine retains published overlays and applies
them to ordinary step admission too. A mover-specific occupancy overlay must
not leak into the party or the next actor's admission. This is not a
second pathfinder: dynamic doors use the published navigation replacement.

Collider arrays supplied to `Spatial.CastSegment` are the safe packaged SDK's
supported dynamic-body contract. They describe authored combat hit bodies, not a
second spatial implementation. Engine performs the cast against these bodies
and retained static world geometry. Caching them would require invalidation for
motion, deaths and sizes; do that only when measurement supports it.

Rifles deliberately uses explicit `EnemyBrain` transitions for patrol, memory,
search, retreat and rifle positioning. Engine StateMachine would supply a generic
mechanism but would not remove this game policy; no extra wrapper is justified.
Likewise direct `Spatial.CastSegment` uses the same authoritative world/body facts
for shot and sight rules without duplicating Engine's spatial mechanism.

Engine inventory/equipment assignments are authoritative. Character equipment
contributions are derived committed modifiers; `WeaponState` carries only musket
state keyed by the unique item. Transfer/equip/consume paths reconcile these
owners rather than maintaining another inventory ledger. Presentation revisions
protect stale UI commands at the product boundary.

Engine owns resources and persistence primitives; `ActiveFloor` owns product
resource lifetime. Clear published appearance references before disposing a floor.
Save/load meaning, authored admission and one-pass construction are described in
`gameplay-design.md`. Store pair adoption checks, captures and integration
investigations in Den.

Engine owns the persistence container and decoder. Rifles owns payload meaning
and admission; it does not parse Engine storage files or change container headers
to implement compatibility. Preserve an unreadable development file outside the
active save key before starting a fresh store. Verify fresh-store save/load
separately from old-file compatibility, and record failures in Den rather than
catching every exception to hide an upstream fault.

The SDK-derived dev watch paths include the product project, UI source and
content, and every project it references, so `Rifles.Procgen` edits rebuild and
replace the runtime.

A full dev restage resets Engine time to realtime, releases input claims and
clears observer-camera/drawing overrides. The attached page follows the new
runtime's frame sequence automatically. An unattended playtest must discover
the fresh binding and reselect action-driven time before advancing it. A brief
HTTP 503 during the swap means no runtime is serving; a refused debug command
returns HTTP 422. Keep final visual checks on a stable source revision.

## Native window output

Run `rusty dev --project src/Rifles.Game/Rifles.Game.csproj --output window`
for one windowed launch, or set `RustyEngineProductRenderOutput` to `window` in
the project; the first run installs the pair's desktop pack. Select output through
the CLI or project property; `RUSTY_RENDER_OUTPUT` is not used.
`rusty dev --help` in this repository shows the pinned pair's options.
Stream serving uses ordinary `rusty dev` from the manifest.
