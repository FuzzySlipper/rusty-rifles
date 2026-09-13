# Milestone 4 GPU playtest

Date: 2026-09-13  
Profile: `rusty-rifles-gpu` (`wolf`)  
Configured model identity: `gpt-5.6-luna`  
Service: `http://127.0.0.1:48200`  
Session: `37d40156-a676-4bbe-a963-79f5bf13965f`  
Target: `http://192.168.1.22:37300/`  
Operational outcome: **uncertain**

The restored original frame shows a 1280x720 lit brick corridor with textured walls, corridor depth, a foreground lever, and directional character art. The HUD reports a paused East expedition at `(99,98)`, an unlocked/open gate, Warden selected with a long rifle at `1/7`, Garrison raider `32/32`, and Garrison musketeer `28/28`.

The native sequence produced these visible state changes:

1. Resuming brought the approaching raider close to the party and reduced Warden from `28/40` to `27/40`.
2. Space logged `Garrison raider took 17 damage`, changing the raider to `15/32` and the rifle to `0/7`.
3. T logged `Warden loaded one round`, changing the rifle to `1/6`.
4. The second Space logged `Garrison raider fell`, changing the raider to `0/32`; the musketeer remained `28/28`.

The complete mixed fight and native musketeer target selection remain unverified. A first attempt ended in a visible party defeat while the session continued in real time during an observation gap. The single bounded retry completed the raider kill, but a later Tab+Enter target-selection probe was also made after real-time depletion and showed the party defeated, so it cannot establish musketeer selection behavior. One direct native input request using a 10-second wait was rejected with an HTTP EOF; the supervised program path succeeded after keeping the batch below the service's 10-second limit.

Original captures preserved in this directory:

- [Restored save](gpu-restored.png) — `05afecc5-ce73-4f35-9dfa-2d00668f7872`
- [Approaching raider](gpu-approach.png) — `c9fd5bee-ff7e-4e24-a34f-f2d7ece38418`
- [First shot](gpu-first-shot.png) — `06c46c60-a1cc-4ba9-b1ce-f8621a924f73`
- [After reload](gpu-after-reload.png) — `8eed623c-abb3-442e-a4c1-78ca54d7ed7b`
- [Raider dead](gpu-raider-dead.png) — `1f0d108a-727d-4aab-a739-9c59dbf0e71c`
- [Party defeated limitation](gpu-party-defeated.png) — `d77bea79-2372-40b6-ae4b-207768f6a122`

The indexed originals and journals remain under `/home/dev/dsh-crew/experiments/wolf-den-srv/controller/state/37d40156-a676-4bbe-a963-79f5bf13965f/`. Key supervised script IDs were `caf3208f-9247-4dda-b265-11d39a04cd51` (load), `4d797427-8b16-4f69-9b73-2cc0ff6cda29` (resume), `86963c25-1e45-48c3-8016-9033098b967f` (first shot), `20f15e7c-5fd1-4c62-9a9a-404f0f16f3ed` (bounded retry load), `cf42d627-5668-48b7-a151-c9eb6bf61b41` (shot/reload/shot), and `5f77d413-14b6-49d8-a68c-1b4dd4e10547` (Tab+Enter probe).

Cleanup: `playtest stop` returned `released: true`, `errors: []`, `local_capture_stopped: true`, slot `slot-1`, at `2026-09-13T10:19:44Z`. The MCP `playtest_finish` adapter could not read the CLI-created session record from its separate local state path; the service stop receipt is clean and the evidence remains indexed.
