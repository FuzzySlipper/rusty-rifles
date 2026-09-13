# Native GPU observation

The independent playtester used the `rusty-rifles-gpu` Wolf profile, session
`dff578ad-9b3a-42d8-8835-2e691b7fc244`, slot 1. It recovered an early party defeat
with the ordinary Load control, then used short paused observation bursts.

At East (99,98), 48 seconds, the saved scene showed the dead raider, a partially
reloading musketeer and small enemies near the entrance. Native P/S/P controls
moved the party backward to (98,98). The corridor mouth, entrance room and table
were visible; enemy sprites occupied distinct depths with no obvious collapse
or jitter in the captured observations. Foreground clothing was brighter and
distant corridor sprites darker. The UI reported a shot blocked by a friendly
body and a musketeer reload, alongside continuing melee pressure.

This was a bounded room/corridor and lighting observation, not a full encounter
clear, a player attack probe, or comprehensive frame-by-frame motion certification.
The later conservative crowd departure-lane guard is covered by source checks and
the final browser repeat; these GPU images precede that guard. Original pixels
are copied unchanged and indexed in `gpu-captures.json`.

The first attempt used a mismatched manifest-based adapter. Retrying through the
maintained `/home/agent/.local/bin/playtest` CLI succeeded. Cleanup returned
`released: true`, `local_capture_stopped: true`, and an empty error list.
