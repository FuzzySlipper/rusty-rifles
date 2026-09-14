using Rifles.Game.Expedition;
using Rifles.Game.Dungeon;
using Rifles.Game.Items;
using Rifles.Game.Content;
using System.Text.Json;
using Rifles.Game.Presentation;
using Rifles.Procgen.Expeditions;
using Rifles.Procgen.Generation;

namespace Rifles.Game;

public sealed partial class RiflesProduct
{
    private readonly Dictionary<string, RetainedFloor> inactiveFloors = [];
    private RunProgress progress;
    private RunSnapshot CaptureRun() => new(Capture(), inactiveFloors.Values.ToArray(), progress);
    private GameDefinitions FloorDefinitions(string difficulty) => definitions with { RouteSupplies = definitions.RouteSupplies with
        { AmmunitionAllowance = definitions.Run.Difficulty(difficulty).AmmunitionAllowance } };

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
        if (progress.Completed) return "Expedition complete.";
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
            var created = FloorFactory.Create(engine, dungeonMaterials!, FloorDefinitions(progress.Difficulty), expedition, route.Destination,
                expeditionId, partyId, preset, ref next, AllocateLightId);
            destination = RetainedFloor.Capture(created);
        }
        GridPoint arrival = route.Forward ? destination.Floor.Entrance : destination.Floor.Exit;
        var pose = new ExplorationSnapshot(arrival, exploration.Facing, destination.Departure.ElapsedSeconds,
            null, arrival, exploration.Facing, 0);
        var candidate = destination.Join(current with { NextObjectId = next }, pose);
        var retained = inactiveFloors.Values.Where(f => f.Floor.IntentFloorId != route.Destination)
            .Append(RetainedFloor.Capture(current)).ToArray();
        var run = new RunSnapshot(candidate, retained, progress);
        RunCodec.Validate(run, definitions);
        // Activate validates and prepares the replacement before retiring the old scene.
        Activate(candidate, RunCodec.Rewards(run), RunCodec.Items(run));
        inactiveFloors.Clear();
        foreach (var state in retained) inactiveFloors.Add(state.Floor.IntentFloorId, state);
        feedback = "Entered " + expedition.Floors.Single(f => f.Id == route.Destination).Title + ". Inactive floors are frozen.";
    }

    private void StartNewRun(ulong seed, string? difficulty = null)
    {
        difficulty ??= progress.Difficulty;
        var floorDefinitions = FloorDefinitions(difficulty);
        var generated = new ExpeditionGenerator().Generate(definitions.Generation.Expedition, seed);
        if (!generated.Accepted) throw new InvalidDataException("Expedition generation rejected: " + string.Join(", ", generated.Diagnostics.Select(d => d.Detail)));
        ulong next = nextObjectId;
        var initial = FloorFactory.Create(engine, dungeonMaterials!, floorDefinitions, generated.Expedition!, generated.Expedition!.EntranceFloor,
            Guid.NewGuid(), next++, preset, ref next, AllocateLightId);
        Activate(initial, []);
        inactiveFloors.Clear();
        progress = new(difficulty, false, []);
        mapObservation = null;
        feedback = "New expedition started.";
    }

    private void NewRunCommand(string choice)
    {
        using var input = JsonDocument.Parse(choice);
        if (!ulong.TryParse(input.RootElement.GetProperty("seed").GetString(), out ulong seed))
            throw new InvalidDataException("Seed must be an unsigned whole number.");
        StartNewRun(seed, input.RootElement.GetProperty("difficulty").GetString());
    }

    private string? CompletionProblem()
    {
        if (progress.Completed) return "Expedition already complete.";
        if (floor.IntentFloorId != expedition.ObjectiveFloor) return "Reach " + expedition.Floors.Single(f => f.Id == expedition.ObjectiveFloor).Title + ".";
        if (inactiveFloors.Count + 1 != expedition.Floors.Length) return "Explore every expedition floor.";
        if (enemies.Any(e => e.Alive)) return "Defeat the remaining garrison: " + enemies.Count(e => e.Alive) + " foes.";
        return TravelProblem(floor.Exit);
    }

    private void CompleteRun()
    {
        if (CompletionProblem() is { } problem) throw new InvalidDataException(problem);
        magic!.AwardExperience(definitions.Run.FinaleExperience);
        features!.MarkExitUsed();
        progress = progress with { Completed = true };
        SetPaused(true);
        feedback = definitions.Run.SuccessText;
    }

    private uint RunProjection(SessionValueBuilder value)
    {
        ObserveMap();
        var connections = Connections().Select(c => (c.Link.Id, value.Object(
            ("title", value.String((c.Forward ? "Descend to " : "Return to ") + expedition.Floors.Single(f => f.Id == c.Destination).Title)),
            ("problem", value.String(TravelProblem(c.Departure) ?? "")),
            ("x", value.Number(c.Departure.X)), ("y", value.Number(c.Departure.Y))))).ToArray();
        return value.Object(("id", value.String(expeditionId.ToString())), ("title", value.String(expedition.Title)),
            ("floor", value.String(expedition.Floors.Single(f => f.Id == floor.IntentFloorId).Title)),
            ("floorKey", value.String(floor.IntentFloorId)),
            ("seed", value.String(expedition.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            ("nextSeed", value.String(unchecked(expedition.Seed + definitions.Run.SeedIncrement).ToString(System.Globalization.CultureInfo.InvariantCulture))),
            ("difficulty", value.String(progress.Difficulty)),
            ("status", value.String(progress.Completed ? "Complete" : Defeated ? "Defeated" : "Exploring")),
            ("objective", value.String(definitions.Run.Goal + " Visited " + (inactiveFloors.Count + 1) + "/" + expedition.Floors.Length + " floors.")),
            ("result", value.String(progress.Completed ? definitions.Run.SuccessText : Defeated ? definitions.Run.DefeatText : "")),
            ("completionProblem", value.String(CompletionProblem() ?? "")),
            ("finaleLabel", value.String(definitions.Run.FinaleLabel)),
            ("difficulties", value.Object(definitions.Run.Difficulties.Select(d => (d.Id, value.Object(
                ("name", value.String(d.Name)), ("description", value.String(d.Description))))).ToArray())),
            ("maps", MapProjection(value)), ("x", value.Number(exploration.Position.X)),
            ("y", value.Number(exploration.Position.Y)), ("facing", value.String(exploration.Facing.ToString())),
            ("connections", value.Object(connections)));
    }
}
