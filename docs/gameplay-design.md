# Gameplay and ownership

One party occupies one cell and faces one cardinal direction. Six soldiers
screen a fixed central commander. `martial-command-design.md` describes the
party and combat contracts; this document maps behavior to its owning code.

## Domain owners

| Domain | Owning code and contract |
| --- | --- |
| Authored admission | `Content/GameDefinitions`: Engine content reads into typed, validated records. Martial drill definitions load only on the first drill command. |
| Generation | `Rifles.Procgen` creates graph intent and bounded resolved geometry from explicit definitions, independent of Engine, files and clocks. `DungeonFloor` adapts it to the product. |
| Floor lifetime | `Expedition/ActiveFloor` owns scene, movement grid, features, items, combat, magic and appearance lifetime. One `FloorServices` record supplies session services. |
| Party | `PartyState`, `FormationPlanner`, `FormationRules`: commander screening, formation, rest state and timed repositioning. |
| Combat | `RiflesCombat`, `ActionState`, `EnemyBrain`, `ChargeState`: action phases, ordinary orders, target choice, damage and enemy behavior. |
| Equipment | `ItemInventory` commits assignments through Engine inventory/equipment; character equipment modifiers derive from those committed assignments. `WeaponState` stores only unique musket loaded/bayonet state and reconciles transferred weapons by item identity. |
| Magic | `MagicState` owns books and cast policy; Engine owns effects, stats and tracks. Hotbar capacity and ally stats are authored definitions. |
| Generated mechanisms | `GeneratedFeatureState` is the floor's live gate/hazard/plate owner. Combat reads that same owner, including its current revision, at planning and impact. |
| Exploration interactions | `WorldFeatures` contributes the current candidates to Engine `WorldInteraction`; keyboard, UI and `interaction.use` share product use handlers and Engine revalidation. |
| Expedition | Run progress, objectives, difficulty, discovered maps, retained floor identity and retry live in the expedition domain and the product coordinator. |
| Presentation | Engine owns graphics, camera, audio, input and admitted time. `SessionProjection` publishes bounded snapshots; TypeScript lays out and sends intent. |

Significant tuning lives under `content/tuning/` and `content/definitions/`.
`Combat.TargetHalfAngleDegrees` is a half-angle; enemy `SightConeDegrees` is the
full cone. `VisibilityRange` is independent of the currently equipped weapon's
attack range. Dressing offsets, patrol positions and puzzle placement definitions
are authored. A straight passage's two opposing neighbors are a grid geometry
invariant, not a balance value.

## Runtime behavior

Movement reserves a destination and commits the logical cell on completion.
The source cell remains authoritative during transit. The facing is cardinal;
Engine camera interpolation does not change movement admission. Crowds share
size-aware authored slots and bounded Engine paths. One removal helper stops
motion, releases occupancy and detaches a defeated body.

Orders resolve each ready soldier's actual weapon and forward lane. Reloads and
bayonets belong to the unique musket. Formation planning pauses gameplay, and
execution takes authored admitted time. Member identity remains distinct from
formation-position identity: selection, equipment and inventory follow the member
when positions change. C# owns placement legality; the UI displays legal targets
and submits intents. Inventory transfers and formation changes remain distinct
actions with rejection feedback. The commander cannot leave the center;
casualties do not auto-fill gaps. Charge locks movement and its participants.
Shared abilities retain each provider's cost/recovery with one stable owner for
a shared effect. Enemy decisions and action policy consume Engine-admitted time;
there is no browser simulation clock.

Player pause is distinct from Engine lifecycle suspension. Lifecycle callbacks
clear held controls and preserve the player's pause choice. Defeat and load use
the same gameplay pause setter. A loaded floor receives the saved difficulty's
damage multiplier before activation.

Floor objectives are mostly authored graph structure: seeds change physical
rooms, routing, encounters and supplies, rather than inventing unrelated goals.
`DetourLoop` can add a seeded graph variation. Floors retain their identity and
freeze while inactive; the party and its state travel once.

## Saves and admission

The Engine product-state store uses the source-generated `RunSaveJsonContext`.
The incoming run is validated before mounting. `RunCodec.Validate` returns one
`AdmittedFloor` reconstruction; the live floor consumes those exact exploration,
party, inventory and combat objects. Active and retained floors share
`FloorFacts`; door clearance setup shares `FloorFactory.ConfigureDoorClearance`.
No reflection-serialization fingerprint is rehashed at load. Stored generation
identity and resolved geometry are admitted structurally; loading does not
regenerate a floor. Descriptive field additions do not change that identity.

The current schema rejects unknown members and missing non-optional constructor
parameters. Saved enums use explicitly registered generic string converters;
a graph-walk check rejects missing registrations. Missing `SavedPack.Slots` is
invalid. Programming failures propagate; save/load feedback catches expected
invalid data and I/O failures only.

Optional saved fields deliberately mean an absent activity or cosmetic selection:
rest defaults to inactive; formation and charge to no execution; magic to an empty
initial state; flight spell to a physical projectile; exploration slot fields to
exclusive placement; musket shot count to zero; open container to none.
Pack labels are optional cosmetic text. Resolved roster records may omit base
power/defense/resource (zero), starting values (maximum) and commander (false);
commander/formation admission must still accept the resolved roster. `ActionSnapshot`
may omit aim (none), offsets (zero), spell (none), cost/revision (zero), forward
order origin/facing (none) and shared-effect suppression (false); combat admission
still validates the resulting action. Intent notes, catalog tags/sockets/node
restrictions are optional descriptive data. Saved floor architecture has a nullable
constructor parameter but floor admission requires it, so missing architecture
is rejected by name rather than selected as a runtime default.

These are the defaults on the corresponding snapshot records, not compatibility
versions. Required authored numeric/role/spell fields must appear in content;
nullable starting vitality/resource explicitly select the archetype maximum,
and nullable weapon/preset explicitly select no weapon or an unscoped grant.
Content errors identify the owning asset and field.

Persisted meaning includes resolved floors, party stats, equipment, books/effects,
actions, projectiles, generated mechanisms, world items, run progress and maps.
The combat log, selected art treatment, room-light switch and movable comparison
light are session presentation. Enemy navigation diagnostics/path cursor,
movement fairness counters and the map-observation cache are reconstructed;
they are not saved gameplay commitments. Persistence does not promise the same
transient planning order or diagnostic text after load.

UI inventory revisions reject stale presentation commands at the product boundary.
They are not a second Engine ledger or per-frame runtime admission protocol.
Engine inventory transactions use `Prepare`, `Publish` and `Cancel`.

## Checks and inspection

`bash scripts/check.sh` covers two source and two check projects, SDK UI types,
domain checks and CoreCLR staging. Checks collect failures by group. The pure
floor factory and roster projection have product tests. Offline export is
`dotnet run --project tests/Game -c Release -- --export-floors DIRECTORY`;
`docs/generated/floor-bank/` is a labelled generated snapshot.

Build/staging, runtime launch and visible interaction are separate claims.
Keep captures, review results, measurements and progress records in Den.
See `engine-integration-notes.md` for Engine behavior and `debug-tools.md`
for repeatable inspection commands.

### Skeleton musket watch

The `skeleton-musketeer` definition uses the existing ranged enemy brain,
loaded-round and reload rules, windup/commit/recovery timing, sight checks and
hitscan damage. Its regular `musketeer-patrol` spawn uses the rigged mesh rather
than a directional sprite. Other enemy definitions keep their existing art.

`Presentation/MusketeerArt` admits the body and rigid musket through Engine's
animation content service. Engine resolves the exact named
`mixamorig:RightHand` skin joint and retains the child relationship; Rifles
never polls bone transforms. Idle and walking clips loop through Engine
playback; fire poses follow the combat action's phase, and defeat plays once.
Restored corpses sample the final pose. Fresh floor/load teardown clears the
published appearances before releasing animation instances and resources.

`definitions/musketeer.json` owns clip names, body scale, facing correction,
joint-local grip TRS, firing pose and muzzle burst tuning. Shot commits emit
Engine flash/smoke particles at the authored muzzle offset; held Engine time
also holds their age. Effects are transient and are not replayed from saves.
The skeleton and musket retain their GLB textured materials and receive scene
lights. The product opts into Engine scene shadows; authored lantern and room
light settings select the requesting lights. Shadow rendering remains entirely
Engine-owned and applies to the posed body and its attached weapon.

`Presentation/FrontRankArt` represents the living occupants of the three forward
formation positions with `art/mesh/soldier.glb`. They remain presentation of the
single party cell: placement and facing follow its interpolated grid pose, and
formation membership changes at the existing formation commit. Fallen soldiers
are hidden. Only equipped muskets receive the rigid weapon mesh; other equipment
has no mesh representation yet. Idle/walk playback and action-sampled firing use
Engine animation, with the musket attached to `mixamorig:RightHand`.

`definitions/front-rank.json` owns the experiment switch, model scale, spacing,
forward offset, clips and soldier-specific joint-local grip. The hand's local
basis differs from the skeleton's despite sharing joint names, so their grip
rotations differ. `tuning/exploration.json` owns camera height and perspective:
the view stays level, with a modest eye-height lift to see over the full-height
front rank. Reload currently returns to idle; the supplied clips do not provide
a dedicated reload animation. Presentation adds no movement, targeting or save
authority. Floor replacement clears its published instances before retirement.

`tuning/appearance.json` also owns a low ambient fill. The direct point lights
can contribute no light to faces pointing away from them; fill keeps those
textures readable without raising every light or emitting from the materials.
The room-light comparison toggle switches both ambient and fixed room lights;
the movable entrance lantern remains independent.

The musket's generated source contains zero-area faces. Its original is kept
under `docs/art/mesh-originals/`; the offline `docs/art/tools/clean-musket.py`
removes those faces from the admitted copy without changing textures or usable
geometry. The skeleton keeps the supplied rig and clips, plus a root fall over
the original standing stagger for `rifles-collapse`. The fall shifts the root
to keep the prone body within its occupied cell, including beside a wall.
The original body is kept
alongside the musket; `docs/art/tools/author-skeleton-collapse.py` rebuilds the
admitted copy from the editable `docs/art/prompts/skeleton-collapse.json` recipe.
