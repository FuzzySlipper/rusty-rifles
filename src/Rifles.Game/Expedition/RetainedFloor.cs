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
    ItemExplorationSnapshot ItemWorld, EnemySnapshot[] Enemies, ulong[] LoadedWeapons,
    FlightSnapshot[] Flights, DropSnapshot[] Drops, AllySnapshot[] Allies,
    MagicConditionSnapshot[] Conditions, GeneratedFeatureSnapshot GeneratedFeatures,
    EncounterPlacementResult EncounterPlacement)
{
    internal static RetainedFloor Capture(ExpeditionSnapshot state)
    {
        // Member packs and the shared party pack travel with the party;
        // retained floors keep anchors and drops only.
        SavedPack[] packs = state.Inventory.Packs.Where(p => InventoryOwner.Parse(p.Owner.Key) is not (MemberOwner or PartyOwner)).ToArray();
        var items = packs.SelectMany(p => p.Items).Select(i => i.Id).ToHashSet();
        return new(state.FloorId, state.Floor, state.Exploration, state.Actor, state.Features,
            new(packs), state.ItemWorld with { OpenContainer = null }, state.Combat.Enemies,
            state.Combat.LoadedWeapons.Where(items.Contains).ToArray(), state.Combat.Flights,
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
        IReadOnlyDictionary<ulong, string> items, IReadOnlyDictionary<string, string> roster)
    {
        ArgumentNullException.ThrowIfNull(floor);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(roster);
        floor.Floor.Validate();
        floor.EncounterPlacement.Validate(floor.Floor, definitions.Combat, definitions.Crowd);
        GameDefinitions.Require(floor.EncounterPlacement.Accepted
            && floor.Enemies.Select(e => e.Spawn).ToHashSet(StringComparer.Ordinal)
                .SetEquals(floor.EncounterPlacement.Instances.Select(e => e.SpawnId)), "retained encounter roster");
        Generation.GeneratedFeatures.Validate(floor.GeneratedFeatures, floor.Floor);
        GameDefinitions.Require(floor.GeneratedFeatures.Hazards.All(h => h.Phase < definitions.Hazards.PeriodSeconds), "retained hazard phase");
        ResolvedFloorIntent? floorIntent = intent.Floors.SingleOrDefault(f => f.Id == floor.Floor.IntentFloorId);
        GameDefinitions.Require(floorIntent is not null && floor.Floor.Seed == floorIntent.Candidate.Seed
            && floor.Floor.IntentGraphIdentity == CanonicalIdentity.Hash(floorIntent.Candidate), "retained floor intent identity");
        GameDefinitions.Require(floor.Floor.Rooms.Select(r => r.NodeId).ToHashSet(StringComparer.Ordinal)
            .SetEquals(floorIntent!.Candidate.Graph.Nodes.Select(n => n.Id)), "retained room graph coverage");
        var hazardNodes = floorIntent.Candidate.Graph.Nodes.Where(n => n.Kind == NodeKind.Hazard).Select(n => n.Id).ToHashSet();
        GameDefinitions.Require(floor.GeneratedFeatures.Hazards.Length == hazardNodes.Count
            && floor.GeneratedFeatures.Hazards.Select(h => h.NodeId).ToHashSet().SetEquals(hazardNodes)
            && floor.GeneratedFeatures.Hazards.All(h => floor.Floor.Rooms.Any(r => r.NodeId == h.NodeId && r.Cells.Contains(h.Cell))),
            "retained hazard graph coverage");
        GameDefinitions.Require(floor.Features.LanternRevision > 0 && floor.Features.LanternRevision <= uint.MaxValue, "retained lantern revision");
        ExplorationItems itemWorld = new(definitions.ItemExploration, floor.ItemWorld);
        itemWorld.Validate(floor.Floor);
        ItemInventory inventory = ItemInventory.Restore(definitions.Items, floor.Inventory);
        foreach (var plate in floor.GeneratedFeatures.Plates)
            GameDefinitions.Require(floor.Drops.Any(d => d.Owner == plate.Owner && d.Cell == plate.Cell)
                && inventory.Owner(plate.Owner).Label == "Counterweight plate", "retained counterweight anchor");
        foreach (var key in floor.GeneratedFeatures.Keys)
        {
            GameDefinitions.Require(floor.Drops.Any(d => d.Owner == key.Owner && d.Cell == key.Cell), "retained key source anchor");
            var carried = inventory.Owners.SelectMany(o => inventory.Items(o.Key)).Where(i => i.Entity == key.Entity).ToArray();
            GameDefinitions.Require(items.GetValueOrDefault(key.Entity) == definitions.GeneratedFeatures.KeyItem
                && carried.Length == 1 && carried[0].Definition == definitions.GeneratedFeatures.KeyItem,
                "retained generated key identity");
        }
        string[] expectedOwners = definitions.ItemExploration.Anchors.Select(a => a.Key).ToArray();
        GameDefinitions.Require(inventory.Owners.Select(o => o.Key).ToHashSet().SetEquals(expectedOwners), "retained inventory owners");
        foreach (PackOwner owner in inventory.Owners)
        {
            PackDefinition capacity = owner.Key == "crate" ? definitions.Items.Container : definitions.Items.Anchor;
            GameDefinitions.Require(owner.MassCapacity == capacity.Mass && owner.SpaceCapacity == capacity.Space, "retained pack capacity");
            GameDefinitions.Require(owner.Id == itemWorld.Anchor(InventoryOwner.Parse(owner.Key) is AnchorOwner anchor ? anchor.Anchor : owner.Key).Id, "retained anchor owner");
        }
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
        FlightSnapshot[] flights = CombatRestore.RestoreFlights(floor.Flights, definitions, floor.Floor, inventory, members, 0, enemies);
        CombatRestore.ValidateDropsAndCombatOwners(floor.Drops, flights, enemies, floor.Floor, inventory);
        CombatRestore.ValidateLoadedWeapons(floor.LoadedWeapons, definitions, inventory);
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
            party.Combat.LoadedWeapons.Where(items.Contains).Concat(LoadedWeapons).ToArray(),
            Flights, Drops, Allies, 0, magic);
        return party with { FloorId = Id, Floor = Floor, Exploration = pose, Actor = Actor,
            Features = Features, Inventory = new(travelling.Concat(Inventory.Packs).ToArray()),
            ItemWorld = ItemWorld, Combat = combat, GeneratedFeatures = GeneratedFeatures,
            EncounterPlacement = EncounterPlacement };
    }
}
