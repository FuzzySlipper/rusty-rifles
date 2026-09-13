using Rifles.Procgen;
using Rifles.Game.Content;
using Rifles.Procgen.Generation;

namespace Rifles.Game.Dungeon;

// Resolved data is the played floor. Reload never reruns the generator.
internal sealed record DungeonFloor(ulong Seed, string GenerationIdentity, GridPoint[] Cells,
    GridPoint[] RoomCenters, GridPoint Entrance, GridPoint Exit)
{
    internal static DungeonFloor Generate(ulong seed, GenerationDefinition definition)
    {
        GraphCore graph = new();
        Candidate candidate = graph.CreateInitial(new SeedIntent(definition.IntentId, definition.Title, definition.Tags), seed);
        foreach (GraphRule rule in definition.Rules)
        {
            RuleApplication application = graph.Apply(candidate, rule, seed);
            if (!application.Accepted)
                throw new InvalidOperationException("Dungeon graph rejected: " + string.Join(", ", application.Diagnostics.Select(d => d.Code)));
            candidate = application.Candidate;
        }
        DungeonGenerationResult generated = new DungeonGenerator().Generate(candidate, definition.Policy, seed);
        if (!generated.Accepted || generated.Artifacts is null)
            throw new InvalidOperationException($"Dungeon generation rejected: {generated.RejectionStage}/{generated.RejectionCode}");
        DungeonArtifacts geometry = generated.Artifacts;
        static GridPoint Center(PlacedPiece room)
        {
            GridPoint average = new((int)room.WalkableCells.Average(p => p.X), (int)room.WalkableCells.Average(p => p.Y));
            return room.WalkableCells.OrderBy(p => p.ManhattanDistance(average)).ThenBy(p => p.Y).ThenBy(p => p.X).First();
        }
        GridPoint Endpoint(NodeKind kind) => Center(geometry.Pieces.Single(p => p.RegionId == geometry.Intermediate.Regions.Single(r => r.Kind == kind).Id));
        return new(seed, generated.Identity, geometry.WalkableCells.OrderBy(p => p.Y).ThenBy(p => p.X).ToArray(),
            geometry.Pieces.Select(Center).ToArray(), Endpoint(NodeKind.Start), Endpoint(NodeKind.Goal));
    }

    internal void Validate()
    {
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(GenerationIdentity), "Floor.GenerationIdentity");
        GameDefinitions.Require(Cells is { Length: > 0 } && Cells.Distinct().Count() == Cells.Length, "Floor.Cells");
        GameDefinitions.Require(Cells.All(p => p.X >= 0 && p.Y >= 0
            && p.X < GenerationPolicyValidation.MaxDimension && p.Y < GenerationPolicyValidation.MaxDimension), "Floor cell bounds");
        HashSet<GridPoint> cells = Cells.ToHashSet();
        GameDefinitions.Require(cells.Contains(Entrance) && cells.Contains(Exit) && Entrance != Exit, "Floor.Entrance/Exit");
        GameDefinitions.Require(ResolvedGridValidation.IsConnected(cells, Entrance), "Floor connectivity");
        GameDefinitions.Require(RoomCenters is { Length: > 0 } && RoomCenters.All(cells.Contains), "Floor.RoomCenters");
    }
}
