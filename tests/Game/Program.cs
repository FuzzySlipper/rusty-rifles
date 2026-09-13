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
    Require(floor.Generation.Identity == DungeonFloor.Generate(seed, definitions.Generation).Generation.Identity, "Same seed must replay.");
    HashSet<GridPoint> reached = [floor.Entrance];
    Queue<GridPoint> queue = new();
    queue.Enqueue(floor.Entrance);
    while (queue.TryDequeue(out GridPoint cell))
        foreach (CardinalDirection direction in CardinalDirections.Ordered)
        {
            GridPoint next = cell + direction.Offset();
            if (floor.Geometry.WalkableCells.Contains(next) && reached.Add(next)) queue.Enqueue(next);
        }
    Require(reached.SetEquals(floor.Geometry.WalkableCells), "All generated floor cells must connect to entrance.");
    Require(reached.Contains(floor.Exit) && floor.Exit != floor.Entrance, "Exit must be distinct and reachable.");
}

ExplorationState state = new(new GridPoint(10, 10), definitions.Exploration);
Require(!state.Act(ExplorationAction.Forward, (_, _) => false), "Rejected Engine step must not move party.");
Require(state.Position == new GridPoint(10, 10) && state.RecoverySeconds == 0, "Rejected step must leave pose and recovery unchanged.");
Require(state.Act(ExplorationAction.TurnRight, (_, _) => throw new Exception("Turning must not request translation.")), "Turn accepted.");
Require(!state.Act(ExplorationAction.Forward, (_, _) => true), "Action recovery prevents another action in the same instant.");
state.Advance(definitions.Exploration.TurnSeconds);
GridPoint proposed = default;
Require(state.Act(ExplorationAction.Forward, (_, to) => { proposed = to; return true; }), "Recovered action accepted.");
Require(proposed == new GridPoint(11, 10) && state.Position == proposed, "Forward follows party facing.");
state.Advance(1);
Require(state.ElapsedSeconds > 1 && state.RecoverySeconds == 0, "Simulation advances without player input.");
Require(state.Act(ExplorationAction.StrafeLeft, (_, _) => true) && state.Position == new GridPoint(11, 9), "Strafing respects facing.");

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
