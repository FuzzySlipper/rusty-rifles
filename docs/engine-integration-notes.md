# Engine integration

The SDK/runtime pair is pinned only by `RustyEnginePackageVersion` in
`Directory.Build.props`. Use `rusty install`, `rusty status`, `rusty update` and
`rusty dev`; products do not depend on an adjacent Engine source checkout.
CoreCLR is the ordinary loader. NativeAOT is an explicit release check.

## Renderer and host changes

Engine removed the former Three.js browser renderer. The native wgpu renderer
now draws the world and streams frames to the browser; the browser hosts the
DOM companion. Engine also has a native window surface. Stream and window are
presentation surfaces of the native renderer, not parallel product renderers.
A root-page health check sees the generic `Rusty Product Host` shell; the product
title arrives after mounting. The serve manifest therefore probes `/` using
that shell identity, rather than polling an uncached product JavaScript module.
The broker command includes the usual user pnpm/npm binary directories so a
service launch can rebuild the UI, even when it does not inherit a login shell.

Lit billboard sprites, directional voxel textures and dynamic room/spell lights
are still owned by Engine. Renderer differences include sprite fog/shadow
limitations, MSAA/encoding edges and CSS-pixel viewport scaling. Evaluate the
actual scenes when moving the pair; compiling alone does not prove visual parity.
Some corridor and prop side faces appear completely black in native captures.
Rifles #8992 tracks the bounded material/lighting assessment; the cause has not
been established as an Engine regression, and full visual parity is not claimed.
Native device audio is the default and uses Kira's linear attenuation, replacing
the earlier Web Audio inverse falloff. A browser connection alone does not prove
that the host device is audible to the remote observer.

The SDK supplies `RustyEngineProductUiTypes`, the required UI port and packaged
live-debug declarations. `scripts/build-ui.sh` typechecks against these files.
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
input, and retain before/after product observations. The old Engine input issue
#8257 was cancelled; it is not a pending dependency. Rifles #8452 is the historical
one-action symptom and its live retest belongs in campaign #8939.

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
second pathfinder or a missing dynamic-door mechanism. The review's proposed
upstream door gap was not confirmed against the current source and SDK.

Collider arrays supplied to `Spatial.CastSegment` are the safe packaged SDK's
supported dynamic-body contract. They describe authored combat hit bodies, not a
second spatial implementation. Engine performs the cast against these bodies
and retained static world geometry. Caching them would require invalidation for
motion, deaths and sizes; do that only when measurement supports it. No missing
safe collider-registration contract was established by this campaign.

Rifles deliberately uses explicit `EnemyBrain` transitions for patrol, memory,
search, retreat and rifle positioning. Engine StateMachine would supply a generic
mechanism but would not remove this game policy; no extra wrapper is justified.
Likewise direct `Spatial.CastSegment` uses the same authoritative world/body facts
for shot and sight rules. Adopting Perception is a future policy integration choice,
not evidence that the present spatial call is a local Engine replacement.

Engine inventory/equipment assignments are authoritative. Character equipment
contributions are derived committed modifiers; `WeaponState` carries only musket
state keyed by the unique item. Transfer/equip/consume paths reconcile these
owners rather than maintaining another inventory ledger. Presentation revisions
protect stale UI commands at the product boundary.

Engine owns resources and persistence primitives; `ActiveFloor` owns product
resource lifetime. Clear published appearance references before disposing a floor.
Save/load meaning, authored admission and one-pass construction are described in
`gameplay-design.md`. Den campaign #8937 records pair adoption checks, visible
captures and remaining integration friction; do not copy version strings or
historical measurements into this document.

Engine's ordinary persistence container changed from the schema-bearing `RSP1`
layout to `RSP2`. Engine deliberately supplies no automatic migration for these
development files. Preserve an old file outside the active save key before
starting fresh; converting its payload also requires the product's admission
rules, rather than changing a header. Restoring an old container to the active
key makes both loading and replacing it fail on the current runtime.

The current native persistence boundary reduces an unreadable existing container
to ABI status zero. The safe SDK throws and CoreCLR faults instead of returning
an actionable storage rejection. Engine #8991 tracks that error contract; Rifles
does not parse Engine storage files or catch every exception to hide the fault.
Fresh-store save/load verification is separate from old-file compatibility.

The current SDK-derived dev watch paths include the product project, UI source
and content, but omit Rifles' ordinary `Rifles.Procgen` project reference. Changes
to that library require an explicit rebuild and restart of the owned host until
the upstream watch-discovery gap is resolved. This campaign filed the narrow
Engine #8984; there is no downstream watcher replacement.

## Pinned launcher compatibility

Use the help shipped inside the installed pair when the global CLI advertises
newer arguments. This pair selects native window output through
`RUSTY_RENDER_OUTPUT=window`. A newer global launcher can delegate an explicit
ordinary runtime pack, preventing this pair from selecting its desktop pack.
Engine #8989 tracks that compatibility failure. For a window inspection until
it is resolved, use the published pair's `runtime-pack/bin/rusty dev` with the
same project arguments and environment; it installs the matching desktop pack.
Find that installed pair with `rusty status`; do not hard-code a cache version or
build a replacement host from Engine source. Stream serving uses ordinary
`rusty dev` from the manifest.
