using Rifles.Procgen.Generation;

namespace Rifles.Game.Dungeon;

/// <summary>Generation-time placement admission, not runtime actor navigation.</summary>
internal static class DressingPlacement
{
    internal static bool CanBlock(DungeonFloor floor, IEnumerable<GridPoint> occupied)
    {
        var blocked = occupied.ToHashSet();
        HashSet<GridPoint> protectedCells = [floor.Entrance, floor.Exit,
            .. floor.Rooms.SelectMany(room => room.Thresholds),
            .. floor.Routes.SelectMany(route => route.Cells.Concat(route.AdditionalCells ?? [])),
            .. floor.Grants.Select(grant => grant.Cell),
            .. floor.Connectors.SelectMany(connector => new[] { connector.From, connector.To })];
        if (blocked.Overlaps(protectedCells)) return false;
        var remaining = floor.Cells.Where(cell => !blocked.Contains(cell)).ToHashSet();
        return ResolvedGridValidation.IsConnected(remaining, floor.Entrance);
    }
}
