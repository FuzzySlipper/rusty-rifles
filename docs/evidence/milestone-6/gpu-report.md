# M6 GPU playtest observation

Mission: Load the supplied saved expedition in the native GPU profile, observe
the paused scene and UI, and preserve original captures. No product or
configuration files were inspected or changed.

Profile: `rusty-rifles-gpu` (`wolf`, native remote GPU)
Broker/product URL: `http://192.168.1.22:37300/`
Candidate serving reference: `2026-09-13T12:17:56.897920442Z` (source frozen)
Configured model identity: `gpt-5.6-luna`; authoritative model metadata was not
available from the playtest/runtime receipts.

Session: `4089e11a-2793-4a32-9510-8991eeba83af`
Slot: `slot-1`
Started: `2026-09-13T12:20:52.032197283Z`
Execution stream: remote Wolf decoded by Moonlight, 1280x720, 30 Hz.

## Neutral observation

The initial frame showed a dark empty canvas with a Rusty Rifles control panel
at upper left. The panel read “Preparing the expedition…” and exposed the
movement, turn, attack, use, save, load, and pause controls. No level geometry
or sprites were visible in that initial frame.

After a native click at `(1100,400)` and VK `76` (`L`), followed by a 10 s
wait, the scene was visibly paused. The status line read `Paused · East ·
(99,98) · 71s · Seed 29`. A first-person brick corridor filled the canvas:
red/orange brick side walls, a beige ceiling, a dark tiled floor, and a
receding corridor opening at center. The weapon and several party/enemy sprites
were visible with different light and shade across foreground and distance.

The visible combat panel showed Garrison raider `0/32` and Powder runner
`0/18`, both marked dead; Garrison musketeer `21/28`, Garrison sapper `24/24`,
Garrison bulwark `60/60`, and Garrison marksman `22/22` remained visible. The
party state lines showed Blade ready, Mender ready, Seeker in `Cast Recovery
0.5s`, and Warden ready with a long rifle. The party health/resource rows
showed Warden `12/40 · 1/12`, Blade `32/32 · 5/8`, Seeker `26/26 · 6/16`, and
Mender `28/28 · 16/20`. The beginning of a lower `Gate — unlocked` line was
visible at the bottom edge, but its full text was clipped by the viewport.

The `Spells & recovery` header remained collapsed after native clicks at the
visible header coordinates `(100,579)` and `(80,578)`. Lantern’s label and
remaining timed-light duration were therefore not directly readable; this is
an observation limit, not evidence that Lantern was absent.

## Operational outcome and acceptance mapping

Operational outcome: **uncertain**.

The saved expedition visibly loaded into the expected paused East `(99,98)`
scene, with the expected dead raider/runner, surviving enemies, and low-health
Warden context. The gate state is only partly legible, and the active Lantern
timer plus complete Seeker selection state were not directly exposed in the
captured UI. The visual lighting and sprite shading are directly observable;
this run does not establish GPU acceleration or synthetic-lighting
effectiveness by itself.

Acceptance mapping for the parent: Load and paused-scene restoration are
visibly supported; exact Lantern duration, complete gate text, and any visual
comparison claim remain unconfirmed.

## Evidence

Original PNGs copied unmodified into this directory:

- [`gpu-initial-preparing.png`](gpu-initial-preparing.png) — start artifact
  `93b71ae8-b8cf-4612-b1ce-1a2ec343a2c1`.
- [`gpu-load-paused.png`](gpu-load-paused.png) — capture
  `8b1d5e6e-bd37-44cb-883e-c82c0a442789`, metadata
  `/home/agent/.local/state/crew-playtest/capture-8b1d5e6e-bd37-44cb-883e-c82c0a442789.json`.
- [`gpu-spells-paused.png`](gpu-spells-paused.png) — capture
  `4130af52-a818-4988-885c-347079d45231`, metadata
  `/home/agent/.local/state/crew-playtest/capture-4130af52-a818-4988-885c-347079d45231.json`.

Source originals remain at
`/home/dev/dsh-crew/experiments/wolf-den-srv/controller/state/4089e11a-2793-4a32-9510-8991eeba83af/`.
Engine presentation receipts were available for the two captures, with runtime
instance `576222`, but reported `hardware_acceleration`, `gpu_completion`,
`frame_correlation`, and `worldReadiness` as unavailable. Resource fallback
counts were zero in those receipts; this is diagnostic context rather than a
GPU acceptance verdict.

Limits: native input delivery was target-reported while game consumption and
pointer-lock state were unknown. The run stayed paused after Load, performed no
combat, and did not run the optional backstep because the low-health Warden
made the bounded observation safer to leave at the restored pause state.

## Cleanup

The owned session was stopped. The service reported it released successfully
with no errors; final session status was `stopped`.
