using Rifles.Game.Magic;
using Rifles.Game.Items;
using Rifles.Game.Combat;
using Rifles.Game.Characters;
using Rifles.Game.Dungeon;
using Rifles.Game.Content;
using Rifles.Game.Party;
using Rifles.Game.Tests;
using Rifles.Procgen.Generation;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

string contentRoot = Path.GetFullPath("content");
GameDefinitions definitions = GameDefinitions.Load(path => File.ReadAllBytes(Path.Combine(contentRoot, path)));
var hud = new Rifles.Game.HudPublication();
Require(hud.Take(.1, true), "Initial HUD is immediate.");
hud.Advance(.05);
Require(!hud.Take(.1, false), "Simulation updates do not flood the HUD.");
Require(hud.Take(.1, true), "Commands publish before the periodic deadline.");
hud.Advance(.05);
Require(!hud.Take(.1, false), "Command feedback resets the periodic deadline.");
hud.Advance(.06);
Require(hud.Take(.1, false), "Admitted time refreshes the HUD even while gameplay is paused.");
hud.Advance(1);
Require(hud.Take(.1, false) && !hud.Take(.1, false), "Catch-up publishes only the latest HUD once.");
foreach (double invalid in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
{
    try { new HudTuning(invalid).Validate(); throw new Exception("Invalid HUD tuning accepted."); }
    catch (InvalidDataException) { }
}
ExpeditionChecks.Run(definitions);
RoomCatalogueChecks.Run(definitions);
GeneratedFeatureChecks.Run(definitions);
EncounterPlacementChecks.Run(definitions);
ArchitectureDetailChecks.Run(definitions);
DressingPlacementChecks.Run(definitions);
ActionChecks.Run();
MagicChecks.Run(definitions);
CommandChecks.Run();
EnemyBrainChecks.Run();
CrowdChecks.Run();
CrowdLaneChecks.Run(definitions);
FormationRulesChecks.Run(definitions);
CommanderChecks.Run(definitions);
WeaponChecks.Run(definitions);
CombatInventoryChecks.Run(definitions);
CombatSaveChecks.Run(definitions);
// Invalid authored files fail at admission, before creating any live world.
foreach (string invalid in new[] { "{", "{}", "null", File.ReadAllText(Path.Combine(contentRoot, "tuning/exploration.json")).Replace("0.22", "-1") })
{
    try
    {
        GameDefinitions.Load(path => path == "tuning/exploration.json"
            ? System.Text.Encoding.UTF8.GetBytes(invalid) : File.ReadAllBytes(Path.Combine(contentRoot, path)));
        throw new Exception("Invalid content was accepted.");
    }
    catch (InvalidDataException error) { Require(error.Message.Contains("tuning/exploration.json"), "Content errors identify the asset."); }
}
GameDefinitions retuned = GameDefinitions.Load(path => path == "tuning/exploration.json"
    ? System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(contentRoot, path)).Replace("0.22", "0.44"))
    : File.ReadAllBytes(Path.Combine(contentRoot, path)));
Require(retuned.Exploration.StepSeconds == .44, "Authored tuning reaches the typed domain.");
// Exercise the actual game's composition of graph intent and physical geometry.
foreach (ulong seed in new ulong[] { 0, 1, 29, 83 })
{
    DungeonFloor floor = DungeonFloor.Generate(seed, definitions.Generation, definitions.Rooms);
    Require(floor.GenerationIdentity == DungeonFloor.Generate(seed, definitions.Generation, definitions.Rooms).GenerationIdentity, "Same seed must replay.");
    HashSet<GridPoint> reached = [floor.Entrance];
    Queue<GridPoint> queue = new();
    queue.Enqueue(floor.Entrance);
    while (queue.TryDequeue(out GridPoint cell))
        foreach (CardinalDirection direction in CardinalDirections.Ordered)
        {
            GridPoint next = cell + direction.Offset();
            if (floor.Cells.Contains(next) && reached.Add(next)) queue.Enqueue(next);
        }
    Require(reached.SetEquals(floor.Cells), "All generated floor cells must connect to entrance.");
    Require(reached.Contains(floor.Exit) && floor.Exit != floor.Entrance, "Exit must be distinct and reachable.");
}

HashSet<GridPoint> testCells = [new(10, 10), new(11, 10), new(11, 9)];
MovementGrid grid = new(testCells, (_, _) => true);
ExplorationState state = new(new GridPoint(10, 10), definitions.Exploration);
state.Bind(grid, 1);
Require(!state.Act(ExplorationAction.Forward), "Blocked move rejected.");
Require(state.Position == new GridPoint(10, 10) && state.RecoverySeconds == 0, "Rejected move leaves pose unchanged.");
Require(state.Act(ExplorationAction.TurnRight), "Turn starts.");
Require(!state.Act(ExplorationAction.Forward), "Recovery prevents another action.");
state.Advance(definitions.Exploration.TurnSeconds);
Require(state.Facing == CardinalDirection.East, "Turn commits exact facing.");
Require(state.Act(ExplorationAction.Forward), "Forward starts.");
state.Advance(definitions.Exploration.StepSeconds / 2);
Require(state.Position == new GridPoint(10, 10) && state.VisualCell.X > 10 && state.VisualCell.X < 11, "Logical source owns transit; presentation moves.");
state.Advance(definitions.Exploration.StepSeconds);
Require(state.Position == new GridPoint(11, 10), "Completed move commits destination.");
grid.Add(2, new(11, 9));
Require(!grid.TryReserve(2, new(11, 10)), "Actor cannot reserve occupied party cell.");
Require(grid.TryReserve(1, new(10, 10)), "Party reservation accepted.");
grid.Cancel(1);
Require(grid.TryReserve(1, new(10, 10)), "Cancellation releases reservation.");
grid.SetBlocked(new(10, 10), new(11, 10), true);
Require(!grid.Commit(1) && grid.Position(1) == new GridPoint(11, 10), "Closing edge cancels move without displacing actor.");

MemberDefinition[] roster = definitions.Characters.ResolvePreset(definitions.Characters.DefaultPresetId);
Require(roster.Single(m => m.Id == "warden").MaximumVitality == 40 && roster.Single(m => m.Id == "warden").BasePower == 7
    && roster.Single(m => m.Id == "seeker").MaximumResource == 16 && roster.Single(m => m.Id == "seeker").InitialResource == 7
    && roster.Where(m => m.Id.StartsWith("musketeer-", StringComparison.Ordinal)).All(m => m.Archetype == "seeker"), "Supplied presets retain their authored values through archetype references.");
PartyState party = new(definitions.Party.Positions, definitions.Party.MaxPartySize, roster);
Require(party.Members.Select(m => m.Definition.Position).Distinct(StringComparer.Ordinal).Count() == party.Members.Count, "Authored formation positions are distinct.");
CharacterOptionsDefinition twins = definitions.Characters with
{
    Presets =
    [
        new StarterPartyPresetDefinition("twin-watch", "Twin watch",
        [
            new PresetMemberDefinition("warden-a", "warden", "Warden A", "front-left"),
            new PresetMemberDefinition("warden-b", "warden", "Warden B", "front-center"),
        ]),
    ],
};
MemberDefinition[] twinRoster = twins.ResolvePreset("twin-watch");
Require(twinRoster.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() == 2
    && twinRoster.All(m => m.Archetype == "warden" && m.MaximumVitality == 40 && m.BasePower == 7),
    "Shared archetype instances resolve with distinct instance ids and identical authored stats, without code branches.");
PartyState twinParty = new(definitions.Party.Positions, definitions.Party.MaxPartySize, twinRoster);
MagicState twinMagic = new(definitions.Magic, twinRoster.Select(m => (m.Id, m.Archetype)), twinParty.Entities);
Require(twinParty.Members.Count == 2 && twinMagic.For("warden-a").Known.SetEquals(twinMagic.For("warden-b").Known),
    "A different roster with shared archetypes constructs party state and spellbooks per instance.");
Require(twinRoster.All(m => m.Id != m.Archetype && definitions.Magic.Resistances.ContainsKey(m.Archetype)),
    "Resistance lookups resolve through the archetype, so condition ticks on shared-archetype twins stay covered.");
RiflesCharacter twinA = twinParty.Members.Single(m => m.Definition.Id == "warden-a");
RiflesCharacter twinB = twinParty.Members.Single(m => m.Definition.Id == "warden-b");
Require(twinA.Entity != twinB.Entity
    && ReferenceEquals(twinA.Stats, twinParty.Entities.Store.Get<StatsComponent>(twinA.Entity)),
    "Same-archetype instances are distinct entities sharing no state; the facade reads the attached component live.");
Require(twinParty.Entities.TryGetEntity("warden-a", out EntityId twinEntity) && twinEntity == twinA.Entity
    && twinParty.Entities.TryGetInstance(twinB.Entity) == "warden-b",
    "Durable instance ids reconstruct to runtime entities in both directions.");
long twinVitality = twinB.Vitality;
twinA.ApplyDamage(5);
Require(twinB.Vitality == twinVitality && twinA.Vitality == twinVitality - 5,
    "Damaging one same-archetype instance leaves the other untouched.");
Require(party.Members.All(m => ReferenceEquals(m.Stats.GetTrack(RiflesStatIds.Vitality).Maximum, m.Stats.GetStat(RiflesStatIds.VitalityMax))),
    "Tracks share their maximum Stat references.");
List<FormationPositionDefinition> openPositions = [.. definitions.Party.Positions,
    new FormationPositionDefinition("reserve-a", "Reserve A", 1),
    new FormationPositionDefinition("reserve-b", "Reserve B", 2)];
List<MemberDefinition> six = [.. roster.Take(4),
    new MemberDefinition("fifth", "warden", "Fifth", "reserve-a", 20),
    new MemberDefinition("sixth", "blade", "Sixth", "reserve-b", 20)];
PartyState large = new(openPositions, definitions.Party.MaxPartySize, six);
Require(large.Members.Count == 6 && large.EligibleMembers(PartyReach.Melee).Count() == 2
    && large.CanUseReach("fifth", PartyReach.Ranged) && !large.CanUseReach("sixth", PartyReach.Melee),
    "Party size follows construction and melee reach follows rank, not roster order.");
PartyState pair = new(openPositions, definitions.Party.MaxPartySize, six.Take(2).ToArray());
Require(pair.Members.Count == 2 && pair.MoveFormation("warden", "reserve-b") && pair.Members[0].Position == "reserve-b",
    "Smaller parties construct and move within the same authored positions.");
party.Members[0].ApplyDamage(7);
var saved = party.Capture();
party.Members[0].ApplyDamage(long.MaxValue);
Require(!party.Members[0].IsLiving, "Damage saturates at zero vitality.");
party.Restore(saved);
Require(StatSnapshotHelpers.MembersEqual(party.Capture(), saved), "Snapshot restores prior vitality, including healing.");
try
{
    party.Restore(saved.Select((m, index) => index == 3
        ? m with { Stats = StatSnapshotHelpers.WithTrack(m.Stats, RiflesStatIds.Vitality, -1) }
        : m with { Stats = StatSnapshotHelpers.WithTrack(m.Stats, RiflesStatIds.Vitality, 0) }).ToArray());
    throw new Exception("Invalid vitality accepted.");
}
catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
{
    Require(StatSnapshotHelpers.MembersEqual(party.Capture(), saved), "Invalid snapshot cannot partially change party.");
}
Console.WriteLine("Game checks passed: generated connectivity/replay, grid actions/recovery, party vitality/snapshots.");

// A resolved snapshot round-trips without invoking the generator on restore.
DungeonFloor savedFloor = DungeonFloor.Generate(definitions.Generation.Seed, definitions.Generation, definitions.Rooms).WithArchitecture(definitions.Architecture);
ExplorationState savePose = new(savedFloor.Entrance, definitions.Exploration);
PatrolActor saveActor = PatrolActor.Create(3, savedFloor,
    definitions.Exploration with { StepSeconds = definitions.Features.ActorStepSeconds }, definitions.Features);
MovementGrid saveGrid = new(savedFloor.Cells.ToHashSet(), (_, _) => true, definitions.Crowd);
savePose.Bind(saveGrid, 2); saveActor.Bind(saveGrid);
saveActor.Advance(.1);
PartyState saveParty = new(definitions.Party.Positions, definitions.Party.MaxPartySize, definitions.Characters, definitions.Characters.DefaultPresetId);
saveParty.Members[0].ApplyDamage(9);
ulong dressingId = 6;
ulong AllocateDressingId() => dressingId++;
RoomDressing savedDressing = RoomDressing.Create(savedFloor, saveActor, definitions.Art, AllocateDressingId);
ExplorationItems savedItemWorld = ExplorationItems.Create(definitions.ItemExploration, savedFloor, savedDressing, saveActor, AllocateDressingId);
ItemInventory savedInventory = new(definitions.Items,
    saveParty.Members.Select(m => new PackOwner(AllocateDressingId(), "member:" + m.Definition.Id, definitions.Items.Backpack.Mass, definitions.Items.Backpack.Space))
    .Append(new PackOwner(AllocateDressingId(), "party", definitions.Items.Party.Mass, definitions.Items.Party.Space))
    .Concat(savedItemWorld.Anchors.Select(a => new PackOwner(a.Id, a.Key,
        a.Key == "crate" ? definitions.Items.Container.Mass : definitions.Items.Anchor.Mass,
        a.Key == "crate" ? definitions.Items.Container.Space : definitions.Items.Anchor.Space))));
savedInventory.GrantStarting(AllocateDressingId);
savedDressing.Bind(saveGrid);
ulong savedObserverId = savedDressing.ObserverId;
var savedGeneratedGates = Rifles.Game.Generation.GeneratedFeatures.Resolve(savedFloor, AllocateDressingId);
List<EnemySnapshot> saveEnemies = [];
var savedEncounterPlacement = new EncounterPlacementResolver(definitions.EncounterPlacement).Resolve(savedFloor.Seed, savedFloor,
    definitions.Combat, definitions.Crowd, savedFloor.Cells.Where(saveGrid.Occupied).Append(savedItemWorld.Capture().Door).Concat(savedGeneratedGates.Select(g => g.Cell)).ToHashSet());
Require(savedEncounterPlacement.Accepted, "Saved fixture encounter placement accepted.");
foreach (var placed in savedEncounterPlacement.Instances)
{
    var spawn = definitions.Combat.Encounter.Single(s => s.Id == placed.SpawnId);
    EnemyDefinition enemy = definitions.Combat.Enemy(spawn.Enemy);
    ulong id = AllocateDressingId(); string owner = "combat:enemy:" + id;
    savedInventory.RegisterOwner(new PackOwner(AllocateDressingId(), owner, definitions.Combat.DropCapacity.Mass, definitions.Combat.DropCapacity.Space));
    foreach (StartingItem loot in enemy.Loot) savedInventory.Grant(InventoryOwner.Parse(owner), loot.Definition, loot.Quantity, AllocateDressingId);
    GridPoint cell = placed.Cell;
    ExplorationState motion = new(cell, definitions.Exploration with { StepSeconds = enemy.StepSeconds }); motion.Bind(saveGrid, id, enemy.Footprint, enemy.Faction, enemy.Share);
    saveEnemies.Add(new(id, enemy.Id, motion.Capture(), StatSnapshotHelpers.FullEnemy(enemy, definitions.Magic.EnemyResource), null, 0, false, false, owner, new EnemyBrain(enemy.Brain, cell, [cell]).Capture(), spawn.Id));
}
CombatSnapshot saveCombat = new(saveEnemies.ToArray(), saveParty.Members.Select(m => new MemberActionSnapshot(m.Definition.Id, null)).ToArray(), new WeaponStateSnapshot([]), [], [],
    new[] { RiflesCombat.FreshAlly(saveActor.Id, definitions), RiflesCombat.FreshAlly(savedObserverId, definitions) }, 0, new MagicState(definitions.Magic, saveParty.Members.Select(m => (m.Definition.Id, m.Definition.Archetype)), saveParty.Entities).Capture());
var snapshot = new Rifles.Game.Expedition.ExpeditionSnapshot(Guid.NewGuid(), 1, 2, dressingId,
    savedFloor, savePose.Capture(), saveParty.Members.Select(m => m.Definition).ToArray(), saveParty.Capture().ToArray(), true,
    saveParty.Members[2].Definition.Id, saveActor.Capture(), new FeatureSnapshot(4, 5, 2, false, true, savedDressing),
    definitions.Characters.DefaultPresetId, savedInventory.Capture(), savedItemWorld.Capture(), saveCombat, new Rifles.Procgen.Expeditions.ExpeditionGenerator().Generate(definitions.Generation.Expedition, savedFloor.Seed).Expedition!, new Rifles.Game.Generation.GeneratedFeatureSnapshot(1, savedGeneratedGates, [], [], [], []), savedEncounterPlacement, 0, "");
var run = new Rifles.Game.Expedition.RunSnapshot(snapshot, [],
    new Rifles.Game.Expedition.RunProgress(definitions.Run.DefaultDifficulty, false, []));
var codec = Rifles.Game.Expedition.RunCodec.CreateStoreCodec();
System.Buffers.ArrayBufferWriter<byte> payload = new();
codec.Encode(run, payload);
var decodedRun = codec.Decode(payload.WrittenSpan);
Rifles.Game.Expedition.RunCodec.Validate(decodedRun, definitions);
var decoded = decodedRun.Active;
var restored = Rifles.Game.Expedition.ExpeditionCodec.Validate(decoded, definitions);
var commanderDefeat = decoded with { Members = decoded.Members.Select(member => member.Id == "commander"
    ? member with { Stats = StatSnapshotHelpers.WithTrack(member.Stats, RiflesStatIds.Vitality, 0) } : member).ToArray() };
var defeatedParty = Rifles.Game.Expedition.ExpeditionCodec.Validate(commanderDefeat, definitions).Party;
Require(defeatedParty.Defeated && defeatedParty.Soldiers.Any(member => member.IsLiving),
    "Whole expedition restore retains commander defeat with surviving soldiers.");
RunStateChecks.Run(definitions, decoded);
Require(decoded.Intent.Identity == snapshot.Intent.Identity
    && decoded.Intent.Connectors.SequenceEqual(snapshot.Intent.Connectors), "Resolved expedition and connector identities survive the save.");
var changedGeneration = definitions with { Generation = definitions.Generation with { Seed = 999 } };
_ = Rifles.Game.Expedition.ExpeditionCodec.Validate(decoded, changedGeneration);
Require(decoded.Id == snapshot.Id && decoded.NextObjectId == snapshot.NextObjectId, "Stable identities survive serialization.");
Require(decoded.Floor.Cells.SequenceEqual(savedFloor.Cells) && decoded.Floor.GenerationIdentity == savedFloor.GenerationIdentity, "Resolved floor is stored exactly.");
Require(StatSnapshotHelpers.MembersEqual(restored.Party.Capture(), saveParty.Capture()) && restored.Actor.Capture() == saveActor.Capture(), "Vitality and in-transit actor survive save.");
MovementGrid restoredGrid = new(decoded.Floor.Cells.ToHashSet(), (_, _) => true);
restored.Exploration.Bind(restoredGrid, decoded.PartyId); restored.Actor.Bind(restoredGrid);
saveActor.Advance(2); restored.Actor.Advance(2);
Require(restored.Actor.Capture() == saveActor.Capture(), "Resumed actor settles the same reservation and next move.");
foreach (var invalid in new[] {
    decoded with { NextObjectId = 3 },
    decoded with { Features = decoded.Features with { LanternRevision = ulong.MaxValue } },
    decoded with { Floor = decoded.Floor with { Cells = [.. decoded.Floor.Cells, new(16000, 16000)] } },
    decoded with { Actor = decoded.Actor with { Motion = decoded.Actor.Motion with {
        Position = decoded.Actor.Start, Action = ExplorationAction.Backward, RemainingSeconds = .1 } } },
    decoded with { SelectedMember = "missing" },
    decoded with { Floor = decoded.Floor with { Exit = new(-1, -1) } },
    decoded with { Exploration = decoded.Exploration with { RemainingSeconds = double.NaN } },
    decoded with { Features = decoded.Features with { LanternId = decoded.PartyId } },
    decoded with { Members = decoded.Members.Select(m => m with { Stats = StatSnapshotHelpers.WithTrack(m.Stats, RiflesStatIds.Vitality, -1) }).ToArray() },
})
{
    bool rejected = false;
    try { Rifles.Game.Expedition.ExpeditionCodec.Validate(invalid, definitions); }
    catch (Exception error) when (error is InvalidDataException or InvalidOperationException) { rejected = true; }
    Require(rejected, "Invalid expedition snapshot rejected before live replacement.");
}
MovementGrid contested = new(new HashSet<GridPoint> { new(0, 0), new(1, 0), new(2, 0) }, (_, _) => true);
contested.Add(10, new(0, 0)); contested.Add(11, new(2, 0));
Require(contested.TryReserve(10, new(1, 0)) && !contested.TryReserve(11, new(1, 0)), "One exclusive destination reservation wins.");
contested.Cancel(10);
Require(contested.TryReserve(11, new(1, 0)) && contested.Commit(11), "Released destination can be committed by other actor.");

var target = new Rusty.Engine.Interaction.InteractionTarget(4, 1);
var query = new Rusty.Engine.Interaction.InteractionQuery(System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitZ, .5f, .7f, 10, 10);
var candidate = new Rusty.Engine.Interaction.InteractionCandidate(target, "Lamp", System.Numerics.Vector3.UnitZ, 2,
    Rusty.Engine.Interaction.InteractionVisibility.Visible, Rusty.Engine.Interaction.InteractionAvailability.Available);
Require(Rusty.Engine.Interaction.InteractionFocus.Revalidate(target, [candidate], query) == Rusty.Engine.Interaction.InteractionReason.Ready, "Current reachable feature admitted.");
Require(Rusty.Engine.Interaction.InteractionFocus.Revalidate(target, [candidate with { Target = new(4, 2) }], query) == Rusty.Engine.Interaction.InteractionReason.StaleTarget, "Changed feature rejects stale activation.");
Require(Rusty.Engine.Interaction.InteractionFocus.Revalidate(target, [candidate with { Visibility = Rusty.Engine.Interaction.InteractionVisibility.Occluded }], query) == Rusty.Engine.Interaction.InteractionReason.Occluded, "Occluded feature rejected.");
Require(Rusty.Engine.Interaction.InteractionFocus.Revalidate(target, [candidate with { ReachDistance = .5f }], query) == Rusty.Engine.Interaction.InteractionReason.OutOfReach, "Out-of-reach feature rejected.");
Require(Rusty.Engine.Interaction.InteractionFocus.Revalidate(target, [], query) == Rusty.Engine.Interaction.InteractionReason.InvalidTarget, "Removed feature rejected.");
Console.WriteLine("Milestone checks passed: snapshot codec/validation, actor transit replay, contested reservations, target revalidation.");

foreach (CardinalDirection facing in CardinalDirections.Ordered)
{
    GridPoint forward = facing.Offset();
    System.Numerics.Vector2 front = new(forward.X, forward.Y), right = new(-forward.Y, forward.X);
    Require(SentryView.Select(default, facing, front) == "sentry-front", "Front view follows actor facing.");
    Require(SentryView.Select(default, facing, -front) == "sentry-back", "Back view is independent of camera yaw.");
    Require(SentryView.Select(default, facing, right) == "sentry-right", "Right profile preserves anatomical side.");
    Require(SentryView.Select(default, facing, -right) == "sentry-left", "Left profile is not a mirrored right profile.");
}
Require(decoded.Features.Dressing == snapshot.Features.Dressing, "Dressing identities and resolved cells round-trip.");
foreach (ArtStyleDefinition style in definitions.Art.Styles)
    foreach (SpriteImageDefinition image in style.Images)
        Require(File.Exists(Path.Combine(contentRoot, image.Path)), "Every authored sprite resolves to a committed asset.");
try
{
    (definitions.Art with { AlphaCutoff = float.NaN }).Validate();
    throw new Exception("Invalid sprite alpha was accepted.");
}
catch (InvalidDataException) { }
Console.WriteLine("Art checks passed: cardinal views, saved dressing, asset references and lighting admission.");

try
{
    GameDefinitions.Load(path => path == "definitions/world-art.json"
        ? System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(contentRoot, path)).Replace("ink-wash", "different-treatment"))
        : File.ReadAllBytes(Path.Combine(contentRoot, path)));
    throw new Exception("Mismatched voxel/sprite treatments were accepted.");
}
catch (InvalidDataException error)
{
    Require(error.Message.Contains("matching treatments"), "Style drift fails at content admission.");
}
InventoryChecks.Run(definitions);
