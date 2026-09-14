using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Procgen.Generation;

namespace Rifles.Game.Generation;

internal sealed record SupplyRule(string Item, ulong Quantity, string RoomFunction);
internal sealed record RouteSupplyDefinition(string Ammunition, double ExpectedDamagePerShot, double AmmunitionAllowance,
    ulong MaximumAmmunition, int MaximumPlacements, SupplyRule[] Supplies)
{
    internal void Validate()
    {
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(Ammunition) && double.IsFinite(ExpectedDamagePerShot)
            && ExpectedDamagePerShot > 0 && double.IsFinite(AmmunitionAllowance) && AmmunitionAllowance >= 1
            && MaximumAmmunition > 0 && MaximumPlacements > 0 && Supplies.Length < MaximumPlacements
            && Supplies.All(s => !string.IsNullOrWhiteSpace(s.Item) && s.Quantity > 0
                && !string.IsNullOrWhiteSpace(s.RoomFunction)), "route supplies");
    }
}
internal sealed record ResolvedSupply(string Item, ulong Quantity, GridPoint Cell, string Reason);

internal static class RouteSupplies
{
    internal static ResolvedSupply[] Resolve(DungeonFloor floor, RouteSupplyDefinition definition, long encounterVitality)
    {
        ulong ammunition = checked((ulong)Math.Ceiling(encounterVitality / definition.ExpectedDamagePerShot * definition.AmmunitionAllowance));
        if (ammunition > definition.MaximumAmmunition)
            throw new InvalidDataException($"Supply budget shortage: {ammunition} rifle rounds required by encounter allowance; cap {definition.MaximumAmmunition}.");
        List<ResolvedSupply> result = [];
        HashSet<GridPoint> used = [floor.Entrance, floor.Exit, .. floor.Grants.Select(g => g.Cell)];
        var arrival = floor.Rooms.Single(r => r.Cells.Contains(floor.Entrance));
        GridPoint Place(IEnumerable<GridPoint> candidates)
        {
            var cell = candidates.Where(c => !used.Contains(c)).OrderBy(c => c.ManhattanDistance(floor.Entrance))
                .ThenBy(c => c.Y).ThenBy(c => c.X).Select(c => (GridPoint?)c).FirstOrDefault()
                ?? throw new InvalidDataException("Supply placement exhausted: no free reachable room cell.");
            used.Add(cell); return cell;
        }
        if (ammunition > 0) result.Add(new(definition.Ammunition, ammunition, Place(arrival.Cells), "Arrival reserve based on accepted encounter vitality and authored shot allowance."));
        foreach (var rule in definition.Supplies)
        {
            var rooms = rule.RoomFunction == "arrival" ? new[] { arrival } : floor.Rooms.Where(r => r.Function == rule.RoomFunction).ToArray();
            if (rooms.Length == 0) continue; // This floor has no room with that optional supply role.
            result.Add(new(rule.Item, rule.Quantity, Place(rooms.SelectMany(r => r.Cells)), "Authored " + rule.RoomFunction + " supply."));
        }
        if (result.Count > definition.MaximumPlacements) throw new InvalidDataException("Supply placement quota exceeded.");
        return result.ToArray();
    }
}
