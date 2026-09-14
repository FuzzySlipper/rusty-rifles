using Rifles.Game.Expedition;
using Rifles.Game.Dungeon;
using Rifles.Game.Items;
using Rifles.Game.Presentation;
using Rifles.Procgen.Expeditions;
using Rifles.Procgen.Generation;

namespace Rifles.Game;

public sealed partial class RiflesProduct
{
    private readonly Dictionary<string, RetainedFloor> inactiveFloors = [];
    private RunSnapshot CaptureRun() => new(Capture(), inactiveFloors.Values.ToArray());

    private IEnumerable<(ExpeditionConnector Link, string Destination, GridPoint Departure, bool Forward)> Connections()
    {
        foreach (var link in expedition.Connectors)
        {
            if (link.FromFloor == floor.IntentFloorId) yield return (link, link.ToFloor, floor.Exit, true);
            if (link.TwoWay && link.ToFloor == floor.IntentFloorId) yield return (link, link.FromFloor, floor.Entrance, false);
        }
    }

    private string? TravelProblem(GridPoint departure)
    {
        if (Defeated) return "A living party is required.";
        if (paused) return "Resume before travelling.";
        if (exploration.Position != departure) return "Stand on the marked stair to travel.";
        if (exploration.Moving || actions.Values.Any(a => a.Busy) || flights.Count != 0 || magic!.RestRemaining > 0)
            return "Finish movement, actions and projectile flights before travelling.";
        return null;
    }

    private void Travel(string connector)
    {
        var options = Connections().Where(c => c.Link.Id == connector).ToArray();
        if (options.Length != 1) throw new InvalidDataException("Stair destination unavailable.");
        var route = options[0];
        if (TravelProblem(route.Departure) is { } problem) throw new InvalidDataException(problem);
        ExpeditionSnapshot current = Capture();
        ulong next = nextObjectId;
        RetainedFloor destination;
        if (!inactiveFloors.TryGetValue(route.Destination, out destination!))
        {
            var created = FloorFactory.Create(engine, generatedArt!, definitions, expedition, route.Destination,
                expeditionId, partyId, preset, ref next, AllocateLightId);
            destination = RetainedFloor.Capture(created);
        }
        GridPoint arrival = route.Forward ? destination.Floor.Entrance : destination.Floor.Exit;
        var pose = new ExplorationSnapshot(arrival, exploration.Facing, destination.Departure.ElapsedSeconds,
            null, arrival, exploration.Facing, 0);
        var candidate = destination.Join(current with { NextObjectId = next }, pose);
        var retained = inactiveFloors.Values.Where(f => f.Floor.IntentFloorId != route.Destination)
            .Append(RetainedFloor.Capture(current)).ToArray();
        var run = new RunSnapshot(candidate, retained);
        RunCodec.Validate(run, definitions);
        // Activate validates and prepares the replacement before retiring the old scene.
        Activate(candidate, RunCodec.Rewards(run), RunCodec.Items(run));
        inactiveFloors.Clear();
        foreach (var state in retained) inactiveFloors.Add(state.Floor.IntentFloorId, state);
        feedback = "Entered " + expedition.Floors.Single(f => f.Id == route.Destination).Title + ". Inactive floors are frozen.";
    }

    private void StartNewRun(ulong seed)
    {
        var generated = new ExpeditionGenerator().Generate(definitions.Generation.Expedition, seed);
        if (!generated.Accepted) throw new InvalidDataException("Expedition generation rejected: " + string.Join(", ", generated.Diagnostics.Select(d => d.Detail)));
        ulong next = nextObjectId;
        var initial = FloorFactory.Create(engine, generatedArt!, definitions, generated.Expedition!, generated.Expedition!.EntranceFloor,
            Guid.NewGuid(), partyId, preset, ref next, AllocateLightId);
        Activate(initial, []);
        inactiveFloors.Clear();
        feedback = "New expedition started.";
    }

    private uint RunProjection(SessionValueBuilder value)
    {
        var connections = Connections().Select(c => (c.Link.Id, value.Object(
            ("title", value.String((c.Forward ? "Descend to " : "Return to ") + expedition.Floors.Single(f => f.Id == c.Destination).Title)),
            ("problem", value.String(TravelProblem(c.Departure) ?? "")),
            ("x", value.Number(c.Departure.X)), ("y", value.Number(c.Departure.Y))))).ToArray();
        return value.Object(("title", value.String(expedition.Title)),
            ("floor", value.String(expedition.Floors.Single(f => f.Id == floor.IntentFloorId).Title)),
            ("connections", value.Object(connections)), ("inactiveTime", value.String("Inactive floors freeze.")));
    }
}
