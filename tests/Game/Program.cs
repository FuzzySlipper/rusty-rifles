using Rifles.Game.Dungeon;
using Rifles.Game.Content;
using Rifles.Game.Party;
using Rifles.Procgen.Generation;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

string contentRoot = Path.GetFullPath("content");
GameDefinitions definitions = GameDefinitions.Load(path => File.ReadAllBytes(Path.Combine(contentRoot, path)));
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
    DungeonFloor floor = DungeonFloor.Generate(seed, definitions.Generation);
    Require(floor.GenerationIdentity == DungeonFloor.Generate(seed, definitions.Generation).GenerationIdentity, "Same seed must replay.");
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

PartyState party = new(definitions.Party.Members);
Require(party.Members.Count == 4 && party.Members.Select(m => m.Definition.Slot).Distinct().Count() == 4, "Four distinct formation slots.");
party.Members[0].ApplyDamage(7);
var saved = party.Capture();
party.Members[0].ApplyDamage(long.MaxValue);
Require(!party.Members[0].IsLiving, "Damage saturates at zero vitality.");
party.Restore(saved);
Require(party.Capture().SequenceEqual(saved), "Snapshot restores prior vitality, including healing.");
try
{
    party.Restore(saved.Select((m, index) => index == 3 ? m with { Vitality = -1 } : m with { Vitality = 0 }).ToArray());
    throw new Exception("Invalid vitality accepted.");
}
catch (InvalidOperationException)
{
    Require(party.Capture().SequenceEqual(saved), "Invalid snapshot cannot partially change party.");
}
Console.WriteLine("Game checks passed: generated connectivity/replay, grid actions/recovery, party vitality/snapshots.");

// A resolved snapshot round-trips without invoking the generator on restore.
DungeonFloor savedFloor = DungeonFloor.Generate(definitions.Generation.Seed, definitions.Generation);
ExplorationState savePose = new(savedFloor.Entrance, definitions.Exploration);
PatrolActor saveActor = PatrolActor.Create(3, savedFloor,
    definitions.Exploration with { StepSeconds = definitions.Features.ActorStepSeconds }, definitions.Features);
MovementGrid saveGrid = new(savedFloor.Cells.ToHashSet(), (_, _) => true);
savePose.Bind(saveGrid, 2); saveActor.Bind(saveGrid);
saveActor.Advance(.1);
PartyState saveParty = new(definitions.Party.Members);
saveParty.Members[0].ApplyDamage(9);
var snapshot = new Rifles.Game.Expedition.ExpeditionSnapshot(Guid.NewGuid(), 1, 2, 6,
    savedFloor, savePose.Capture(), definitions.Party.Members, saveParty.Capture().ToArray(), true,
    definitions.Party.Members[2].Id, saveActor.Capture(), new FeatureSnapshot(4, 5, 2, false, true));
var codec = new Rifles.Game.Expedition.ExpeditionCodec();
System.Buffers.ArrayBufferWriter<byte> payload = new();
codec.Encode(snapshot, payload);
var decoded = codec.Decode(payload.WrittenSpan);
var restored = Rifles.Game.Expedition.ExpeditionCodec.Validate(decoded, definitions);
Require(decoded.Id == snapshot.Id && decoded.NextObjectId == snapshot.NextObjectId, "Stable identities survive serialization.");
Require(decoded.Floor.Cells.SequenceEqual(savedFloor.Cells) && decoded.Floor.GenerationIdentity == savedFloor.GenerationIdentity, "Resolved floor is stored exactly.");
Require(restored.Party.Capture().SequenceEqual(saveParty.Capture()) && restored.Actor.Capture() == saveActor.Capture(), "Vitality and in-transit actor survive save.");
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
    decoded with { Members = decoded.Members.Select(m => m with { Vitality = -1 }).ToArray() },
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
