using Rifles.Procgen;
using Rifles.Procgen.Generation;

namespace Rifles.Game.Dungeon;

internal sealed record DungeonFloor(ulong Seed, Candidate Intent, DungeonGenerationResult Generation,
    GridPoint Entrance, GridPoint Exit)
{
    internal DungeonArtifacts Geometry => Generation.Artifacts!;

    internal static DungeonFloor Generate(ulong seed)
    {
        GraphCore graph = new();
        Candidate candidate = graph.CreateInitial(new SeedIntent("rifles.dungeon", "Dungeon", ["detour"]), seed);
        RuleApplication application = graph.Apply(candidate, GraphRule.DetourLoop, seed);
        if (!application.Accepted)
            throw new InvalidOperationException("Dungeon graph rejected: " + string.Join(", ", application.Diagnostics.Select(d => d.Code)));
        candidate = application.Candidate;
        DungeonGenerationResult generated = new DungeonGenerator().Generate(candidate, GenerationPolicy.Normal, seed);
        if (!generated.Accepted || generated.Artifacts is null)
            throw new InvalidOperationException($"Dungeon generation rejected: {generated.RejectionStage}/{generated.RejectionCode}");
        GridPoint Center(NodeKind kind)
        {
            string region = generated.Artifacts.Intermediate.Regions.Single(r => r.Kind == kind).Id;
            PlacedPiece room = generated.Artifacts.Pieces.Single(p => p.RegionId == region);
            return room.WalkableCells.OrderBy(p => p.ManhattanDistance(room.Origin + new GridPoint(2, 2)))
                .ThenBy(p => p.Y).ThenBy(p => p.X).First();
        }
        return new(seed, candidate, generated, Center(NodeKind.Start), Center(NodeKind.Goal));
    }
}
