# Engine integration notes for campaign planning

Checked 2026-09-13 against the **installed** `Rusty.Engine`
`0.1.0-dev.2e4255bd3ad5` assembly, not just the newer sibling Engine checkout.
These are discovery results, not completed integration tests. The game's
existing build checks cover its current narrow voxel/navigation/party usage.

| Campaign need | Surface present in the installed SDK | What still needs a real caller/check |
| --- | --- | --- |
| Shared-grid travel and enemy routing | `ISpatialService.ReplaceNavigation`, `EvaluateNavigationStep`, `RequestNavigationPath`, `RequestWeightedNavigationPath`, `ReplaceNavigationTraversal` | Size-dependent passability, per-query occupancy policy, doors, path-result ownership, and bounded replanning under congestion |
| Firing lanes and awareness | `ISpatialService.CastRay`, `IPerceptionService` | Consistent actor/feature occlusion, partial-cell enemy footprints, ally blocking and perception policy |
| Item conservation and equipment | `Mechanics.InventoryWorld`, `InventoryWorldCandidate`, `EquipmentService`, `ItemState` | Adapt inventory owners to characters, containers and world placements; confirm grouped transfer/equip/use settlement and save representation |
| Timed behavior | Engine update facts; `Application.SimulationScheduler` | Product action phases, cancellation/interruption and save/resume; do not introduce a competing timer loop |
| Textured voxel rooms | `VoxelScenePresentation.ProjectSceneDirectional`, material bindings, `Graphics.CreateMaterial` | Image admission, texture repetition/scale, material-face selection and retained resource lifecycle in the chosen art treatment |
| Directional still enemies and prop billboards | `Graphics.CreateSprite`, atlas APIs, `ReplaceSprite`, `ReadSprite` | Ground anchor, facing/identity consistency, world sizing, alpha/depth/occlusion, individual selection and crowded-cell rendering |
| Item/world targeting | `Interaction.InteractionFocus.Update`, `Observe`, `Revalidate` | Reach/visibility and permitted use/drop/throw semantics; UI commands must revalidate changing targets |
| Authored tuning and saves | `Content.ReadBytes`, `Persistence.ProductStateStore<T>.Save/Load` | Typed schemas and validation; a complete product snapshot including live actions and resolved floors |

Sprite lighting is a campaign requirement to verify in the actual host. Begin
with a simple Engine-supported dynamic response to level lights over color/alpha
art; generated normal maps are not required. Selective illustrated form shading
can remain, but strong painted lights must not fight the runtime light direction.
If the paired SDK lacks the necessary control, scope the owning Engine addition
rather than introducing a downstream renderer. No lighting implementation or
visual acceptance is claimed by these planning notes.

`InventoryWorld.Prepare` produces a candidate with `Validate` and `Publish`;
inspect that contract before writing product-owned rollback or duplicate
inventory bookkeeping. Game policy still owns which operations belong together.
An Engine service call is not automatically a transaction with every other
service or product mutation in the callback.

Navigation traversal overlays are session-owned APIs. Do not assume one global
overlay can simultaneously represent every enemy size and reservation state,
or that rejected movement leaves all retained path output unchanged. Settle
the supported query/admission approach before implementing crowded tactical AI.
If query-specific filtering or another reusable spatial mechanism is missing,
propose the narrow upstream capability; do not put A* or a shadow navigation
world in Rifles. Product occupancy reservations remain game policy.

The current procgen pipeline's `DungeonGenerator.ValidateBuiltFlow` validates
route continuity, declared item-aware connectivity, artifact binding, and portal
facts. `DungeonScene` currently realizes open walkable cells with perimeter
walls; it does not implement those portal state machines. Campaign validation
must cover actual blocked edges and incidental geometric bypasses after gates,
world interactions, and decoration are realized.

The sibling Engine discovery documents are useful for locating services but
may describe a newer release than Rifles uses:

- `/home/dev/rusty-engine/docs/csharp-capabilities.md`
- `/home/dev/rusty-engine/docs/csharp-sdk.md`
- `/home/dev/rusty-engine/csharp/Rusty.Engine/Mechanics/Inventory.cs`
- `/home/dev/rusty-engine/csharp/Rusty.Engine/Mechanics/Equipment.cs`

Verify the installed SDK and paired runtime when implementing each boundary.
Upgrade them together when an agreed capability requires it. This planning
pass changes neither the Engine nor game runtime.
