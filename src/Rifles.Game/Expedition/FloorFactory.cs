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
using Rifles.Procgen.Generation;
using Rusty.Engine;

namespace Rifles.Game.Expedition;

/// <summary>Pure floor-construction helpers shared by live floor mounts. Fresh floors mount
/// directly from these builders; the snapshot roundtrip is gone.</summary>
internal static class FloorFactory
{
    internal static void ConfigureDoorClearance(MovementGrid movement, DungeonFloor floor, GridPoint door, float clearance)
    {
        foreach (CardinalDirection direction in CardinalDirections.Ordered)
        {
            GridPoint neighbor = door + direction.Offset();
            if (floor.Cells.Contains(neighbor)) movement.SetClearance(door, neighbor, clearance);
        }
    }

    internal static GeneratedFeatureSnapshot CreateGeneratedFeatures(ResolvedExpedition intent, DungeonFloor floor,
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
            inventory.Grant(InventoryOwner.Parse(owner), definitions.GeneratedFeatures.KeyItem, 1, () => entity);
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
            inventory.Grant(InventoryOwner.Parse(key.Owner), definitions.GeneratedFeatures.WeightItem, 1, allocate);
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

    internal static EncounterPlacementResult CreateEnemies(DungeonFloor floor, GameDefinitions definitions, ItemInventory inventory,
        ExplorationItems itemWorld, MovementGrid movement, GeneratedFeatureSnapshot generatedFeatures,
        Dictionary<string, GridPoint> drops, Func<ulong> allocate, CharacterEntities entities, out EnemyState[] enemies)
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
            foreach (StartingItem loot in definition.Loot) inventory.Grant(InventoryOwner.Parse(owner), loot.Definition, loot.Quantity, allocate);
            ExplorationState motion = new(placed.Cell, definitions.Exploration with { StepSeconds = definition.StepSeconds });
            GridPoint[] patrol = PatrolRoute(floor, itemWorld.Capture().Door, placed.Cell, spawn);
            EnemyState enemy = new(new EnemySnapshot(id, definition.Id, motion.Capture(), definition.Vitality, null, 0, false, false,
                owner, new EnemyBrain(definition.Brain, placed.Cell, patrol).Capture(), spawn.Id, definitions.Magic.EnemyResource),
                definition, floor, definitions.Exploration, entities, definitions.Magic.EnemyResource);
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

    internal static GeneratedFeatureSnapshot AddRouteSupplies(DungeonFloor floor, GameDefinitions definitions, ItemInventory inventory,
        EncounterPlacementResult placement, Dictionary<string, GridPoint> drops, GeneratedFeatureSnapshot features, Func<ulong> allocate)
    {
        ResolvedSupply[] supplies = RouteSupplies.Resolve(floor, definitions.RouteSupplies,
            placement.Instances.Sum(instance => definitions.Combat.Enemy(instance.EnemyId).Vitality));
        foreach (ResolvedSupply supply in supplies)
        {
            ulong packId = allocate();
            string owner = "combat:flight:" + packId;
            inventory.RegisterOwner(new PackOwner(packId, owner, definitions.Combat.DropCapacity.Mass, definitions.Combat.DropCapacity.Space));
            inventory.Grant(InventoryOwner.Parse(owner), supply.Item, supply.Quantity, allocate);
            drops.Add(owner, supply.Cell);
        }
        return features with { Supplies = supplies };
    }
}
