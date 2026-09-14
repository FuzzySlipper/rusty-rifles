using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Procgen.Expeditions;
using Rifles.Procgen.Generation;

internal static class DressingPlacementChecks
{
    private const ulong RedoubtSeed29 = 5681767263628933845;

    internal static void Run(GameDefinitions definitions)
    {
        VerifyHistoricalRedoubtFailure(definitions);

        ExpeditionGenerator generator = new();
        int floors = 0;
        foreach (ulong seed in new ulong[] { 29, 30, 67, 314159 })
        {
            ExpeditionGenerationResult generated = generator.Generate(definitions.Generation.Expedition, seed);
            Require(generated.Accepted, "Placement check expedition must resolve.");
            foreach (ResolvedFloorIntent intent in generated.Expedition!.Floors)
            {
                DungeonFloor floor = DungeonFloor.Generate(intent, definitions.Generation.Policy, definitions.Rooms,
                    definitions.Generation.Elevation);
                PatrolActor actor = PatrolActor.Create(1, floor,
                    definitions.Exploration with { StepSeconds = definitions.Features.ActorStepSeconds }, definitions.Features);
                ulong nextId = 2;
                RoomDressing dressing = RoomDressing.Create(floor, actor, definitions.Art, () => nextId++);
                AssertSafePlacement(floor, actor, dressing);
                floors++;
            }
        }
        Console.WriteLine($"Dressing placement checks passed: {floors} expedition floors retain playable routes around patrols and props.");
    }

    private static void VerifyHistoricalRedoubtFailure(GameDefinitions definitions)
    {
        ExpeditionGenerationResult generated = new ExpeditionGenerator().Generate(definitions.Generation.Expedition, 29);
        ResolvedFloorIntent redoubt = generated.Expedition!.Floors.Single(intent => intent.Id == "redoubt");
        Require(redoubt.Candidate.Seed == RedoubtSeed29, "Seed 29 redoubt retains its stable derived floor seed.");
        DungeonFloor floor = DungeonFloor.Generate(redoubt, definitions.Generation.Policy, definitions.Rooms,
            definitions.Generation.Elevation);
        (GridPoint start, GridPoint end) old = OldPatrol(floor, definitions);
        Require(old.start == new GridPoint(98, 96) && old.end == new GridPoint(98, 95),
            "Historical redoubt patrol selection is reproduced exactly.");
        Require(new HashSet<GridPoint> { old.start, old.end }.Overlaps(TraversalCells(floor)),
            "The historical patrol obstructed a protected direct route, room threshold, or height connector into the north room.");
    }

    private static void AssertSafePlacement(DungeonFloor floor, PatrolActor actor, RoomDressing dressing)
    {
        PatrolSnapshot patrol = actor.Capture();
        HashSet<GridPoint> blocked = [patrol.Start, patrol.End, dressing.Bench, dressing.Crate, dressing.Observer];
        Require(blocked.Count == 5, "Patrol and dressing reserve five distinct floor cells.");
        Require(ConnectedAfterBlocking(floor, blocked), "Floor remains connected while both patrol cells and all dressing are occupied.");

        HashSet<GridPoint> protectedCells = [floor.Entrance, floor.Exit, .. floor.Grants.Select(grant => grant.Cell),
            .. TraversalCells(floor)];
        Require(!blocked.Overlaps(protectedCells),
            "Patrol and dressing never block entrances, exits, grants, routes, room thresholds, or height connectors.");
    }

    private static HashSet<GridPoint> TraversalCells(DungeonFloor floor) => [
        .. floor.Routes.SelectMany(route => route.Cells.Concat(route.AdditionalCells ?? [])),
        .. floor.Rooms.SelectMany(room => room.Thresholds),
        .. floor.Connectors.SelectMany(connector => new[] { connector.From, connector.To })];

    private static (GridPoint start, GridPoint end) OldPatrol(DungeonFloor floor, GameDefinitions definitions)
    {
        HashSet<GridPoint> cells = floor.Cells.ToHashSet();
        foreach (GridPoint start in cells.OrderBy(cell => Math.Abs(cell.ManhattanDistance(floor.Entrance) - definitions.Features.ActorOffsetCells))
            .ThenBy(cell => cell.Y).ThenBy(cell => cell.X))
        {
            foreach (CardinalDirection direction in CardinalDirections.Ordered)
            {
                GridPoint end = start + direction.Offset();
                if (start == floor.Entrance || end == floor.Entrance || !cells.Contains(end)) continue;
                return (start, end);
            }
        }
        throw new InvalidOperationException("Historical patrol selection unexpectedly had no candidate.");
    }

    private static bool ConnectedAfterBlocking(DungeonFloor floor, IReadOnlySet<GridPoint> blocked)
    {
        HashSet<GridPoint> remaining = floor.Cells.Where(cell => !blocked.Contains(cell)).ToHashSet();
        if (remaining.Count == 0) return false;
        HashSet<GridPoint> reached = [remaining.First()];
        Queue<GridPoint> queue = new(reached);
        while (queue.TryDequeue(out GridPoint cell))
        {
            foreach (CardinalDirection direction in CardinalDirections.Ordered)
            {
                GridPoint next = cell + direction.Offset();
                if (remaining.Contains(next) && reached.Add(next)) queue.Enqueue(next);
            }
        }
        return reached.SetEquals(remaining);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
