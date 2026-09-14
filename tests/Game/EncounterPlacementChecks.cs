using Rifles.Game.Combat;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Procgen.Generation;

internal static class EncounterPlacementChecks
{
    internal static void Run(GameDefinitions definitions)
    {
        EncounterPlacementDefinition authored = EncounterPlacementDefinition.Load(path =>
            File.ReadAllBytes(Path.Combine(Path.GetFullPath("content"), path)));
        authored.ValidateAgainst(definitions.Combat, definitions.Crowd);

        DungeonFloor floor = DungeonFloor.Generate(definitions.Generation.Seed, definitions.Generation, definitions.Rooms);
        HashSet<GridPoint> excluded = [floor.Entrance, floor.Exit];
        EncounterPlacementResolver resolver = new(authored);
        EncounterPlacementResult result = resolver.Resolve(floor.Seed, floor, definitions.Combat, definitions.Crowd, excluded);
        Require(result.Accepted, "Authored encounter placement must accept the generated entrance floor: "
            + string.Join(", ", result.Rejections.Select(rejection => rejection.Code)));
        result.Validate(floor, definitions.Combat, definitions.Crowd, authored);
        Require(result.Instances.Length == authored.Groups.Sum(group => group.Members.Length), "Every authored combat spawn receives one resolved instance.");
        Require(result.Instances.Select(instance => instance.Role).Distinct().Count() == 2, "Resolved encounter mixes melee and ranged roles.");
        Require(result.Instances.Select(instance => instance.EnemyId).Distinct().Count() == 6, "Resolved encounter retains individual enemy identities.");
        Require(result.Instances.Any(instance => definitions.Combat.Enemy(instance.EnemyId).Footprint == "large"), "Resolved encounter includes the cell-filling footprint.");
        Require(result.Instances.All(instance => instance.ReachableAttackPositions.Length > 0), "Every instance has a reachable attack position.");
        Require(result.Instances.All(instance => instance.ReachableAttackPositions.All(position => floor.Level(position) == floor.Level(instance.Cell))),
            "Attack positions stay on the enemy's traversable level.");
        Require(result.Instances.All(instance => instance.Cell.ManhattanDistance(floor.Entrance) >= authored.Tuning.MinimumArrivalDistance),
            "Every instance respects the safe arrival distance.");

        EncounterPlacementResult replay = new EncounterPlacementResolver(authored with { Groups = authored.Groups.Reverse().ToArray() })
            .Resolve(floor.Seed, floor, definitions.Combat, definitions.Crowd, excluded);
        Require(replay.Accepted && replay.Identity == result.Identity, "Placement is stable when authored group order changes.");
        Require(replay.Instances.Select(instance => instance.Cell).SequenceEqual(result.Instances.Select(instance => instance.Cell)),
            "Placement cells replay exactly for the same seed and floor.");

        EncounterPlacementDefinition missingRoom = authored with
        {
            Groups = [authored.Groups[0] with { Id = "missing-room-group", RoomFunctions = ["missing-room"] }],
        };
        EncounterPlacementResult rejected = new EncounterPlacementResolver(missingRoom)
            .Resolve(floor.Seed, floor, definitions.Combat, definitions.Crowd, excluded);
        Require(!rejected.Accepted && rejected.Instances.Length == 0
            && rejected.Rejections.Any(rejection => rejection.Code == "no_matching_room"),
            "A required room-role mismatch rejects with an actionable reason and no partial instances.");

        EncounterPlacementDefinition tinyBudget = authored with
        {
            Tuning = authored.Tuning with { MaxReachabilityCells = 1 },
        };
        EncounterPlacementResult bounded = new EncounterPlacementResolver(tinyBudget)
            .Resolve(floor.Seed, floor, definitions.Combat, definitions.Crowd, excluded);
        Require(!bounded.Accepted && bounded.Rejections.Any(rejection => rejection.Code == "reachability_budget_exhausted"),
            "Reachability inspection rejects at its authored bound.");
        Console.WriteLine("Encounter placement checks passed: deterministic roles, safe arrivals, reachable attack positions, crowd footprints, and bounded rejection reasons.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
