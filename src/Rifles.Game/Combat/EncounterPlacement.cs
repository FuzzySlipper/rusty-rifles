using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Generation;
using Rifles.Procgen.Generation;

namespace Rifles.Game.Combat;

/// <summary>The tactical job an encounter member should make possible at spawn.</summary>
internal enum EncounterTacticalRole
{
    Melee,
    Ranged,
}

/// <summary>A named existing combat spawn with an authored difficulty cost.</summary>
internal sealed record EncounterMemberDefinition(string SpawnId, EncounterTacticalRole Role, int DifficultyCost);

/// <summary>One role and its members. Room matching is intentionally concrete and file-authored.</summary>
internal sealed record EncounterGroupDefinition(
    string Id,
    string[] RoomFunctions,
    string[] RoomTemplates,
    bool Required,
    int DifficultyBudget,
    EncounterMemberDefinition[] Members)
{
    internal void Validate()
    {
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(Id) && Id.Length <= 96, "encounter group identity");
        GameDefinitions.Require(RoomFunctions is { Length: > 0 } || RoomTemplates is { Length: > 0 }, "encounter group room selectors");
        GameDefinitions.Require((RoomFunctions ?? []).All(value => !string.IsNullOrWhiteSpace(value))
            && (RoomTemplates ?? []).All(value => !string.IsNullOrWhiteSpace(value)), "encounter group room selector values");
        GameDefinitions.Require(Members is { Length: > 0 }, "encounter group members");
        GameDefinitions.Require(DifficultyBudget > 0 && Members.Sum(member => member.DifficultyCost) <= DifficultyBudget,
            "encounter group difficulty budget");
        foreach (EncounterMemberDefinition member in Members)
        {
            GameDefinitions.Require(!string.IsNullOrWhiteSpace(member.SpawnId)
                && Enum.IsDefined(member.Role) && member.DifficultyCost > 0, "encounter member");
        }
    }

    internal bool Matches(ResolvedRoom room) =>
        (RoomFunctions is not { Length: > 0 } || RoomFunctions.Contains(room.Function, StringComparer.Ordinal))
        && (RoomTemplates is not { Length: > 0 } || RoomTemplates.Contains(room.TemplateId, StringComparer.Ordinal));
}

/// <summary>Bounded, file-authored encounter placement policy.</summary>
internal sealed record EncounterPlacementTuning(
    int MaxGroups,
    int MaxMembers,
    int MaxGroupsPerRoom,
    int MaxRoomCandidates,
    int MaxCandidateCellsPerMember,
    int MaxReachabilityCells,
    int MaxAttackPositionsPerMember,
    int MinimumArrivalDistance,
    int MinimumMeleeSpawnDistance,
    int MinimumRangedSpawnDistance,
    int MinimumRangedAttackDistance,
    int PreferredRangedAttackDistance,
    int MaximumRangedAttackDistance,
    int MaximumTotalDifficulty,
    bool RejectDirectArrivalLane)
{
    internal void Validate()
    {
        GameDefinitions.Require(MaxGroups is > 0 and <= 128 && MaxMembers is > 0 and <= 512
            && MaxGroupsPerRoom is > 0 and <= 32 && MaxRoomCandidates is > 0 and <= 128
            && MaxCandidateCellsPerMember is > 0 and <= 256 && MaxReachabilityCells is > 0 and <= 64_000_000
            && MaxAttackPositionsPerMember is > 0 and <= 256, "encounter placement budgets");
        GameDefinitions.Require(MinimumArrivalDistance >= 0 && MinimumMeleeSpawnDistance >= 0
            && MinimumRangedSpawnDistance >= 0 && MinimumRangedAttackDistance > 0
            && PreferredRangedAttackDistance >= MinimumRangedAttackDistance
            && MaximumRangedAttackDistance >= PreferredRangedAttackDistance
            && MaximumTotalDifficulty > 0, "encounter placement distances and difficulty");
    }
}

/// <summary>All authored encounter groups used by a generated floor.</summary>
internal sealed record EncounterPlacementDefinition(
    string Id,
    EncounterPlacementTuning Tuning,
    EncounterGroupDefinition[] Groups)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter() },
    };

    internal void Validate()
    {
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(Id) && Id.Length <= 96
            && Id.All(value => char.IsLower(value) || char.IsDigit(value) || value is '.' or '-' or '_'), "encounter placement identity");
        Tuning.Validate();
        GameDefinitions.Require(Groups is { Length: > 0 } && Groups.Length <= Tuning.MaxGroups
            && Groups.Select(group => group.Id).Distinct(StringComparer.Ordinal).Count() == Groups.Length, "encounter placement groups");
        foreach (EncounterGroupDefinition group in Groups) group.Validate();
        GameDefinitions.Require(Groups.Sum(group => group.Members.Length) <= Tuning.MaxMembers, "encounter placement member budget");
    }

    /// <summary>Loads the product-owned definition. Cross-domain references are checked separately.</summary>
    internal static EncounterPlacementDefinition Load(Func<string, ReadOnlyMemory<byte>> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        try
        {
            EncounterPlacementDefinition definition = JsonSerializer.Deserialize<EncounterPlacementDefinition>(
                read("definitions/encounter-placement.json").Span, Json)
                ?? throw new InvalidDataException("Document must not be null.");
            definition.Validate();
            return definition;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            throw new InvalidDataException("Content 'definitions/encounter-placement.json': " + error.Message, error);
        }
    }

    /// <summary>Validates spawn identity, role, and crowd references at the domain boundary.</summary>
    internal void ValidateAgainst(CombatDefinition combat, CrowdDefinition crowd)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(crowd);
        Validate();
        crowd.Validate();
        HashSet<string> referenced = new(StringComparer.Ordinal);
        int totalDifficulty = 0;
        foreach (EncounterGroupDefinition group in Groups)
        {
        foreach (EncounterMemberDefinition member in group.Members)
        {
            GameDefinitions.Require(referenced.Add(member.SpawnId), "encounter spawn is referenced once");
                EnemySpawnDefinition spawn = combat.Encounter.SingleOrDefault(value => value.Id == member.SpawnId)
                    ?? throw new InvalidDataException($"Encounter placement references unknown spawn '{member.SpawnId}'.");
                EnemyDefinition enemy = combat.Enemy(spawn.Enemy);
                GameDefinitions.Require(enemy.Attack == (member.Role == EncounterTacticalRole.Ranged
                    ? CombatActionKind.Fire : CombatActionKind.Melee), "encounter member tactical role " + member.SpawnId);
                _ = crowd.Footprint(enemy.Footprint);
                totalDifficulty = checked(totalDifficulty + member.DifficultyCost);
            }
        }
        GameDefinitions.Require(MathF.Floor(combat.Action(CombatActionKind.Melee).Range) >= 1
            && MathF.Floor(combat.Action(CombatActionKind.Fire).Range) >= Tuning.MinimumRangedAttackDistance,
            "encounter attack ranges");
        GameDefinitions.Require(totalDifficulty <= Tuning.MaximumTotalDifficulty, "encounter placement difficulty budget");
    }
}

internal sealed record EncounterPlacementRejection(string GroupId, string? SpawnId, string Code, string Detail);

/// <summary>One immutable, resolved enemy instance. The spawn id retains the authored combat/loot identity.</summary>
internal sealed record ResolvedEncounterInstance(
    string SpawnId,
    string EnemyId,
    string GroupId,
    string RoomId,
    GridPoint Cell,
    string PlacementId,
    EncounterTacticalRole Role,
    int DifficultyCost,
    GridPoint[] ReachableAttackPositions);

internal sealed record EncounterPlacementReport(
    int ConsideredGroups,
    int PlacedGroups,
    int ConsideredMembers,
    int PlacedMembers,
    int CandidateChecks,
    int ReachabilityCells,
    int TotalDifficulty);

/// <summary>Deterministic result which can be retained in a save without rerunning placement.</summary>
internal sealed record EncounterPlacementResult(
    bool Accepted,
    string DefinitionId,
    ulong Seed,
    string Identity,
    ResolvedEncounterInstance[] Instances,
    EncounterPlacementRejection[] Rejections,
    EncounterPlacementReport Report)
{
    internal void Validate(DungeonFloor floor, CombatDefinition combat, CrowdDefinition crowd,
        EncounterPlacementDefinition? definition = null)
    {
        ArgumentNullException.ThrowIfNull(floor);
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(crowd);
        if (definition is not null)
        {
            definition.ValidateAgainst(combat, crowd);
            GameDefinitions.Require(DefinitionId == definition.Id, "saved encounter placement definition");
        }
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(DefinitionId) && !string.IsNullOrWhiteSpace(Identity), "saved encounter placement identity");
        ResolvedEncounterInstance[] instances = Instances ?? throw new InvalidDataException("Saved encounter placement instances are required.");
        EncounterPlacementRejection[] rejections = Rejections ?? throw new InvalidDataException("Saved encounter placement rejections are required.");
        EncounterPlacementReport report = Report ?? throw new InvalidDataException("Saved encounter placement report is required.");
        GameDefinitions.Require(rejections.All(rejection => rejection is not null
            && !string.IsNullOrWhiteSpace(rejection.Code) && !string.IsNullOrWhiteSpace(rejection.Detail)), "saved encounter rejection details");
        GameDefinitions.Require(instances.All(instance => instance is not null), "saved encounter instances");
        GameDefinitions.Require(instances.Select(instance => instance.SpawnId).Distinct(StringComparer.Ordinal).Count() == instances.Length,
            "saved encounter spawn uniqueness");
        GameDefinitions.Require(report.ConsideredGroups >= 0 && report.PlacedGroups >= 0 && report.ConsideredMembers >= 0
            && report.PlacedMembers >= 0 && report.CandidateChecks >= 0 && report.ReachabilityCells >= 0 && report.TotalDifficulty >= 0,
            "saved encounter report values");
        HashSet<GridPoint> floorCells = floor.Cells.ToHashSet();
        Dictionary<GridEdge, float> connectorClearances = EncounterPlacementResolver.ConnectorClearances(floor);
        MovementGrid grid = new(floorCells, (_, _) => true, crowd);
        foreach (FloorConnector connector in floor.Connectors)
            grid.SetClearance(connector.From, connector.To, connector.Clearance);
        for (int index = 0; index < instances.Length; index++)
        {
            ResolvedEncounterInstance instance = instances[index];
            EnemySpawnDefinition spawn = combat.Encounter.SingleOrDefault(value => value.Id == instance.SpawnId)
                ?? throw new InvalidDataException("Saved encounter references unknown spawn '" + instance.SpawnId + "'.");
            EnemyDefinition enemy = combat.Enemy(spawn.Enemy);
            GameDefinitions.Require(instance.EnemyId == enemy.Id && instance.Role == (enemy.Attack == CombatActionKind.Fire
                ? EncounterTacticalRole.Ranged : EncounterTacticalRole.Melee), "saved encounter archetype");
            if (definition is not null)
            {
                GameDefinitions.Require(definition.Groups.Any(group => group.Id == instance.GroupId
                    && group.Members.Any(member => member.SpawnId == instance.SpawnId && member.Role == instance.Role
                        && member.DifficultyCost == instance.DifficultyCost)), "saved encounter group membership");
                int minimumArrival = Math.Max(definition.Tuning.MinimumArrivalDistance,
                    instance.Role == EncounterTacticalRole.Ranged ? definition.Tuning.MinimumRangedSpawnDistance : definition.Tuning.MinimumMeleeSpawnDistance);
                GameDefinitions.Require(instance.Cell.ManhattanDistance(floor.Entrance) >= minimumArrival,
                    "saved encounter arrival distance");
                int directRange = Math.Min(definition.Tuning.MaximumRangedAttackDistance,
                    (int)MathF.Floor(combat.Action(CombatActionKind.Fire).Range));
                GameDefinitions.Require(instance.Role != EncounterTacticalRole.Ranged || !definition.Tuning.RejectDirectArrivalLane
                    || !EncounterPlacementResolver.GridLineOfSight(floor, floorCells, floor.Entrance, instance.Cell)
                    || instance.Cell.ManhattanDistance(floor.Entrance) > directRange, "saved encounter arrival lane");
            }
            ResolvedRoom room = floor.Rooms.SingleOrDefault(value => value.RegionId == instance.RoomId)
                ?? throw new InvalidDataException("Saved encounter references unknown room '" + instance.RoomId + "'.");
            GameDefinitions.Require(room.Cells.Contains(instance.Cell) && !room.Thresholds.Contains(instance.Cell)
                && instance.Cell != floor.Entrance && instance.Cell != floor.Exit, "saved encounter cell");
            GameDefinitions.Require(instance.ReachableAttackPositions is { Length: > 0 }
                && instance.ReachableAttackPositions.All(floorCells.Contains), "saved encounter attack positions");
            float edgeClearance = crowd.Footprint(enemy.Footprint).EdgeClearance;
            GameDefinitions.Require(EncounterPlacementResolver.TryReachable(floor, floorCells, floor.Entrance,
                floorCells.Count, edgeClearance, connectorClearances, out HashSet<GridPoint> reachable)
                && reachable.Contains(instance.Cell), "saved encounter arrival route");
            int meleeRange = (int)MathF.Floor(combat.Action(CombatActionKind.Melee).Range);
            int rangedMaximum = (int)MathF.Floor(combat.Action(CombatActionKind.Fire).Range);
            int rangedMinimum = definition?.Tuning.MinimumRangedAttackDistance ?? 1;
            if (definition is not null)
                rangedMaximum = Math.Min(rangedMaximum, definition.Tuning.MaximumRangedAttackDistance);
            GameDefinitions.Require(instance.ReachableAttackPositions.All(position => reachable.Contains(position)
                && floor.Level(position) == floor.Level(instance.Cell)
                && (instance.Role == EncounterTacticalRole.Melee
                    ? position.ManhattanDistance(instance.Cell) > 0 && position.ManhattanDistance(instance.Cell) <= meleeRange
                    : position.ManhattanDistance(instance.Cell) >= rangedMinimum && position.ManhattanDistance(instance.Cell) <= rangedMaximum
                        && EncounterPlacementResolver.GridLineOfSight(floor, floorCells, position, instance.Cell))),
                "saved encounter attack routes");
            ulong actorId = checked((ulong)index + 1);
            try { grid.Add(actorId, instance.Cell, enemy.Footprint, enemy.Faction, enemy.Share, instance.PlacementId); }
            catch (InvalidDataException error) { throw new InvalidDataException("Saved encounter crowd placement: " + error.Message, error); }
        }
        GameDefinitions.Require(report.PlacedMembers == instances.Length && report.PlacedGroups == instances.Select(i => i.GroupId).Distinct().Count()
            && report.TotalDifficulty == instances.Sum(i => i.DifficultyCost), "saved encounter report");
        GameDefinitions.Require(Accepted ? instances.Length > 0 : instances.Length == 0, "saved encounter acceptance");
        GameDefinitions.Require(Identity == ComputeIdentity(DefinitionId, Seed, instances), "saved encounter identity");
    }

    internal static string ComputeIdentity(string definitionId, ulong seed, IReadOnlyList<ResolvedEncounterInstance> instances)
    {
        StringBuilder text = new();
        text.Append(definitionId).Append('|').Append(seed).Append('\n');
        foreach (ResolvedEncounterInstance instance in instances.OrderBy(value => value.SpawnId, StringComparer.Ordinal))
        {
            text.Append(instance.SpawnId).Append('|').Append(instance.EnemyId).Append('|').Append(instance.GroupId).Append('|')
                .Append(instance.RoomId).Append('|').Append(instance.Cell.X).Append('|').Append(instance.Cell.Y).Append('|')
                .Append(instance.PlacementId).Append('|').Append(instance.Role).Append('|').Append(instance.DifficultyCost).Append('\n');
            foreach (GridPoint cell in instance.ReachableAttackPositions.OrderBy(value => value.Y).ThenBy(value => value.X))
                text.Append(cell.X).Append(',').Append(cell.Y).Append(';');
            text.Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }
}

/// <summary>
/// Places encounter members against resolved room geometry. It performs bounded grid
/// reachability inspection but leaves runtime navigation to the Engine.
/// </summary>
internal sealed class EncounterPlacementResolver(EncounterPlacementDefinition definition)
{
    private sealed record WorkingPlacement(ulong ActorId, ResolvedEncounterInstance Instance);

    internal EncounterPlacementResult Resolve(ulong seed, DungeonFloor floor, CombatDefinition combat,
        CrowdDefinition crowd, IReadOnlySet<GridPoint>? excludedCells = null)
    {
        ArgumentNullException.ThrowIfNull(floor);
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(crowd);
        definition.ValidateAgainst(combat, crowd);

        HashSet<GridPoint> floorCells = floor.Cells.ToHashSet();
        Dictionary<GridEdge, float> connectorClearances = ConnectorClearances(floor);
        HashSet<GridPoint> protectedCells = (excludedCells ?? new HashSet<GridPoint>()).ToHashSet();
        protectedCells.UnionWith(floor.Rooms.SelectMany(room => room.Thresholds));
        protectedCells.Add(floor.Entrance);
        protectedCells.Add(floor.Exit);
        if (!TryReachable(floor, floorCells, floor.Entrance, definition.Tuning.MaxReachabilityCells, 0, connectorClearances,
                out HashSet<GridPoint> reachable))
        {
            return Rejected(seed, "reachability_budget_exhausted", "Floor reachability exceeded the authored placement inspection budget.",
                new EncounterPlacementReport(0, 0, 0, 0, 0, 0, 0));
        }
        Dictionary<string, HashSet<GridPoint>> reachableByFootprint = [];
        foreach (EnemyDefinition enemy in combat.Enemies)
        {
            if (reachableByFootprint.ContainsKey(enemy.Footprint)) continue;
            float edgeClearance = crowd.Footprint(enemy.Footprint).EdgeClearance;
            if (!TryReachable(floor, floorCells, floor.Entrance, definition.Tuning.MaxReachabilityCells, edgeClearance,
                    connectorClearances, out HashSet<GridPoint> footprintReachable))
            {
                return Rejected(seed, "no_reachable_footprint",
                    $"No bounded floor route satisfies the '{enemy.Footprint}' edge clearance.",
                    new EncounterPlacementReport(0, 0, 0, 0, 0, reachable.Count, 0));
            }
            reachableByFootprint.Add(enemy.Footprint, footprintReachable);
        }

        List<WorkingPlacement> placed = [];
        List<EncounterPlacementRejection> rejections = [];
        int candidateChecks = 0;
        int consideredGroups = 0;
        int placedGroups = 0;
        int consideredMembers = 0;
        int totalDifficulty = 0;
        Dictionary<string, int> roomUse = new(StringComparer.Ordinal);

        foreach (EncounterGroupDefinition group in definition.Groups.OrderBy(group => group.Id, StringComparer.Ordinal))
        {
            consideredGroups++;
            consideredMembers += group.Members.Length;
            if (checked(totalDifficulty + group.Members.Sum(member => member.DifficultyCost)) > definition.Tuning.MaximumTotalDifficulty)
            {
                rejections.Add(Reject(group, null, "difficulty_budget_exhausted", "Group would exceed the authored floor difficulty budget."));
                if (group.Required) return Rejected(seed, rejections, consideredGroups, placedGroups, consideredMembers, candidateChecks, reachable.Count, totalDifficulty);
                continue;
            }

            ResolvedRoom[] rooms = floor.Rooms.Where(group.Matches)
                .OrderBy(room => roomUse.GetValueOrDefault(room.RegionId) >= definition.Tuning.MaxGroupsPerRoom ? 1 : 0)
                .ThenBy(room => roomUse.GetValueOrDefault(room.RegionId))
                .ThenBy(room => RoomDistance(room, floor.Entrance, group, combat))
                .ThenBy(room => StableRank(seed, group.Id, room.RegionId))
                .ThenBy(room => room.RegionId, StringComparer.Ordinal)
                .Take(definition.Tuning.MaxRoomCandidates)
                .ToArray();
            if (rooms.Length == 0)
            {
                rejections.Add(Reject(group, null, "no_matching_room", "No generated room matches the authored encounter role."));
                if (group.Required) return Rejected(seed, rejections, consideredGroups, placedGroups, consideredMembers, candidateChecks, reachable.Count, totalDifficulty);
                continue;
            }

            bool groupPlaced = false;
            EncounterPlacementRejection? lastFailure = null;
            foreach (ResolvedRoom room in rooms)
            {
                if (roomUse.GetValueOrDefault(room.RegionId) >= definition.Tuning.MaxGroupsPerRoom) continue;
                List<WorkingPlacement> tentative = [];
                MovementGrid grid = BuildGrid(floor, floorCells, crowd, combat, placed);
                bool failed = false;
                EncounterPlacementRejection? memberFailure = null;
                foreach (EncounterMemberDefinition member in group.Members
                    .OrderByDescending(value => FootprintCost(combat, value.SpawnId, crowd))
                    .ThenBy(value => value.SpawnId, StringComparer.Ordinal))
                {
                    EnemySpawnDefinition spawn = combat.Encounter.Single(value => value.Id == member.SpawnId);
                    EnemyDefinition enemy = combat.Enemy(spawn.Enemy);
                    HashSet<GridPoint> memberReachable = reachableByFootprint[enemy.Footprint];
                    ulong actorId = checked((ulong)(placed.Count + tentative.Count) + 1);
                    (GridPoint Cell, string PlacementId, GridPoint[] AttackPositions)? selected = null;
                    IEnumerable<GridPoint> cells = CandidateCells(room, floor, floorCells, protectedCells, member, spawn, combat, memberReachable, seed)
                        .Take(definition.Tuning.MaxCandidateCellsPerMember);
                    foreach (GridPoint cell in cells)
                    {
                        candidateChecks++;
                        if (!grid.CanFit(cell, enemy.Footprint, enemy.Faction, enemy.Share)) continue;
                        GridPoint[] attackPositions = AttackPositions(cell, member.Role, combat, floor, floorCells, protectedCells,
                            placed.Concat(tentative).Select(value => value.Instance.Cell), memberReachable);
                        if (attackPositions.Length == 0) continue;
                        try
                        {
                            grid.Add(actorId, cell, enemy.Footprint, enemy.Faction, enemy.Share);
                            selected = (cell, grid.Placement(actorId).Id, attackPositions);
                            break;
                        }
                        catch (InvalidDataException) { }
                    }
                    if (selected is null)
                    {
                        failed = true;
                        memberFailure = Reject(group, member.SpawnId, "no_legal_tactical_cell",
                            "No bounded candidate cell had crowd capacity, safe arrival distance, and a reachable attack position.");
                        break;
                    }
                    ResolvedEncounterInstance instance = new(member.SpawnId, enemy.Id, group.Id, room.RegionId,
                        selected.Value.Cell, selected.Value.PlacementId, member.Role, member.DifficultyCost, selected.Value.AttackPositions);
                    tentative.Add(new(actorId, instance));
                }
                if (!failed)
                {
                    placed.AddRange(tentative);
                    roomUse[room.RegionId] = roomUse.GetValueOrDefault(room.RegionId) + 1;
                    totalDifficulty = checked(totalDifficulty + group.Members.Sum(member => member.DifficultyCost));
                    placedGroups++;
                    groupPlaced = true;
                    break;
                }
                lastFailure = memberFailure;
            }

            if (!groupPlaced)
            {
                rejections.Add(lastFailure ?? Reject(group, null, "room_candidate_budget_exhausted", "All bounded room candidates were unavailable."));
                if (group.Required) return Rejected(seed, rejections, consideredGroups, placedGroups, consideredMembers, candidateChecks, reachable.Count, totalDifficulty);
            }
        }

        if (placed.Count == 0)
            return Rejected(seed, rejections.Count == 0
                ? [new EncounterPlacementRejection("", null, "no_encounters_placed", "No authored encounter group produced an accepted instance.")]
                : rejections, consideredGroups, placedGroups, consideredMembers, candidateChecks, reachable.Count, totalDifficulty);

        ResolvedEncounterInstance[] instances = placed.Select(value => value.Instance).ToArray();
        string identity = EncounterPlacementResult.ComputeIdentity(definition.Id, seed, instances);
        EncounterPlacementResult result = new(true, definition.Id, seed, identity, instances, rejections.ToArray(),
            new EncounterPlacementReport(consideredGroups, placedGroups, consideredMembers, instances.Length, candidateChecks, reachable.Count, totalDifficulty));
        result.Validate(floor, combat, crowd);
        return result;
    }

    private IEnumerable<GridPoint> CandidateCells(ResolvedRoom room, DungeonFloor floor, IReadOnlySet<GridPoint> floorCells,
        IReadOnlySet<GridPoint> protectedCells,
        EncounterMemberDefinition member, EnemySpawnDefinition spawn, CombatDefinition combat, IReadOnlySet<GridPoint> reachable, ulong seed)
    {
        int minimumDistance = Math.Max(definition.Tuning.MinimumArrivalDistance,
            member.Role == EncounterTacticalRole.Ranged ? definition.Tuning.MinimumRangedSpawnDistance : definition.Tuning.MinimumMeleeSpawnDistance);
        int directRange = (int)MathF.Floor(Math.Min(definition.Tuning.MaximumRangedAttackDistance,
            combat.Action(CombatActionKind.Fire).Range));
        return room.Cells.Where(cell => floorCells.Contains(cell) && reachable.Contains(cell)
                && !protectedCells.Contains(cell) && cell.ManhattanDistance(floor.Entrance) >= minimumDistance
                && (member.Role != EncounterTacticalRole.Ranged || !DirectArrivalLane(cell, floor, floorCells, directRange)))
            .OrderBy(cell => Math.Abs(cell.ManhattanDistance(floor.Entrance) - spawn.Distance))
            .ThenBy(cell => member.Role == EncounterTacticalRole.Ranged ? Math.Abs(cell.ManhattanDistance(floor.Entrance) - definition.Tuning.PreferredRangedAttackDistance) : 0)
            .ThenBy(cell => StableRank(seed, member.SpawnId, room.RegionId, cell.X.ToString(System.Globalization.CultureInfo.InvariantCulture), cell.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ThenBy(cell => cell.Y).ThenBy(cell => cell.X);
    }

    private GridPoint[] AttackPositions(GridPoint enemyCell, EncounterTacticalRole role, CombatDefinition combat, DungeonFloor floor,
        IReadOnlySet<GridPoint> floorCells,
        IReadOnlySet<GridPoint> protectedCells, IEnumerable<GridPoint> occupied, IReadOnlySet<GridPoint> reachable)
    {
        HashSet<GridPoint> unavailable = protectedCells.ToHashSet();
        unavailable.UnionWith(occupied);
        if (role == EncounterTacticalRole.Melee)
        {
            int range = (int)MathF.Floor(combat.Action(CombatActionKind.Melee).Range);
            return reachable.Where(cell => !unavailable.Contains(cell) && cell != enemyCell
                && floor.Level(cell) == floor.Level(enemyCell)
                && cell.ManhattanDistance(enemyCell) > 0 && cell.ManhattanDistance(enemyCell) <= range)
                .OrderBy(cell => cell.ManhattanDistance(enemyCell)).ThenBy(cell => cell.Y).ThenBy(cell => cell.X)
                .Take(definition.Tuning.MaxAttackPositionsPerMember).ToArray();
        }

        int minimum = definition.Tuning.MinimumRangedAttackDistance;
        int maximum = Math.Min(definition.Tuning.MaximumRangedAttackDistance,
            (int)MathF.Floor(combat.Action(CombatActionKind.Fire).Range));
        return reachable.Where(cell => !unavailable.Contains(cell) && cell != enemyCell)
            .Select(cell => (Cell: cell, Distance: cell.ManhattanDistance(enemyCell)))
            .Where(value => floor.Level(value.Cell) == floor.Level(enemyCell)
                && value.Distance >= minimum && value.Distance <= maximum
                && GridLineOfSight(floor, floorCells, value.Cell, enemyCell))
            .OrderBy(value => Math.Abs(value.Distance - definition.Tuning.PreferredRangedAttackDistance))
            .ThenBy(value => value.Distance).ThenBy(value => value.Cell.Y).ThenBy(value => value.Cell.X)
            .Take(definition.Tuning.MaxAttackPositionsPerMember).Select(value => value.Cell).ToArray();
    }

    private static MovementGrid BuildGrid(DungeonFloor floor, IReadOnlySet<GridPoint> cells, CrowdDefinition crowd, CombatDefinition combat,
        IReadOnlyList<WorkingPlacement> placed)
    {
        MovementGrid grid = new(cells, (_, _) => true, crowd);
        foreach (FloorConnector connector in floor.Connectors)
            grid.SetClearance(connector.From, connector.To, connector.Clearance);
        foreach (WorkingPlacement working in placed)
        {
            EnemyDefinition enemy = combat.Enemy(working.Instance.EnemyId);
            grid.Add(working.ActorId, working.Instance.Cell, enemy.Footprint, enemy.Faction, enemy.Share, working.Instance.PlacementId);
        }
        return grid;
    }

    private static int FootprintCost(CombatDefinition combat, string spawnId, CrowdDefinition crowd)
    {
        EnemySpawnDefinition spawn = combat.Encounter.Single(value => value.Id == spawnId);
        return crowd.Footprint(combat.Enemy(spawn.Enemy).Footprint).Cost;
    }

    private static int RoomDistance(ResolvedRoom room, GridPoint entrance, EncounterGroupDefinition group, CombatDefinition combat)
    {
        int desired = (int)Math.Round(group.Members
            .Select(member => combat.Encounter.Single(value => value.Id == member.SpawnId).Distance)
            .DefaultIfEmpty(0).Average());
        int actual = room.Cells.Min(cell => cell.ManhattanDistance(entrance));
        return Math.Abs(actual - desired);
    }

    internal static bool TryReachable(DungeonFloor floor, IReadOnlySet<GridPoint> cells, GridPoint start, int budget,
        float minimumClearance, IReadOnlyDictionary<GridEdge, float> connectorClearances, out HashSet<GridPoint> reachable)
    {
        reachable = [];
        if (!cells.Contains(start)) return false;
        Queue<GridPoint> queue = new();
        queue.Enqueue(start); reachable.Add(start);
        while (queue.TryDequeue(out GridPoint cell))
        {
            foreach (CardinalDirection direction in CardinalDirections.Ordered)
            {
                GridPoint next = cell + direction.Offset();
                if (!cells.Contains(next) || !CanTraverse(floor, cell, next, minimumClearance, connectorClearances)
                    || !reachable.Add(next)) continue;
                if (reachable.Count > budget) return false;
                queue.Enqueue(next);
            }
        }
        return true;
    }

    private bool DirectArrivalLane(GridPoint spawn, DungeonFloor floor, IReadOnlySet<GridPoint> floorCells, int maximumDistance)
    {
        return definition.Tuning.RejectDirectArrivalLane
            && spawn.ManhattanDistance(floor.Entrance) <= maximumDistance
            && GridLineOfSight(floor, floorCells, floor.Entrance, spawn);
    }

    internal static bool GridLineOfSight(DungeonFloor floor, IReadOnlySet<GridPoint> floorCells, GridPoint from, GridPoint to)
    {
        if (floor.Level(from) != floor.Level(to)) return false;
        int dx = to.X - from.X, dy = to.Y - from.Y;
        int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (steps == 0) return false;
        for (int index = 1; index < steps; index++)
        {
            GridPoint cell = new(from.X + (int)Math.Round(dx * index / (double)steps), from.Y + (int)Math.Round(dy * index / (double)steps));
            if (!floorCells.Contains(cell) || floor.Level(cell) != floor.Level(from)) return false;
        }
        return true;
    }

    internal static Dictionary<GridEdge, float> ConnectorClearances(DungeonFloor floor)
    {
        Dictionary<GridEdge, float> clearances = [];
        foreach (FloorConnector connector in floor.Connectors)
        {
            GridEdge edge = GridEdge.Between(connector.From, connector.To);
            if (!clearances.TryGetValue(edge, out float existing) || connector.Clearance < existing)
                clearances[edge] = connector.Clearance;
        }
        return clearances;
    }

    private static bool CanTraverse(DungeonFloor floor, GridPoint from, GridPoint to, float minimumClearance,
        IReadOnlyDictionary<GridEdge, float> connectorClearances)
    {
        GridEdge edge = GridEdge.Between(from, to);
        int levelDelta = Math.Abs(floor.Level(from) - floor.Level(to));
        if (levelDelta > 0 && !connectorClearances.ContainsKey(edge)) return false;
        return !connectorClearances.TryGetValue(edge, out float clearance) || clearance >= minimumClearance;
    }

    private EncounterPlacementResult Rejected(ulong seed, string code, string detail, EncounterPlacementReport report) =>
        Rejected(seed, [new EncounterPlacementRejection("", null, code, detail)], report.ConsideredGroups, report.PlacedGroups,
            report.ConsideredMembers, report.CandidateChecks, report.ReachabilityCells, report.TotalDifficulty);

    private EncounterPlacementResult Rejected(ulong seed, IReadOnlyList<EncounterPlacementRejection> rejections,
        int consideredGroups, int placedGroups, int consideredMembers, int candidateChecks, int reachabilityCells, int totalDifficulty) =>
        new(false, definition.Id, seed, "", [], rejections.ToArray(),
            new EncounterPlacementReport(consideredGroups, placedGroups, consideredMembers, 0, candidateChecks, reachabilityCells, totalDifficulty));

    private static EncounterPlacementRejection Reject(EncounterGroupDefinition group, string? spawnId, string code, string detail) =>
        new(group.Id, spawnId, code, detail);

    private static ulong StableRank(ulong seed, params string[] fields)
    {
        string value = seed.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + string.Join('|', fields);
        return BinaryPrimitives.ReadUInt64LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

}
