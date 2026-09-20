using Rifles.Game.Characters;
using Rifles.Game.Combat;
using Rifles.Game.Content;
using Rifles.Game.Items;
using Rusty.Engine.Mechanics;

namespace Rifles.Game.Expedition;

/// <summary>
/// The one enumeration of durable identities used for per-floor and
/// cross-run relationships. Genuine duplicate/dangling checks stay; the
/// enumeration itself lives here instead of scattered across validators.
/// </summary>
internal static class SaveIdentities
{
    internal static IEnumerable<ulong> Floor(RetainedFloor floor)
    {
        ArgumentNullException.ThrowIfNull(floor);
        return new[]
        {
            floor.Id, floor.Actor.Id, floor.Features.LanternId, floor.Features.ExitId,
            floor.Features.Dressing.BenchId, floor.Features.Dressing.CrateId, floor.Features.Dressing.ObserverId,
            floor.ItemWorld.DoorId, floor.ItemWorld.LeverId, floor.ItemWorld.PlateId,
        }.Concat(floor.GeneratedFeatures.Gates.Select(g => g.Id)).Concat(floor.GeneratedFeatures.Plates.Select(p => p.Id))
            .Concat(floor.GeneratedFeatures.Hazards.Select(h => h.Id)).Concat(floor.Enemies.Select(e => e.Id))
            .Concat(floor.Flights.Select(flight => flight.Id))
            .Concat(floor.Inventory.Packs.SelectMany(p => p.Items.Select(i => i.Id).Append(p.Owner.Id)));
    }

    internal static void RequireCrossFloorUnique(RunSnapshot run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var active = run.Active;
        var ids = new[] { RetainedFloor.Capture(active) }.Concat(run.Inactive).SelectMany(Floor)
            .Concat(active.Inventory.Packs
                .Where(p => InventoryOwner.Parse(p.Owner.Key) is (MemberOwner or PartyOwner))
                .SelectMany(p => p.Items.Select(i => i.Id).Append(p.Owner.Id)))
            .Append(active.PartyId).ToArray();
        GameDefinitions.Require(ids.Distinct().Count() == ids.Length && ids.All(i => i > 0 && i < active.NextObjectId), "cross-floor object identities");
    }

    internal static bool AllDefeated(IEnumerable<EnemySnapshot> enemies) =>
        enemies.All(e => RiflesStats.TrackCurrent(e.Stats, RiflesStatIds.Vitality) <= 0);
}
