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
local_capture_stopped=true. A living-party observation follows separately.
