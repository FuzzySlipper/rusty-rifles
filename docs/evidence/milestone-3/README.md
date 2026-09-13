# Milestone 3 verification — 2026-09-13

`bash scripts/check.sh` passes: Release build, UI typecheck, procgen/game checks,
artifact/tool checks and CoreCLR staging. See [check.log](check.log). The focused
inventory checks use the actual paired Engine candidate implementation and cover
conservation, split/merge, stale/full rejection, atomic equipment displacement,
restored identities/equipment, party formation/dead eligibility and placement
across seeds 0, 1, 29 and 83. An independent bounded source/reuse review found no
remaining consequential defect within its reviewed scope.

## Browser interaction

Profile `rusty-rifles`, headless Chromium through crew-services. Session
`04ef3edd-0e45-46b1-b869-c9e7f0c903eb`, slot-2. Scripts and original PNG copies are
indexed by [captures.json](captures.json). These scripts use DOM-assisted
keyboard activation of actual controls, plus ordinary W/S/D/E movement. The
browser uses the host's software-rendering resolution policy (320×180 backing).

Observed:

- Blade's tonic healed Warden from 28 to 40; tonic quantity changed from 2 to 1.
- Entered ammunition quantity 3 remained across projection updates; Warden's 8
  split into Warden 5 and Blade 3.
- Mender's cordial restored Seeker from 7 to 15 resource. An initial overly fast
  script raced projection delivery; subsequent operations waited for readback.
- The floor key transferred into a pack. Out-of-reach use did not unlock the
  gate; approaching its handle allowed use, retaining the reusable key.
- Opened the crate and placed its iron weight on the plate: weight became 50/50.
- Equipping Seeker's rifle onto Blade transferred it and retained displaced gear.
- Save/restart/load restored position (98,99), Warden 40, Seeker 15 and the
  plate's placed weight. Restart visibly reset the starting condition first.
- Facing east at (99,98), the lever opened the unlocked weighted gate. The party
  entered (101,98). After stepping back and switching off, forward movement was
  blocked at (100,98), with the closed door visible.

A final browser session `ddbab32e-bed1-46ae-9565-b472572b14ae`, slot-1, used native
absolute pointer clicks (no semantic selection) to open Inventory and transfer
Warden's rifle to Blade. [Before](browserNativeClick.png) and
[after](browserNativeTransfer.png) show exact ownership and capacity changes.
Both browser sessions were stopped; receipts reported browser closed/released.

## Remote GPU

Profile `rusty-rifles-gpu`, Wolf/Gamescope/Firefox on the remote GPU, session
`7907341c-c030-410b-816c-3ea841c3b024`, slot-2. The original captures show
[the initial room](gpuFinalInitial.png) and [the room after native S movement](gpuFinalNative.png).
The visible pose changed from (98,98) to (98,99); textures and directional sprites
rendered clearly. This is remote-stream evidence, not a GPU-completion or
performance-isolation measurement. A second independent session occupied the
other slot for part of this check.

Native pointer clicks did not visibly open Inventory in Wolf; that outcome is
uncertain. The same coordinates worked in the final browser check. This pass
establishes remote rendering and keyboard movement, not remote pointer acceptance.
Cleanup reported released, local capture stopped and no errors.

## Limits and corrections

The first GPU pass exposed a page-growing inventory overlay; the UI is now a
fixed, bounded viewport overlay with a scrolling pack region. Source review
also corrected partial-drag quantity capture, cancellation on internal focus
changes, and cancelled intents being resurrected from DataTransfer.

The installed playtest controls expose point/click/keyboard but no held-pointer
drag gesture. Drag/drop is implemented and source-checked, including cancellation
and captured revisions/quantity; a physical drag gesture remains unverified.
The click alternative has both assisted and ordinary-pointer runtime evidence.

Dynamic-light visual acceptance remains B05 / Engine #8256. This milestone uses
the existing Engine pair and introduces no shader/renderer workaround. Gate and
plate materials are intentionally simple; item art uses the generated atlas.
