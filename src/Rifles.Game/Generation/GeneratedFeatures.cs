using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Procgen;
using Rifles.Procgen.Generation;

namespace Rifles.Game.Generation;

internal sealed record GeneratedFeatureDefinition(string KeyItem, string GateClue, string SecretClue, float Reach, string WeightItem, ulong PlateWeight, string PlateClue)
{
    internal void Validate() => GameDefinitions.Require(!string.IsNullOrWhiteSpace(KeyItem)
        && !string.IsNullOrWhiteSpace(GateClue) && !string.IsNullOrWhiteSpace(SecretClue)
        && float.IsFinite(Reach) && Reach > 0 && !string.IsNullOrWhiteSpace(WeightItem) && PlateWeight > 0
        && !string.IsNullOrWhiteSpace(PlateClue), "generated features");
}
internal sealed record GeneratedGate(ulong Id, string RouteId, GridPoint Cell, GridPoint Approach,
    TraversalKind Traversal, string? RequiredItem, bool Discovered, bool Open);
internal sealed record GeneratedKey(string Item, ulong Entity, string Owner, GridPoint Cell);
internal sealed record GeneratedPlate(ulong Id, ulong GateId, string Owner, GridPoint Cell, GridPoint WeightSource, ulong RequiredWeight);
internal sealed record GeneratedFeatureSnapshot(ulong Revision, GeneratedGate[] Gates, GeneratedKey[] Keys, ResolvedSupply[] Supplies, GeneratedHazard[] Hazards, GeneratedPlate[] Plates);

internal static class GeneratedFeatures
{
    internal static FloorGrant[] RequiredKeys(DungeonFloor floor) => floor.Grants
        .Where(g => floor.Routes.Any(r => r.RequiredItem == g.Item)).ToArray();

    internal static GeneratedGate[] Resolve(DungeonFloor floor, Func<ulong> allocate) => floor.Routes
        .Where(r => r.Traversal != TraversalKind.Open).Select(route =>
        {
            int middle = route.Cells.Count / 2;
            if (middle < 1 || middle >= route.Cells.Count - 1)
                throw new InvalidDataException("Portal route has no interior threshold: " + route.Id);
            return new GeneratedGate(allocate(), route.Id, route.Cells[middle], route.Cells[middle - 1],
                route.Traversal, route.RequiredItem, route.Traversal != TraversalKind.Hidden, false);
        }).ToArray();

    internal static void Validate(GeneratedFeatureSnapshot state, DungeonFloor floor)
    {
        GameDefinitions.Require(state.Revision > 0 && state.Gates.Length == floor.Routes.Count(r => r.Traversal != TraversalKind.Open)
            && state.Gates.Select(g => g.RouteId).ToHashSet()
            .SetEquals(floor.Routes.Where(r => r.Traversal != TraversalKind.Open).Select(r => r.Id)), "generated gate coverage");
        GameDefinitions.Require(state.Hazards is not null && state.Hazards.All(h => floor.Cells.Contains(h.Cell)
            && double.IsFinite(h.Phase) && h.Phase >= 0), "generated hazard state");
        GameDefinitions.Require(state.Supplies is not null && state.Supplies.All(s => floor.Cells.Contains(s.Cell)
            && s.Quantity > 0 && !string.IsNullOrWhiteSpace(s.Item)), "generated supply state");
        GameDefinitions.Require(state.Plates.Length == state.Gates.Count(g => g.Traversal == TraversalKind.Locked)
            && state.Plates.Select(p => p.GateId).ToHashSet().SetEquals(state.Gates.Where(g => g.Traversal == TraversalKind.Locked).Select(g => g.Id))
            && state.Plates.Select(p => p.Owner).Distinct().Count() == state.Plates.Length
            && state.Plates.All(p => floor.Cells.Contains(p.Cell) && floor.Cells.Contains(p.WeightSource) && p.RequiredWeight > 0), "generated plate coverage");
        foreach (var gate in state.Gates)
        {
            var route = floor.Routes.Single(r => r.Id == gate.RouteId);
            GameDefinitions.Require(gate.Cell == route.Cells[route.Cells.Count / 2] && gate.Approach == route.Cells[route.Cells.Count / 2 - 1]
                && gate.Cell.ManhattanDistance(gate.Approach) == 1 && gate.Traversal == route.Traversal
                && gate.RequiredItem == route.RequiredItem && (!gate.Open || gate.Discovered), "generated gate binding");
        }
        foreach (var plate in state.Plates)
        {
            var gate = state.Gates.Single(g => g.Id == plate.GateId);
            var route = floor.Routes.Single(r => r.Id == gate.RouteId);
            GameDefinitions.Require(plate.Cell == route.Cells[0]
                && floor.Grants.Any(g => g.Item == gate.RequiredItem && g.Cell == plate.WeightSource), "generated plate binding");
        }
        GameDefinitions.Require(state.Keys.Length == RequiredKeys(floor).Length
            && state.Keys.Select(k => k.Entity).Distinct().Count() == state.Keys.Length
            && state.Keys.Select(k => k.Owner).Distinct().Count() == state.Keys.Length && state.Keys.Select(k => k.Item).ToHashSet().SetEquals(RequiredKeys(floor).Select(g => g.Item))
            && state.Keys.All(k => floor.Grants.Any(g => g.Item == k.Item && g.Cell == k.Cell)), "generated key binding");
    }
}
