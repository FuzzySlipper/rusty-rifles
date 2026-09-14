using System.Security.Cryptography;
using System.Text.Json;
using Rifles.Game.Generation;
using Rifles.Procgen.Expeditions;
using Rifles.Procgen;
using Rifles.Game.Content;
using Rifles.Procgen.Generation;

namespace Rifles.Game.Dungeon;

// Resolved data is the played floor. Reload never reruns the generator.
internal sealed record DungeonFloor(ulong Seed, string GenerationIdentity, GridPoint[] Cells,
    GridPoint[] RoomCenters, GridPoint Entrance, GridPoint Exit, string IntentFloorId, string IntentGraphIdentity, ResolvedRoom[] Rooms, string LayoutIdentity)
{
    internal static DungeonFloor Generate(ulong seed, GenerationDefinition definition, RoomCatalogue catalogue)
    {
        ExpeditionGenerationResult result = new ExpeditionGenerator().Generate(definition.Expedition, seed);
        if (!result.Accepted) throw new InvalidDataException("Expedition rejected: " + string.Join(", ", result.Diagnostics.Select(d => d.Code + ": " + d.Detail)));
        ResolvedFloorIntent floor = result.Expedition!.Floors.Single(f => f.Id == result.Expedition.EntranceFloor);
        return Generate(floor, definition.Policy, catalogue);
    }

    internal static DungeonFloor Generate(ResolvedFloorIntent floor, GenerationPolicy policy, RoomCatalogue catalogue)
    {
        Candidate candidate = floor.Candidate;
        ulong seed = candidate.Seed;
        DungeonGenerationResult generated = new DungeonGenerator().Generate(candidate, policy, seed, catalogue.Shapes);
        if (!generated.Accepted || generated.Artifacts is null)
            throw new InvalidOperationException($"Dungeon generation rejected: {generated.RejectionStage}/{generated.RejectionCode}");
        DungeonArtifacts geometry = generated.Artifacts;
        static GridPoint Center(PlacedPiece room)
        {
            GridPoint average = new((int)room.WalkableCells.Average(p => p.X), (int)room.WalkableCells.Average(p => p.Y));
            return room.WalkableCells.OrderBy(p => p.ManhattanDistance(average)).ThenBy(p => p.Y).ThenBy(p => p.X).First();
        }
        GridPoint Endpoint(NodeKind kind) => Center(geometry.Pieces.Single(p => p.RegionId == geometry.Intermediate.Regions.Single(r => r.Kind == kind).Id));
        DungeonFloor resolved = new(seed, "", geometry.WalkableCells.OrderBy(p => p.Y).ThenBy(p => p.X).ToArray(),
            geometry.Pieces.Select(Center).ToArray(), Endpoint(NodeKind.Start), Endpoint(NodeKind.Goal), floor.Id, CanonicalIdentity.Hash(candidate),
            geometry.Pieces.Select(piece =>
            {
                var template = catalogue.Rooms.Single(r => r.Id == piece.ShapeId);
                var region = geometry.Intermediate.Regions.Single(r => r.Id == piece.RegionId);
                return new ResolvedRoom(region.Id, region.SourceNodeId, template.Id, template.Title, template.Function, template.Landmark,
                    piece.WalkableCells.ToArray(), piece.Exits.Values.Select(e => e.Cell).Distinct().ToArray());
            }).ToArray(), generated.Identity);
        return resolved with { GenerationIdentity = ManifestIdentity(resolved) };
    }

    // Save-schema serialization is deterministic and includes the complete retained room manifest.
    private static string ManifestIdentity(DungeonFloor floor) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(floor with { GenerationIdentity = "" }))).ToLowerInvariant();

    internal void Validate()
    {
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(IntentFloorId) && !string.IsNullOrWhiteSpace(IntentGraphIdentity), "Floor intent identity");
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(GenerationIdentity), "Floor.GenerationIdentity");
        GameDefinitions.Require(Cells is { Length: > 0 } && Cells.Distinct().Count() == Cells.Length, "Floor.Cells");
        GameDefinitions.Require(Cells.All(p => p.X >= 0 && p.Y >= 0
            && p.X < GenerationPolicyValidation.MaxDimension && p.Y < GenerationPolicyValidation.MaxDimension), "Floor cell bounds");
        HashSet<GridPoint> cells = Cells.ToHashSet();
        GameDefinitions.Require(cells.Contains(Entrance) && cells.Contains(Exit) && Entrance != Exit, "Floor.Entrance/Exit");
        GameDefinitions.Require(ResolvedGridValidation.IsConnected(cells, Entrance), "Floor connectivity");
        GameDefinitions.Require(Rooms is { Length: > 0 } && Rooms.All(r => r is not null
            && !string.IsNullOrWhiteSpace(r.RegionId) && !string.IsNullOrWhiteSpace(r.NodeId) && r.RegionId == "region." + r.NodeId
            && !string.IsNullOrWhiteSpace(r.TemplateId) && !string.IsNullOrWhiteSpace(r.Title)
            && !string.IsNullOrWhiteSpace(r.Function) && !string.IsNullOrWhiteSpace(r.Landmark)
            && r.Cells is { Length: > 0 } && r.Cells.Distinct().Count() == r.Cells.Length && r.Cells.All(cells.Contains)
            && ResolvedGridValidation.IsConnected(r.Cells.ToHashSet(), r.Cells[0])
            && r.Thresholds is not null && r.Thresholds.All(r.Cells.Contains))
            && Rooms.Select(r => r.RegionId).Distinct().Count() == Rooms.Length
            && Rooms.Select(r => r.NodeId).Distinct().Count() == Rooms.Length
            && Rooms.SelectMany(r => r.Cells).Distinct().Count() == Rooms.Sum(r => r.Cells.Length), "Floor rooms");
        GameDefinitions.Require(RoomCenters is { Length: > 0 } && RoomCenters.All(cells.Contains), "Floor.RoomCenters");
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(LayoutIdentity) && GenerationIdentity == ManifestIdentity(this), "Floor manifest identity");
    }
}
