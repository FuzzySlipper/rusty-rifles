using Rifles.Game.Characters;
using Rifles.Game.Combat;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Generation;
using Rifles.Game.Items;
using Rifles.Game.Magic;
using Rifles.Game.Party;
using Rifles.Procgen;
using Rifles.Procgen.Expeditions;

namespace Rifles.Game.Expedition;

// Only floor-owned facts sleep here. Travelling members, their items and spellbooks
// have one owner: the active expedition snapshot.
internal sealed record RetainedFloor(ulong Id, DungeonFloor Floor, ExplorationSnapshot Departure,
    PatrolSnapshot Actor, FeatureSnapshot Features, InventorySnapshot Inventory,
    ItemExplorationSnapshot ItemWorld, EnemySnapshot[] Enemies, WeaponStateSnapshot Weapons,
    FlightSnapshot[] Flights, DropSnapshot[] Drops, AllySnapshot[] Allies,
    MagicConditionSnapshot[] Conditions, GeneratedFeatureSnapshot GeneratedFeatures,
    EncounterPlacementResult EncounterPlacement)
{
    internal static RetainedFloor Capture(ExpeditionSnapshot state)
    {
        // Member packs and the shared party pack travel with the party;
        // anchors and combat drop, flight, and enemy-loot packs remain local.
        SavedPack[] packs = state.Inventory.Packs.Where(p => InventoryOwner.Parse(p.Owner.Key) is not (MemberOwner or PartyOwner)).ToArray();
        var items = packs.SelectMany(p => p.Items).Select(i => i.Id).ToHashSet();
        return new(state.FloorId, state.Floor, state.Exploration, state.Actor, state.Features,
            new(packs), state.ItemWorld with { OpenContainer = null }, state.Combat.Enemies,
            new(state.Combat.Weapons.Muskets.Where(m => items.Contains(m.Item)).ToArray()), state.Combat.Flights,
            state.Combat.Drops, state.Combat.Allies,
            state.Combat.Magic!.Conditions.Where(c => c.Target.StartsWith("enemy:", StringComparison.Ordinal)).ToArray(),
            state.GeneratedFeatures, state.EncounterPlacement);
    }

    /// <summary>
    /// Admits one frozen floor as a floor: floor-local consistency only, no
    /// entities, no party, no synthetic expedition. The thaw path revalidates
    /// the live merge through the active snapshot.
    /// </summary>
    internal static void Validate(RetainedFloor floor, GameDefinitions definitions, ResolvedExpedition intent,
        IReadOnlyDictionary<ulong, string> items, IReadOnlyDictionary<string, string> roster, ulong partyId)
    {
        ArgumentNullException.ThrowIfNull(floor);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(roster);
        FloorFacts.Validate(floor.Floor, floor.GeneratedFeatures, floor.EncounterPlacement,
            floor.Enemies, intent, definitions);
        GameDefinitions.Require(floor.Features.LanternRevision > 0 && floor.Features.LanternRevision <= uint.MaxValue, "retained lantern revision");
        ExplorationItems itemWorld = new(definitions.ItemExploration, floor.ItemWorld);
        itemWorld.Validate(floor.Floor);
        ItemInventory inventory = ItemInventory.Restore(definitions.Items, floor.Inventory);
        FloorFacts.ValidateAnchors(floor.GeneratedFeatures, floor.Drops, inventory, definitions, items);
        HashSet<string> expectedOwners = definitions.ItemExploration.Anchors.Select(anchor => anchor.Key)
            .Concat(floor.Enemies.Select(enemy => enemy.Owner))
            .Concat(floor.Flights.Where(flight => flight.Owner is not null).Select(flight => flight.Owner!))
            .Concat(floor.Drops.Select(drop => drop.Owner))
            .ToHashSet(StringComparer.Ordinal);
        GameDefinitions.Require(inventory.Owners.Select(o => o.Key).ToHashSet().SetEquals(expectedOwners), "retained inventory owners");
        FloorFacts.ValidatePackCapacities(inventory, itemWorld, definitions);
        ExpeditionCodec.ValidateWorldObstructions(floor.Floor, floor.Actor, floor.Features.Dressing, floor.ItemWorld, floor.GeneratedFeatures);
        _ = ExplorationState.Restore(floor.Departure, floor.Floor, definitions.Exploration);
        _ = PatrolActor.Restore(floor.Actor, floor.Floor, definitions.Exploration with { StepSeconds = definitions.Features.ActorStepSeconds });
        floor.Features.Dressing.Validate(floor.Floor);
        CombatRestore.ValidateEnemies(floor.Enemies, definitions, floor.Floor);
        foreach (EnemySnapshot enemy in floor.Enemies)
        {
            EnemySpawnDefinition spawn = definitions.Combat.Encounter.Single(candidate => candidate.Id == enemy.Spawn);
            EnemyDefinition definition = definitions.Combat.Enemy(spawn.Enemy);
            RiflesStats.AdmitStats(enemy.Stats, RiflesStats.ForVitality(definition.Vitality, definition.Vitality,
                definitions.Magic.EnemyResource, definitions.Magic.EnemyResource));
            _ = ExplorationState.Restore(enemy.Motion, floor.Floor, definitions.Exploration with { StepSeconds = definition.StepSeconds });
            ActionState action = ActionState.Restore(enemy.Action);
            GameDefinitions.Require(RiflesStats.TrackCurrent(enemy.Stats, RiflesStatIds.Vitality) > 0 || !action.Busy, "retained dead enemy activity");
            _ = EnemyBrain.FromSnapshot(definition.Brain, enemy.Brain, floor.Floor.Cells.ToHashSet());
        }
        ulong[] allyIds = [floor.Actor.Id, floor.Features.Dressing.ObserverId];
        CombatRestore.ValidateAllies(floor.Allies, definitions.Combat, allyIds);
        foreach (AllySnapshot ally in floor.Allies)
            RiflesStats.AdmitStats(ally.Stats, RiflesStats.ForMember(RiflesCombat.AllyDefinition(ally.Id, definitions)));
        // Member actions are not frozen here (the thaw adopts the live
        // party's); flights only need the travelling roster for shooters.
        HashSet<string> members = roster.Keys.ToHashSet(StringComparer.Ordinal);
        (ulong Id, string Owner, string Definition, bool Alive)[] enemies =
            floor.Enemies.Select(e => (e.Id, e.Owner, e.Definition, RiflesStats.TrackCurrent(e.Stats, RiflesStatIds.Vitality) > 0)).ToArray();
        FlightSnapshot[] flights = CombatRestore.RestoreFlights(floor.Flights, definitions, floor.Floor, inventory, members, partyId, enemies);
        CombatRestore.ValidateDropsAndCombatOwners(floor.Drops, flights, enemies, floor.Floor, inventory);
        _ = WeaponState.Restore(definitions.Items, floor.Weapons, inventory);
        HashSet<string> targets = enemies.Where(e => e.Alive)
            .Select(e => "enemy:" + e.Id).ToHashSet(StringComparer.Ordinal);
        MagicState.ValidateConditions(floor.Conditions, targets, definitions.Magic);
    }

    internal ExpeditionSnapshot Join(ExpeditionSnapshot party, ExplorationSnapshot pose)
    {
        SavedPack[] travelling = party.Inventory.Packs.Where(p => InventoryOwner.Parse(p.Owner.Key) is (MemberOwner or PartyOwner)).ToArray();
        var items = travelling.SelectMany(p => p.Items).Select(i => i.Id).ToHashSet();
        MagicSnapshot magic = party.Combat.Magic! with
        {
            Conditions = party.Combat.Magic!.Conditions.Where(c => !c.Target.StartsWith("enemy:", StringComparison.Ordinal))
                .Concat(Conditions).ToArray(),
        };
        CombatSnapshot combat = new(Enemies, party.Combat.Members,
            new(party.Combat.Weapons.Muskets.Where(m => items.Contains(m.Item)).Concat(Weapons.Muskets).ToArray()),
            Flights, Drops, Allies, 0, magic);
        return party with { FloorId = Id, Floor = Floor, Exploration = pose, Actor = Actor,
            Features = Features, Inventory = new(travelling.Concat(Inventory.Packs).ToArray()),
            ItemWorld = ItemWorld, Combat = combat, GeneratedFeatures = GeneratedFeatures,
            EncounterPlacement = EncounterPlacement };
    }
}
