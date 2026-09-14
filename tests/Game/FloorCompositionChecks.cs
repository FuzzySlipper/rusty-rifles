using System.Text.Json;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Generation;
using Rifles.Game.Items;
using Rifles.Game.Combat;
using Rifles.Procgen;
using Rifles.Procgen.Expeditions;
using Rifles.Procgen.Generation;

internal static class FloorCompositionChecks
{
    internal static void Check(DungeonFloor floor, ResolvedFloorIntent intent, GameDefinitions definitions, DungeonGenerationResult generation)
    {
        floor = floor.WithArchitecture(definitions.Architecture);
        floor.Validate();
        ulong nextId = 1;
        PatrolActor actor = PatrolActor.Create(nextId++, floor,
            definitions.Exploration with { StepSeconds = definitions.Features.ActorStepSeconds }, definitions.Features);
        RoomDressing dressing = RoomDressing.Create(floor, actor, definitions.Art, () => nextId++);
        ExplorationItems itemWorld = ExplorationItems.Create(definitions.ItemExploration, floor, dressing, actor, () => nextId++);
        ItemExplorationSnapshot itemState = itemWorld.Capture();
        GridPoint[] staticObstacles = [actor.Capture().Start, actor.Capture().End, dressing.Bench, dressing.Crate, dressing.Observer];
        if (!DressingPlacement.CanBlock(floor, staticObstacles))
            throw new Exception("Floor composition static placement blocks a protected route.");

        var gates = GeneratedFeatures.Resolve(floor, () => nextId++);
        if (itemState.Door == floor.Entrance || itemState.Door == floor.Exit || staticObstacles.Contains(itemState.Door)
            || gates.Any(gate => gate.Cell == itemState.Door) || floor.Grants.Any(grant => grant.Cell == itemState.Door)
            || floor.Connectors.Any(connector => connector.From == itemState.Door || connector.To == itemState.Door))
            throw new Exception("Floor composition item gate overlaps a protected feature.");
        var keys = GeneratedFeatures.RequiredKeys(floor).Select(g => new GeneratedKey(g.Item, nextId++, "inspection:" + g.Item, g.Cell)).ToArray();
        var plates = gates.Where(g => g.Traversal == TraversalKind.Locked).Select(g => new GeneratedPlate(nextId++, g.Id,
            "inspection", floor.Routes.Single(r => r.Id == g.RouteId).Cells[0],
            keys.Single(k => k.Item == g.RequiredItem).Cell, definitions.GeneratedFeatures.PlateWeight)).ToArray();
        var progression = FloorProgression.Inspect(floor, gates, plates);
        if (!progression.Accepted) throw new Exception("Floor composition: " + string.Join(",", progression.Diagnostics));
        MovementGrid movement = new(floor.Cells.ToHashSet(), (_, _) => true, definitions.Crowd);
        const ulong partyId = ulong.MaxValue;
        movement.Add(partyId, floor.Entrance);
        actor.Bind(movement);
        dressing.Bind(movement);
        HashSet<string> hazardNodes = intent.Candidate.Graph.Nodes.Where(node => node.Kind == NodeKind.Hazard)
            .Select(node => node.Id).ToHashSet();
        GridPoint[] hazards = floor.Rooms.Where(room => hazardNodes.Contains(room.NodeId))
            .Select(room => room.Cells.OrderBy(cell => cell.ManhattanDistance(room.Cells[room.Cells.Length / 2])).First()).ToArray();
        if (hazards.Any(cell => staticObstacles.Contains(cell) || cell == itemState.Door || gates.Any(gate => gate.Cell == cell)))
            throw new Exception("Floor composition hazard overlaps a realized obstacle or closed barrier.");
        var exclusions = floor.Cells.Where(movement.Occupied).Concat(gates.Select(gate => gate.Cell)).Concat(hazards)
            .Concat(floor.Grants.Select(grant => grant.Cell)).Append(itemState.Door).ToHashSet();
        var encounters = new EncounterPlacementResolver(definitions.EncounterPlacement)
            .Resolve(floor.Seed, floor, definitions.Combat, definitions.Crowd, exclusions);
        if (!encounters.Accepted) throw new Exception($"Encounter composition {floor.Seed}/{floor.IntentFloorId}: "
            + JsonSerializer.Serialize(encounters.Rejections));
        encounters.Validate(floor, definitions.Combat, definitions.Crowd);
        if (encounters.Instances.Any(instance => exclusions.Contains(instance.Cell)))
            throw new Exception("Encounter composition overlaps a realized obstacle, gate, hazard, grant, or arrival cell.");
        var supplies = RouteSupplies.Resolve(floor, definitions.RouteSupplies,
            encounters.Instances.Sum(e => definitions.Combat.Enemy(e.EnemyId).Vitality));
        string[] args = Environment.GetCommandLineArgs();
        int export = Array.IndexOf(args, "--export-floors");
        if (export >= 0 && export + 1 < args.Length)
        {
            string directory = Path.GetFullPath(args[export + 1]);
            Directory.CreateDirectory(directory);
            string name = $"{floor.Seed}-{floor.IntentFloorId}";
            File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(floor));
            File.WriteAllText(Path.Combine(directory, name + "-inspection.json"), JsonSerializer.Serialize(new
            { generation.Attempts, generation.Metrics, progression, encounters, supplies, plates, gates }));
        }
    }
}
