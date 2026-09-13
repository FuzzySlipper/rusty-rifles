# GPU input and bundle rendering report

Date: 2026-09-13  
Profile: `rusty-rifles-gpu`  
Backend: Wolf native remote GPU browser  
Session: `77b41612-e9e0-43ac-9de6-fe23f75e3273`  
URL: `http://192.168.1.22:37300/`

## Outcome

`pass` for the bounded native-input mission. The current build accepted native
keyboard input after the normal canvas focus click. Q/E turns and W/S grid
movement were visible in the game header, and P paused and resumed the
expedition. The Wolf target does not expose pointer-lock readback, so lock state
itself remains unknown. This run uses the Wolf browser path rather than Brave;
it confirms the current GPU path but does not independently reproduce the
reported Brave session.

## Neutral observations

After about three seconds of initial loading, the ready scene showed
`Exploring · North · (98,98) · Seed 29` with the entrance gate closed. The
visible room had painted brick walls, a tiled floor, and a textured ceiling.
Directional still enemy sprites were visible in the room and were shaded by the
scene lighting. The left overlay contained combat, party, and spell controls.

Moving backward revealed a lantern and a large gun or cover prop in the world.
Turning and stepping changed the camera view while retaining the same textured
voxel-style room materials. No item inventory interaction was attempted in this
bounded input run.

## Native actions and visible state

- Canvas focus click followed by Q for 120 ms changed `North · (98,98)` to
  `West · (98,98)`.
- E for 120 ms changed `West · (98,98)` back to `North · (98,98)`.
- S for 250 ms changed `North · (98,98)` to `North · (98,100)`.
- Escape followed by E changed `North · (98,100)` to `East · (98,100)`.
- A canvas refocus click followed by E changed `East · (98,100)` to
  `South · (98,100)`.
- P changed the overlay header to `Paused · South · (98,100)`. A second P
  returned it to active `Exploring · South · (98,100)`.
- W while facing South advanced the visible cell to `(98,101)`.
- A and D were attempted at `(98,101)` in the narrow corridor. Both left the
  visible cell unchanged at `(98,101)`, so lateral movement was not available
  from that geometry. This is a blocked-space observation rather than an input
  failure.

The Wolf service reported every native input batch as delivered with no release
errors. Game consumption is not exposed by the service; the state transitions
above are the direct visible evidence of consumption.

## Evidence

These are exact copies of the original Wolf captures retained with this report:

- [Initial ready scene](gpu-initial-ready.png) — capture `8a1f7988-8794-4097-a79d-22a6d450184f`, artifact `8269e243-a44f-4434-948e-01bdc725d908`.
- [After Q turn](gpu-after-q-turn.png) — capture `381fbccc-9548-44c1-a2f0-09706444fa11`, artifact `7089372d-4ed0-4b10-b7ca-09e1741dcbd4`.
- [After E turn](gpu-after-e-turn.png) — capture `26025aaf-b8bf-4769-973f-d6ab3659f9f5`, artifact `17f0d340-d9fa-410f-b165-01e98919e443`.
- [After S step](gpu-after-s-backstep.png) — capture `e4bdb576-ea58-4893-bf5d-cf2e46557617`, artifact `e4dc0049-f737-45f1-b8b0-54fcae3f6437`.
- [After Escape and E](gpu-after-escape-e.png) — capture `a4bfa6b1-712e-480c-9cfd-a0a8158b760b`, artifact `3de36d0e-97b9-42b6-851a-0bdc76c6e3d9`.
- [After refocus and E](gpu-after-refocus-e.png) — capture `b17fb0f8-3b6f-44ed-b3c2-d90be0e66217`, artifact `df1d1e9e-d935-4da9-a572-144f2a70ecbe`.
- [Paused](gpu-paused.png) — capture `493adb59-a821-46a8-b89e-36e1057beca8`, artifact `b3f4308a-4007-47e8-b71b-ac0d89d4b759`.
- [Resumed](gpu-resumed.png) — capture `528cb987-d26b-4463-82c4-c4644ef85d5c`, artifact `a9d0b61f-012b-4bee-beed-e518674e2132`.
- [After W step](gpu-after-w-step.png) — capture `dd4f704c-228b-4da4-a14a-c36bfff0a13f`, artifact `996c0478-f35d-491f-8616-9e36c97594f2`.
- [After A attempt](gpu-after-a-step.png) — capture `c3fbd3a3-0798-49ac-a32e-4515a601c152`, artifact `466a723e-344f-4357-8fe9-215790365f66`.
- [After D attempt](gpu-after-d-step.png) — capture `185148f7-2bf2-4351-8bfd-1cbb8ada0b19`, artifact `9db7f020-b9d2-40a0-b110-b1e8fca3db9f`.

Original session artifacts and journals remain under
`/home/dev/dsh-crew/experiments/wolf-den-srv/controller/state/77b41612-e9e0-43ac-9de6-fe23f75e3273/`.

Cleanup: the owned session was stopped after these captures; the cleanup receipt
should be checked by the parent alongside this report.
