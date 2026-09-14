using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Procgen;
using Rifles.Procgen.Generation;

namespace Rifles.Game.Generation;

internal sealed record ElevationDefinition(string[] RaisedRoomFunctions, string[] PitRoomFunctions,
    int MaximumBridges, int MaximumPits, float BridgeClearance, float StairClearance, long PitDamage)
{
    internal void Validate() => GameDefinitions.Require(RaisedRoomFunctions is not null && PitRoomFunctions is not null
        && MaximumBridges >= 0 && MaximumPits >= 0 && float.IsFinite(BridgeClearance) && BridgeClearance > 0 && float.IsFinite(StairClearance) && StairClearance > 0 && PitDamage >= 0, "floor elevations");
}
internal enum FloorConnectorKind { Stair, Bridge, Pit }
internal sealed record ElevatedCell(GridPoint Cell, int Level);
internal sealed record FloorConnector(string Id, FloorConnectorKind Kind, GridPoint From, GridPoint To, float Clearance, long Damage);

internal static class FloorElevation
{
    internal static (ElevatedCell[] Cells, FloorConnector[] Connectors) Resolve(DungeonFloor floor, ElevationDefinition definition)
    {
        Dictionary<GridPoint, int> levels = [];
        foreach (var room in floor.Rooms.Where(r => definition.RaisedRoomFunctions.Contains(r.Function)
            && !r.Cells.Contains(floor.Entrance)))
            foreach (var cell in room.Cells) levels[cell] = 1;
        var protectedCells = floor.Grants.Select(g => g.Cell).Concat(floor.Rooms.SelectMany(r => r.Thresholds))
            .Append(floor.Entrance).Append(floor.Exit).ToHashSet();
        HashSet<GridPoint> bridges = [];
        foreach (var route in floor.Routes.Where(r => r.Traversal == TraversalKind.Open).OrderBy(r => r.Id))
        {
            if (bridges.Count >= definition.MaximumBridges) break;
            var interior = route.Cells.Skip(1).SkipLast(1).Where(c => !protectedCells.Contains(c)
                && !floor.Rooms.Any(r => r.Cells.Contains(c))).ToArray();
            if (interior.Length == 0) continue;
            var cell = interior[interior.Length / 2]; levels[cell] = 1; bridges.Add(cell);
        }
        HashSet<GridPoint> pits = [];
        foreach (var room in floor.Rooms.Where(r => definition.PitRoomFunctions.Contains(r.Function)).OrderBy(r => r.RegionId))
        {
            if (pits.Count >= definition.MaximumPits) break;
            var candidates = room.Cells.Where(c => !protectedCells.Contains(c) && !levels.ContainsKey(c)
                && CardinalDirections.Ordered.All(d => room.Cells.Contains(c + d.Offset()))).ToArray();
            if (candidates.Length == 0) continue;
            var cell = candidates[candidates.Length / 2]; levels[cell] = -1; pits.Add(cell);
        }
        List<FloorConnector> connectors = [];
        var cells = floor.Cells.ToHashSet();
        foreach (var from in floor.Cells)
            foreach (var direction in CardinalDirections.Ordered)
            {
                var to = from + direction.Offset();
                if (!cells.Contains(to) || levels.GetValueOrDefault(from) == levels.GetValueOrDefault(to)) continue;
                if (Math.Abs(levels.GetValueOrDefault(from) - levels.GetValueOrDefault(to)) != 1)
                    throw new InvalidDataException("Elevation requires a single-level adjacent stair landing.");
                var kind = pits.Contains(to) ? FloorConnectorKind.Pit
                    : bridges.Contains(from) || bridges.Contains(to) ? FloorConnectorKind.Bridge : FloorConnectorKind.Stair;
                connectors.Add(new($"{floor.IntentFloorId}/{from.X},{from.Y}/{to.X},{to.Y}", kind, from, to,
                    kind == FloorConnectorKind.Bridge ? definition.BridgeClearance : definition.StairClearance, kind == FloorConnectorKind.Pit ? definition.PitDamage : 0));
            }
        return (levels.OrderBy(p => p.Key.Y).ThenBy(p => p.Key.X).Select(p => new ElevatedCell(p.Key, p.Value)).ToArray(), connectors.ToArray());
    }
}
