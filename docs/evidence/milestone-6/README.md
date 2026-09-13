# Milestone 6 runtime evidence

`browser-index.json` indexes unmodified screenshots by original capture, script,
session, path and timestamp. These runs used ordinary product controls through
crew-services' headless browser profile. The headless backing canvas was 320×180
scaled to 1280×720: use these captures for behavior/UI evidence, not GPU image
quality. No page errors were reported in the inspected browser session.

Observed sequence:

- Mend healed Warden 28→40 and charged Mender 20→16 resource. Mend was assigned
  to a quick slot before the save.
- Ward charged Warden three resource and survived pause/save/load with its
  remaining duration. The first rest attempt used Warden, who had no tonic;
  `partial-rest-saved.png` from that attempt is a rejected start, not a rest pass.
- Mender's rest then saved at 6.33 seconds with three tonics still present.
  Load restored that countdown while paused. Resuming completed recovery,
  consumed one tonic (three→two), and left zero pending rest time.
- The existing inventory controls supplied the key and plate weight. Hand
  activated the lever and opened the gate while charging three resource.
- A rifle shot and Spark killed the raider; the party received eight XP.
  Bind hit the runner, and Blight plus Spark killed it. Each member then had
  sixteen XP and one unspent advancement point.
- Warden selected Powder savant, learned Burst, assigned it to quick slot two,
  and cast it. The musketeer changed 28→21 vitality, Warden spent four resource
  after the advancement discount, and the projectile settled once. This scene
  did not establish simultaneous damage to multiple area victims.
- Seeker cast Lantern and saved its active party condition at 29.68 seconds.
  That paused save was retained for the separate GPU observation.

Read-only inspection of Engine's saved product envelope corroborated vitality,
resource, item quantities, cast/flight completion, conditions, claimed spawn
rewards and advancement choices. No save was edited or injected. Browser
sessions `21465397-4339-4c64-b191-52165dd5b2ce` and
`9e9209bf-edae-4e0e-9d4c-a8416831e1be` were both stopped cleanly.

One combat script initially used an ambiguous Spark text selector (two buttons)
and stopped early; the saved paused encounter was loaded and the corrected
explicit selector completed the sequence. This was a harness-selector failure.

The independent GPU report records its own observations and limitations. The
runs do not claim a full catalogue playthrough, a revival interaction, multi-victim
area occlusion coverage, or the older native held-drag inventory acceptance.
