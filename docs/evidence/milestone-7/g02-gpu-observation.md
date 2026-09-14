# G02 GPU room observation

Crew-services CLI `rusty-rifles-gpu` profile, Wolf backend. Initial session
`ac27c9fc-c318-4f9f-8982-b0abad795e0f` and bounded retry
`208deb9f-0158-4d81-b1df-c7934fc8119e` both showed the same defeated party.
The [original initial capture](g02-gpu-initial.png) shows textured brick walls,
tiled floor, a right wall return/recess and two humanoid sprites. HUD identifies
Guard Hall, North, (99,98), seed 29. This establishes visible entrance rendering,
not navigation into Watch Gallery or Powder Bays.

Native movement/turn commands did not move the already-defeated party. Wolf
has no pointer-lock or game-consumption readback; this is not evidence of a
new input regression. Both sessions stopped with released=true, errors=[],
local_capture_stopped=true. A final browser preparation attempt used session
`4248e025-e7f9-481f-8110-83b7a0b9cde4`. Normal Restart/Pause and coordinate
clicks returned `Post http://127.0.0.1:48200/command: EOF`; captures still showed
the defeated party. No living-party movement acceptance is claimed. The session
closed with browser_closed=true and released=true; no additional GPU session
was started. This last obstacle is a test-service command-channel failure, not
evidence that the product rejected a delivered Restart command.
