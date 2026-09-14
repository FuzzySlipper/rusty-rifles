using Rifles.Game.Combat;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Generation;
using Rifles.Game.Items;
using Rifles.Game.Magic;
using Rifles.Game.Party;
using Rifles.Procgen;
using Rifles.Procgen.Expeditions;
using Rifles.Procgen.Generation;
using Rusty.Engine;

namespace Rifles.Game.Expedition;

/// <summary>Builds a complete, inactive floor snapshot without changing the active expedition.</summary>
internal static class FloorFactory
{
    internal static ExpeditionSnapshot Create(IEngineContext engine, DungeonMaterialCache materialCache, GameDefinitions definitions,
        ResolvedExpedition intent, string floorKey, Guid runId, ulong partyId, string preset,
        ref ulong nextObjectId, Func<ulong> allocateLightId, DungeonFloor? resolvedFloor = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(materialCache);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentException.ThrowIfNullOrWhiteSpace(floorKey);
        ArgumentNullException.ThrowIfNull(allocateLightId);
        if (runId == Guid.Empty) throw new InvalidDataException("Expedition run identity is required.");
        if (partyId == 0 || partyId > uint.MaxValue) throw new InvalidDataException("Invalid party identity.");

        ResolvedFloorIntent resolved = intent.Floors.SingleOrDefault(f => f.Id == floorKey)
            ?? throw new InvalidDataException("Unknown expedition floor: " + floorKey);
        ulong candidateNextObjectId = nextObjectId;
        ulong Allocate()
        {
            if (candidateNextObjectId == 0 || candidateNextObjectId > uint.MaxValue)
                throw new InvalidOperationException("Expedition object identity space exhausted.");
            return checked(candidateNextObjectId++);
        }

        // Floor identity is allocated before any durable floor-owned fact.
        ulong floorId = Allocate();
        if (floorId == partyId) throw new InvalidDataException("Floor and party identities must differ.");

        DungeonFloor floor = resolvedFloor ?? DungeonFloor.Generate(resolved, definitions.Generation.Policy, definitions.Rooms,
            definitions.Generation.Elevation).WithArchitecture(definitions.Architecture);
        if (floor.IntentFloorId != resolved.Id || floor.Seed != resolved.Candidate.Seed
            || floor.IntentGraphIdentity != Rifles.Procgen.CanonicalIdentity.Hash(resolved.Candidate))
            throw new InvalidDataException("Resolved floor does not belong to the requested expedition floor.");
        PartyState party = new(definitions.Characters.GetPreset(preset));
        ExplorationState exploration = new(floor.Entrance, definitions.Exploration);

        using DungeonScene scene = new(engine, materialCache, floor, definitions.Exploration, definitions.Appearance,
            allocateLightId, definitions.ItemExploration);

        PatrolActor actor = PatrolActor.Create(Allocate(), floor,
            definitions.Exploration with { StepSeconds = definitions.Features.ActorStepSeconds }, definitions.Features);
        FeatureSnapshot features = new(Allocate(), Allocate(), 1, true, false,
            RoomDressing.Create(floor, actor, definitions.Art, Allocate));

        ExplorationItems itemWorld = ExplorationItems.Create(definitions.ItemExploration, floor, features.Dressing, actor, Allocate);
        IEnumerable<PackOwner> memberPacks = party.Members.Select(member => new PackOwner(Allocate(), "member:" + member.Definition.Id,
            definitions.Items.Backpack.Mass, definitions.Items.Backpack.Space));
        IEnumerable<PackOwner> anchorPacks = itemWorld.Anchors.Select(anchor =>
        {
            PackDefinition capacity = anchor.Key == "crate" ? definitions.Items.Container : definitions.Items.Anchor;
            return new PackOwner(anchor.Id, anchor.Key, capacity.Mass, capacity.Space);
        });
        ItemInventory inventory = new(definitions.Items, memberPacks.Concat(anchorPacks));
        inventory.GrantStarting(Allocate, preset);
        ApplyEquipment(party, inventory);
        scene.SetDoor(itemWorld.Capture().Door, false);

        MovementGrid movement = new(floor.Cells.ToHashSet(), scene.AdmitStep, definitions.Crowd);
        foreach (FloorConnector connector in floor.Connectors)
            movement.SetClearance(connector.From, connector.To, connector.Clearance);
        exploration.Bind(movement, partyId);
        actor.Bind(movement);
        features.Dressing.Bind(movement);
        ConfigureDoorClearance(movement, floor, itemWorld.Capture().Door, definitions.Combat.DoorClearance);

        Dictionary<string, GridPoint> drops = [];
        GeneratedFeatureSnapshot generatedFeatures = CreateGeneratedFeatures(intent, floor, definitions, inventory, drops, scene, Allocate);
        EncounterPlacementResult encounterPlacement = CreateEnemies(floor, definitions, inventory, itemWorld, movement,
            generatedFeatures, drops, Allocate, out EnemyState[] enemies);
        generatedFeatures = AddRouteSupplies(floor, definitions, inventory, encounterPlacement, drops, generatedFeatures, Allocate);

        MagicState magic = new(definitions.Magic, party.Members.Select(member => member.Definition.Id));
        CombatSnapshot combat = new(enemies.Select(enemy => enemy.Capture()).ToArray(),
            party.Members.Select(member => new MemberActionSnapshot(member.Definition.Id, new ActionState().Capture())).ToArray(), [], [],
            drops.Select(drop => new DropSnapshot(drop.Key, drop.Value)).ToArray(),
            [new(actor.Id, definitions.Combat.AllyVitality), new(features.Dressing.ObserverId, definitions.Combat.AllyVitality)], 0, magic.Capture());

        ExpeditionSnapshot snapshot = new(runId, floorId, partyId, candidateNextObjectId, floor, exploration.Capture(),
            party.Members.Select(member => member.Definition).ToArray(), party.Capture().ToArray(), false,
            party.Members[0].Definition.Id, actor.Capture(), features, preset, inventory.Capture(), itemWorld.Capture(), combat,
            intent, generatedFeatures, encounterPlacement);
        _ = ExpeditionCodec.Validate(snapshot, definitions);
        nextObjectId = candidateNextObjectId;
        return snapshot;
    }

    private static void ApplyEquipment(PartyState party, ItemInventory inventory)
    {
        foreach (PartyMemberState member in party.Members)
        {
            var bonus = inventory.Bonuses("member:" + member.Definition.Id);
            member.SetEquipmentBonuses(bonus.Power, bonus.Defense);
        }
    }

    private static void ConfigureDoorClearance(MovementGrid movement, DungeonFloor floor, GridPoint door, float clearance)
    {
        foreach (CardinalDirection direction in CardinalDirections.Ordered)
        {
            GridPoint neighbor = door + direction.Offset();
            if (floor.Cells.Contains(neighbor)) movement.SetClearance(door, neighbor, clearance);
        }
    }

    private static GeneratedFeatureSnapshot CreateGeneratedFeatures(ResolvedExpedition intent, DungeonFloor floor,
        GameDefinitions definitions, ItemInventory inventory, Dictionary<string, GridPoint> drops, DungeonScene scene, Func<ulong> allocate)
    {
        GeneratedGate[] gates = GeneratedFeatures.Resolve(floor, allocate);
        List<GeneratedKey> keys = [];
        foreach (FloorGrant grant in GeneratedFeatures.RequiredKeys(floor))
        {
            ulong packId = allocate();
            string owner = "combat:flight:" + packId;
            inventory.RegisterOwner(new PackOwner(packId, owner, definitions.Combat.DropCapacity.Mass, definitions.Combat.DropCapacity.Space));
            ulong entity = allocate();
            inventory.Grant(owner, definitions.GeneratedFeatures.KeyItem, 1, () => entity);
            drops.Add(owner, grant.Cell);
            keys.Add(new GeneratedKey(grant.Item, entity, owner, grant.Cell));
        }

        List<GeneratedPlate> plates = [];
        foreach (GeneratedGate gate in gates.Where(gate => gate.Traversal == TraversalKind.Locked))
        {
            var route = floor.Routes.Single(route => route.Id == gate.RouteId);
            GeneratedKey key = keys.Single(key => key.Item == gate.RequiredItem);
            ulong packId = allocate();
            string owner = "combat:flight:" + packId;
            ulong plateId = allocate();
            inventory.RegisterOwner(new PackOwner(packId, owner, definitions.Combat.DropCapacity.Mass,
                definitions.Combat.DropCapacity.Space, "Counterweight plate"));
            drops.Add(owner, route.Cells[0]);
            inventory.Grant(key.Owner, definitions.GeneratedFeatures.WeightItem, 1, allocate);
            plates.Add(new GeneratedPlate(plateId, gate.Id, owner, route.Cells[0], key.Cell, definitions.GeneratedFeatures.PlateWeight));
        }

        HashSet<string> hazardNodes = intent.Floors.Single(floorIntent => floorIntent.Id == floor.IntentFloorId).Candidate.Graph.Nodes
            .Where(node => node.Kind == NodeKind.Hazard).Select(node => node.Id).ToHashSet();
        GeneratedHazard[] hazards = floor.Rooms.Where(room => hazardNodes.Contains(room.NodeId)).Select(room =>
            new GeneratedHazard(allocate(), room.NodeId, room.Cells.OrderBy(cell => cell.ManhattanDistance(room.Cells[room.Cells.Length / 2])).First(),
                definitions.Hazards.ActiveSeconds, false, false)).ToArray();
        var result = new GeneratedFeatureSnapshot(1, gates, keys.ToArray(), [], hazards, plates.ToArray());
        var progression = FloorProgression.Inspect(floor, gates, result.Plates);
        if (!progression.Accepted)
            throw new InvalidDataException("Generated progression rejected: " + string.Join(", ", progression.Diagnostics));
        foreach (GeneratedGate gate in gates) scene.SetDoor(gate.Cell, false);
        return result;
    }

    private static EncounterPlacementResult CreateEnemies(DungeonFloor floor, GameDefinitions definitions, ItemInventory inventory,
        ExplorationItems itemWorld, MovementGrid movement, GeneratedFeatureSnapshot generatedFeatures,
        Dictionary<string, GridPoint> drops, Func<ulong> allocate, out EnemyState[] enemies)
    {
        HashSet<GridPoint> excluded = floor.Cells.Where(movement.Occupied).Concat(generatedFeatures.Gates.Select(gate => gate.Cell))
            .Concat(generatedFeatures.Hazards.Select(hazard => hazard.Cell)).Concat(floor.Grants.Select(grant => grant.Cell))
            .Append(itemWorld.Capture().Door).ToHashSet();
        EncounterPlacementResult placement = new EncounterPlacementResolver(definitions.EncounterPlacement).Resolve(floor.Seed, floor,
            definitions.Combat, definitions.Crowd, excluded);
        if (!placement.Accepted)
            throw new InvalidDataException("Encounter placement rejected: " + string.Join(", ", placement.Rejections.Select(rejection => rejection.Code + ": " + rejection.Detail)));

        List<EnemyState> created = [];
        foreach (var placed in placement.Instances)
        {
            EnemySpawnDefinition spawn = definitions.Combat.Encounter.Single(candidate => candidate.Id == placed.SpawnId);
            EnemyDefinition definition = definitions.Combat.Enemy(spawn.Enemy);
            ulong id = allocate();
            string owner = "combat:enemy:" + id;
            inventory.RegisterOwner(new PackOwner(allocate(), owner, definitions.Combat.DropCapacity.Mass, definitions.Combat.DropCapacity.Space));
            foreach (StartingItem loot in definition.Loot) inventory.Grant(owner, loot.Definition, loot.Quantity, allocate);
            ExplorationState motion = new(placed.Cell, definitions.Exploration with { StepSeconds = definition.StepSeconds });
            GridPoint[] patrol = PatrolRoute(floor, itemWorld.Capture().Door, placed.Cell, spawn);
            EnemyState enemy = new(new EnemySnapshot(id, definition.Id, motion.Capture(), definition.Vitality, null, 0, false, false,
                owner, new EnemyBrain(definition.Brain, placed.Cell, patrol).Capture(), spawn.Id, definitions.Magic.EnemyResource),
                definition, floor, definitions.Exploration, definitions.Magic.EnemyResource);
            enemy.Motion.Bind(movement, id, definition.Footprint, definition.Faction, definition.Share);
            GameDefinitions.Require(enemy.Motion.Capture().Placement == placed.PlacementId, "resolved enemy crowd slot");
            created.Add(enemy);
        }
        enemies = created.ToArray();
        return placement;
    }

    private static GridPoint[] PatrolRoute(DungeonFloor floor, GridPoint door, GridPoint home, EnemySpawnDefinition definition)
    {
        GridPoint[] route = definition.PatrolOffsets.Select(offset => home + new GridPoint(offset[0], offset[1]))
            .Where(cell => floor.Cells.Contains(cell) && cell != door).ToArray();
        if (route.Length == 0) throw new InvalidDataException("Enemy patrol has no floor cells: " + definition.Id);
        return route;
    }

    private static GeneratedFeatureSnapshot AddRouteSupplies(DungeonFloor floor, GameDefinitions definitions, ItemInventory inventory,
        EncounterPlacementResult placement, Dictionary<string, GridPoint> drops, GeneratedFeatureSnapshot features, Func<ulong> allocate)
    {
        ResolvedSupply[] supplies = RouteSupplies.Resolve(floor, definitions.RouteSupplies,
            placement.Instances.Sum(instance => definitions.Combat.Enemy(instance.EnemyId).Vitality));
        foreach (ResolvedSupply supply in supplies)
        {
            ulong packId = allocate();
            string owner = "combat:flight:" + packId;
            inventory.RegisterOwner(new PackOwner(packId, owner, definitions.Combat.DropCapacity.Mass, definitions.Combat.DropCapacity.Space));
            inventory.Grant(owner, supply.Item, supply.Quantity, allocate);
            drops.Add(owner, supply.Cell);
        }
        return features with { Supplies = supplies };
    }
}
