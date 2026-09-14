using Rifles.Game.Combat;
using Rifles.Game.Dungeon;
using Rifles.Game.Generation;
using Rifles.Game.Items;
using Rifles.Game.Magic;

namespace Rifles.Game.Expedition;

// Only floor-owned facts sleep here. Travelling members, their items and spellbooks
// have one owner: the active expedition snapshot.
internal sealed record RetainedFloor(ulong Id, DungeonFloor Floor, ExplorationSnapshot Departure,
    PatrolSnapshot Actor, FeatureSnapshot Features, InventorySnapshot Inventory,
    ItemExplorationSnapshot ItemWorld, EnemySnapshot[] Enemies, ulong[] LoadedWeapons,
    FlightSnapshot[] Flights, DropSnapshot[] Drops, AllySnapshot[] Allies,
    MagicConditionSnapshot[] Conditions, GeneratedFeatureSnapshot GeneratedFeatures,
    EncounterPlacementResult EncounterPlacement)
{
    internal static RetainedFloor Capture(ExpeditionSnapshot state)
    {
        SavedPack[] packs = state.Inventory.Packs.Where(p => !ItemInventory.IsMember(p.Owner.Key)).ToArray();
        var items = packs.SelectMany(p => p.Items).Select(i => i.Id).ToHashSet();
        return new(state.FloorId, state.Floor, state.Exploration, state.Actor, state.Features,
            new(packs), state.ItemWorld with { OpenContainer = null }, state.Combat.Enemies,
            state.Combat.LoadedWeapons.Where(items.Contains).ToArray(), state.Combat.Flights,
            state.Combat.Drops, state.Combat.Allies,
            state.Combat.Magic!.Conditions.Where(c => c.Target.StartsWith("enemy:", StringComparison.Ordinal)).ToArray(),
            state.GeneratedFeatures, state.EncounterPlacement);
    }

    internal ExpeditionSnapshot Join(ExpeditionSnapshot party, ExplorationSnapshot pose)
    {
        SavedPack[] members = party.Inventory.Packs.Where(p => ItemInventory.IsMember(p.Owner.Key)).ToArray();
        var items = members.SelectMany(p => p.Items).Select(i => i.Id).ToHashSet();
        MagicSnapshot magic = party.Combat.Magic! with
        {
            Conditions = party.Combat.Magic!.Conditions.Where(c => !c.Target.StartsWith("enemy:", StringComparison.Ordinal))
                .Concat(Conditions).ToArray(),
        };
        CombatSnapshot combat = new(Enemies, party.Combat.Members,
            party.Combat.LoadedWeapons.Where(items.Contains).Concat(LoadedWeapons).ToArray(),
            Flights, Drops, Allies, 0, magic);
        return party with { FloorId = Id, Floor = Floor, Exploration = pose, Actor = Actor,
            Features = Features, Inventory = new(members.Concat(Inventory.Packs).ToArray()),
            ItemWorld = ItemWorld, Combat = combat, GeneratedFeatures = GeneratedFeatures,
            EncounterPlacement = EncounterPlacement };
    }
}
