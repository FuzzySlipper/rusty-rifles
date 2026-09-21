using System.Text.Json;
using Rifles.Game.Characters;
using Rifles.Game.Combat;
using Rifles.Game.Debugging;
using Rifles.Game.Dungeon;
using Rifles.Game.Expedition;
using Rifles.Game.Generation;
using Rifles.Game.Items;
using Rifles.Game.Party;
using Rifles.Procgen.Generation;
using Rusty.Engine.Debugging;
using Rusty.Engine.Mechanics;

namespace Rifles.Game;

public sealed partial class RiflesProduct
{
    [DebugCommand("rifles.drill.list", Description = "List the authored martial-command drills. Does not alter the current run.")]
    public string ListMartialDrills() => JsonSerializer.Serialize(definitions.MartialDrills.Drills.Select(drill => new
    {
        drill.Id,
        drill.Name,
        drill.Description,
        enemies = drill.Enemies.Select(enemy => enemy.SpawnId).ToArray(),
        drill.Expected,
    }), new JsonSerializerOptions { WriteIndented = true });

    [DebugCommand("rifles.drill.read", Description = "Read one authored martial-command drill. Does not alter the current run.")]
    public string ReadMartialDrill(string id)
    {
        MartialDrillDefinition drill = definitions.MartialDrills.Drill(id);
        return JsonSerializer.Serialize(drill, new JsonSerializerOptions { WriteIndented = true });
    }

    [DebugCommand("rifles.drill.start", Description = "Reset into one authored martial drill, paused for ordinary game controls.")]
    public string StartMartialDrill(string id)
    {
        MartialDrillDefinition drill;
        try { drill = definitions.MartialDrills.Drill(id); }
        catch (InvalidDataException error) { return error.Message; }

        try
        {
            StartNewRun(definitions.Generation.Seed);
            ExpeditionSnapshot fresh = Capture();
            ExpeditionSnapshot prepared = PrepareMartialDrill(fresh, drill);
            Activate(prepared);
            feedback = "Authored drill '" + drill.Name + "' ready. " + drill.Expected;
            Publish();
            return feedback;
        }
        catch (InvalidDataException error)
        {
            feedback = "Drill '" + drill.Name + "' unavailable: " + error.Message;
            Publish();
            return feedback;
        }
    }

    private ExpeditionSnapshot PrepareMartialDrill(ExpeditionSnapshot fresh, MartialDrillDefinition drill)
    {
        Dictionary<string, EnemySnapshot> original = fresh.Combat.Enemies.ToDictionary(enemy => enemy.Spawn, StringComparer.Ordinal);
        EnemySnapshot[] selected = drill.Enemies.Select(enemy => original.GetValueOrDefault(enemy.SpawnId)
            ?? throw new InvalidDataException("The generated floor did not resolve required spawn '" + enemy.SpawnId + "'."))
            .ToArray();
        MartialDrillLayout layout = FindMartialDrillLayout(fresh, drill, selected);
        Dictionary<string, ResolvedEncounterInstance> placed = fresh.EncounterPlacement.Instances
            .ToDictionary(instance => instance.SpawnId, StringComparer.Ordinal);
        HashSet<string> keptOwners = selected.Select(enemy => enemy.Owner).ToHashSet(StringComparer.Ordinal);
        HashSet<string> removedOwners = fresh.Combat.Enemies.Select(enemy => enemy.Owner).Where(owner => !keptOwners.Contains(owner))
            .ToHashSet(StringComparer.Ordinal);

        EnemySnapshot[] enemies = selected.Select((enemy, index) =>
        {
            MartialDrillEnemyDefinition authored = drill.Enemies[index];
            GridPoint cell = layout.EnemyCells[index];
            EnemyDefinition definition = definitions.Combat.Enemy(enemy.Definition);
            return enemy with
            {
                Motion = new ExplorationSnapshot(cell, drill.Facing.Opposite(), 0, null, cell, drill.Facing.Opposite(), 0, authored.Placement),
                Action = null,
                DecisionRemaining = 0,
                Aware = false,
                Loaded = false,
                Brain = enemy.Brain with { Home = cell, PatrolRoute = [cell], PatrolIndex = 0,
                    Mode = EnemyBrainMode.Patrol, LastKnownTarget = null, MemoryRemaining = 0, SearchRemaining = 0,
                    SearchingAtLastKnown = false, Reason = EnemyBrainReason.None,
                    RetreatBudgetRemaining = definition.Brain.MaxRetreatSteps, RetreatCooldownRemaining = 0, PathRetryRemaining = 0 },
            };
        }).ToArray();

        ResolvedEncounterInstance[] instances = enemies.Select((enemy, index) =>
        {
            ResolvedEncounterInstance prior = placed[enemy.Spawn];
            EnemyDefinition definition = definitions.Combat.Enemy(enemy.Definition);
            GridPoint cell = layout.EnemyCells[index];
            ResolvedRoom room = fresh.Floor.Rooms.Single(room => room.Cells.Contains(cell));
            GridPoint[] attackPositions = DrillAttackPositions(fresh.Floor, definition, cell);
            return new ResolvedEncounterInstance(enemy.Spawn, enemy.Definition, "martial-drill:" + drill.Id, room.RegionId,
                cell, drill.Enemies[index].Placement, prior.Role, prior.DifficultyCost, attackPositions);
        }).ToArray();
        const string placementId = "martial-drill";
        EncounterPlacementResult encounters = new(true, placementId, fresh.Floor.Seed,
            EncounterPlacementResult.ComputeIdentity(placementId, fresh.Floor.Seed, instances), instances,
            [new EncounterPlacementRejection("martial-drill", null, "authored_fixture", "Authored martial drill setup.")],
            new EncounterPlacementReport(1, 1, instances.Length, instances.Length, 0, fresh.Floor.Cells.Count(),
                instances.Sum(instance => instance.DifficultyCost)));

        MemberSnapshot[] members = fresh.Members.Select(member =>
        {
            MartialDrillMemberDefinition authored = drill.Members.Single(definition => definition.Member == member.Id);
            return member with { Position = authored.Position, Stats = authored.Fallen ? WithVitality(member.Stats, 0) : member.Stats };
        }).ToArray();
        InventorySnapshot inventory = fresh.Inventory with
        {
            Packs = fresh.Inventory.Packs.Where(pack => !removedOwners.Contains(pack.Owner.Key)).ToArray(),
        };
        HashSet<ulong> retainedItems = inventory.Packs.SelectMany(pack => pack.Items).Select(item => item.Id).ToHashSet();
        HashSet<ulong> fixedBayonets = RifleItemsFor(drill.BayonetMembers, inventory).ToHashSet();
        WeaponStateSnapshot weapons = new(fresh.Combat.Weapons.Muskets.Where(musket => retainedItems.Contains(musket.Item))
            .Select(musket => musket with { Loaded = true, BayonetFixed = fixedBayonets.Contains(musket.Item) }).ToArray());
        CombatSnapshot combat = fresh.Combat with
        {
            Enemies = enemies,
            Members = fresh.Combat.Members.Select(member => member with { Action = null }).ToArray(),
            Weapons = weapons,
            Flights = [],
            Drops = fresh.Combat.Drops.Where(drop => !removedOwners.Contains(drop.Owner)).ToArray(),
            SelectedTarget = 0,
            Charge = null,
        };
        ExplorationSnapshot pose = new(layout.Origin, drill.Facing, 0, null, layout.Origin, drill.Facing, 0);
        return fresh with { Exploration = pose, Members = members, Paused = true, SelectedMember = "warden", Inventory = inventory,
            Combat = combat, EncounterPlacement = encounters, RestRemaining = 0, RestOwner = "", Formation = null };
    }

    private MartialDrillLayout FindMartialDrillLayout(ExpeditionSnapshot snapshot, MartialDrillDefinition drill,
        IReadOnlyList<EnemySnapshot> enemies)
    {
        HashSet<GridPoint> unavailable =
        [
            snapshot.Floor.Entrance, snapshot.Floor.Exit, snapshot.Actor.Motion.Position,
            snapshot.Features.Dressing.Bench, snapshot.Features.Dressing.Crate, snapshot.Features.Dressing.Observer,
            snapshot.ItemWorld.Door, snapshot.ItemWorld.Lever,
            .. snapshot.ItemWorld.Anchors.Select(anchor => anchor.Cell),
            .. snapshot.GeneratedFeatures.Gates.Select(gate => gate.Cell),
            .. snapshot.GeneratedFeatures.Hazards.Select(hazard => hazard.Cell),
            .. snapshot.GeneratedFeatures.Plates.Select(plate => plate.Cell),
            .. snapshot.GeneratedFeatures.Keys.Select(key => key.Cell),
            .. snapshot.Combat.Drops.Select(drop => drop.Cell),
        ];
        GridPoint forward = drill.Facing.Offset();
        GridPoint left = drill.Facing.Rotate(-1).Offset();
        foreach (ResolvedRoom room in snapshot.Floor.Rooms.OrderBy(room => room.RegionId, StringComparer.Ordinal))
        foreach (GridPoint origin in room.Cells.OrderBy(cell => cell.Y).ThenBy(cell => cell.X))
        {
            if (unavailable.Contains(origin) || room.Thresholds.Contains(origin)) continue;
            GridPoint[] cells = drill.Enemies.Select(enemy => origin + Scale(forward, enemy.Forward) + Scale(left, enemy.Left)).ToArray();
            if (cells.Distinct().Count() != cells.Length || cells.Any(cell => unavailable.Contains(cell) || !room.Cells.Contains(cell)
                || room.Thresholds.Contains(cell) || snapshot.Floor.Level(cell) != snapshot.Floor.Level(origin))) continue;
            if (!drill.ForwardBlocked && drill.Id.StartsWith("charge-", StringComparison.Ordinal)
                && Enumerable.Range(1, Math.Min(definitions.Charge.MaximumCells, drill.Enemies.Max(enemy => enemy.Forward) - 1))
                    .Select(distance => origin + Scale(forward, distance)).Any(cell => unavailable.Contains(cell) || !room.Cells.Contains(cell)
                        || room.Thresholds.Contains(cell) || cells.Contains(cell))) continue;
            try
            {
                for (int index = 0; index < enemies.Count; index++)
                    _ = DrillAttackPositions(snapshot.Floor, definitions.Combat.Enemy(enemies[index].Definition), cells[index]);
                return new MartialDrillLayout(origin, cells);
            }
            catch (InvalidDataException) { }
        }
        throw new InvalidDataException("No generated room fits the authored " + drill.Id + " offsets and crowd slots.");
    }

    private GridPoint[] DrillAttackPositions(DungeonFloor floor, EnemyDefinition enemy, GridPoint cell)
    {
        HashSet<GridPoint> floorCells = floor.Cells.ToHashSet();
        if (!EncounterPlacementResolver.TryReachable(floor, floorCells, floor.Entrance, floorCells.Count,
                definitions.Crowd.Footprint(enemy.Footprint).EdgeClearance, EncounterPlacementResolver.ConnectorClearances(floor), out HashSet<GridPoint> reachable))
            throw new InvalidDataException("The generated floor has no reachable " + enemy.Footprint + " drill cell.");
        if (!reachable.Contains(cell)) throw new InvalidDataException("Authored drill target is outside the enemy footprint route.");
        int maximum = (int)MathF.Floor(definitions.Combat.Action(enemy.Attack).Range);
        GridPoint[] positions = reachable.Where(candidate => candidate != cell && floor.Level(candidate) == floor.Level(cell))
            .Where(candidate => enemy.Attack == CombatActionKind.Melee
                ? candidate.ManhattanDistance(cell) <= maximum
                : candidate.ManhattanDistance(cell) >= 1 && candidate.ManhattanDistance(cell) <= maximum
                    && EncounterPlacementResolver.GridLineOfSight(floor, floorCells, candidate, cell))
            .OrderBy(candidate => candidate.ManhattanDistance(cell)).ThenBy(candidate => candidate.Y).ThenBy(candidate => candidate.X).Take(12).ToArray();
        if (positions.Length == 0) throw new InvalidDataException("Authored drill target has no legal attack position.");
        return positions;
    }

    private static StatsComponentSnapshot WithVitality(StatsComponentSnapshot stats, double vitality) => stats with
    {
        Tracks = [.. stats.Tracks.Select(track => track.Id == RiflesStatIds.Vitality.Value ? track with { Current = vitality } : track)],
    };

    private static GridPoint Scale(GridPoint point, int scalar) => checked(new GridPoint(point.X * scalar, point.Y * scalar));

    private static IEnumerable<ulong> RifleItemsFor(IEnumerable<string> members, InventorySnapshot inventory)
    {
        foreach (string member in members)
        {
            SavedPack pack = inventory.Packs.SingleOrDefault(pack => pack.Owner.Key == "member:" + member)
                ?? throw new InvalidDataException("Martial drill member pack is unavailable: " + member + ".");
            SavedItem rifle = pack.Items.SingleOrDefault(item => item.Definition == "rifle")
                ?? throw new InvalidDataException("Martial drill member has no authored rifle: " + member + ".");
            yield return rifle.Id;
        }
    }

    private sealed record MartialDrillLayout(GridPoint Origin, GridPoint[] EnemyCells);
}
