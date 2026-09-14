# Final expedition acceptance

Build: `718a05fdefed0d4bb7b1dcd45bff9ecd6f1e6a37`, paired Engine
`0.1.0-dev.03ac310b95c2`. Normal expedition seed 29.

The root operated ordinary UI and keyboard controls using the maintained
`rusty-rifles` browser profile. DOM selectors and read-only floor/cell facts
assisted navigation and targeting. No gameplay debug mutations were used.
This is assisted functional play, not an unaided map-discovery study.
The browser rendered through SwiftShader at 320×180 backing / 1280×720 CSS;
these captures do not establish GPU quality or performance.

## Retained checkpoints

| Behavior | Script receipt |
| --- | --- |
| Raised passage and first-floor melee | `21779f6a-1886-4eb3-aea9-fda617bcbc6f` |
| Mend heals Warden and consumes Mender resource | `8db98f1d-a42c-4454-ab06-4c03a663c536` |
| Stores key and counterweight picked up; save confirmed | `80bd6fe1-6838-4901-b9da-390e238c4849` |
| Browser stopped, broker runtime restarted, saved Stores run restored at (99,112) with party/items | `3bcf8bda-adf5-4064-b7da-d38c59603eec` |
| Ordinary load after a failed encounter; rest and rifle preparation | `885487ab-34fb-4e03-bdef-5ee8a3d791ea` |
| Stores rifle defeats marksman | `139a5a53-9869-4fe9-bacb-a5fd7b9c9d99` |
| Stores rifle defeats musketeer | `0d31776d-96dc-488e-965e-ccb3cea474d1` |
| All four living members descend to Inner Redoubt | `32582d14-fba6-46f0-9e0f-95198550a12d` |
| Final-floor rifle defeats sapper | `6215edf1-f7e8-478b-99ac-2675c25b5022` |
| Blade defeats large bulwark | `45e6737e-1522-4d6c-995a-239dbca7fe87` |
| Spark and melee defeat raider/runner | `df2cfed2-3516-498f-8ba7-2e11aa3f57a3` |
| Counterweight lands on plate; keyed gate opens; save confirmed | `51eb9e44-3769-47a0-84cb-8193ec928732` |
| Final musketeer and marksman defeated | `6395a73c-abab-4bde-afab-1a6e0754282f`, `9dcb0c9d-6e4e-419e-8681-07a1cdbdb845` |

Receipts are in `/home/agent/.local/state/crew-playtest/scripts/<id>/events.jsonl`.
Initial session `ce475e4b-af97-44de-b356-54ebe83bb7bc` was stopped for quit/resume;
subsequent session `797d635f-1cb3-4859-890f-ffb6f09b2a22` continued the run.

The first Stores approach failed under enemy fire. Revival was correctly
unavailable while threats remained; the run recovered through the saved game.
Some route/target attempts stopped at blocked cells or unavailable targets.
Those are not recorded as successful attacks or movement. At 720p the expanded
inventory overlaps lower combat controls; collapsing it retains the selected
item and exposes throw controls. This remains a prototype layout limitation.

## Completion

Script `3d000bf1-a456-4ef4-9931-3fe7d661a943` completed the expedition at
Inner Redoubt (113,85), with four living members. Each member held 118 XP and
7 unspent advancement points. Its immediate post-restart load attempt did not
restore before the capture; a settled ordinary Load in
`d2258a65-7b07-4a15-8280-7f29708e2834` restored the completed ending and the same
rewards, without duplicating them.

Original captures copied without modification:
[quit/resume](quit-resume.png), [opened final gate](final-gate.png),
[completed expedition](complete.png), [completed save restored](completed-load.png).

## Different seed

Ordinary New expedition selected seed 314159. The observed saved profile was
**Normal**, despite an attempted keyboard selection of Hard; no Hard-profile
claim is made. Its arrival layout has a western melee route and an eastern
ranged side area, unlike the first run. In `b90758a3-acf4-4812-a356-0cda56311dfa`
the party navigated the southern approach to (90,98).
`d83ecc44-b0da-4d6d-be03-8c76a5140f11` shot the small runner;
`34099fd1-1328-4080-bf76-e2b3a3e4857b` defeated the raider with Blade.
`8bb1b0a1-60bb-492e-b000-3186496ee5cf` descended into this seed's Stores,
which contains six mixed-size ranged/melee archetypes including the bulwark.
This is a second-seed play check, not a second completed expedition.
