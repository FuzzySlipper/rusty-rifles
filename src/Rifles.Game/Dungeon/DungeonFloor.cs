using Rifles.Procgen;
using Rifles.Game.Content;
using Rifles.Procgen.Generation;

namespace Rifles.Game.Dungeon;

internal sealed record DungeonFloor(ulong Seed, Candidate Intent, DungeonGenerationResult Generation,
    GridPoint Entrance, GridPoint Exit)
{
    internal DungeonArtifacts Geometry => Generation.Artifacts!;

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
        GridPoint Center(NodeKind kind)
        {
            string region = generated.Artifacts.Intermediate.Regions.Single(r => r.Kind == kind).Id;
            PlacedPiece room = generated.Artifacts.Pieces.Single(p => p.RegionId == region);
            return room.WalkableCells.OrderBy(p => p.ManhattanDistance(new GridPoint((int)room.WalkableCells.Average(p => p.X), (int)room.WalkableCells.Average(p => p.Y))))
                .ThenBy(p => p.Y).ThenBy(p => p.X).First();
        }
        return new(seed, candidate, generated, Center(NodeKind.Start), Center(NodeKind.Goal));
    }
}
