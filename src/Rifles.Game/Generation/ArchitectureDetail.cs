using System.Security.Cryptography;
using System.Text;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Procgen.Generation;

namespace Rifles.Game.Generation;

/// <summary>Kind of authored architecture detail attached to a resolved room.</summary>
internal enum ArchitectureDetailKind
{
    Trim,
    Recess,
    Support,
    Damage,
    MaterialRegion,
}

/// <summary>Material family resolved by the current dungeon appearance.</summary>
internal enum ArchitectureDetailMaterial
{
    Brick,
    Limewash,
    Floor,
}

/// <summary>Surface that receives a detail fact. Boundary details are wall facts.</summary>
internal enum ArchitectureDetailSurface
{
    Wall,
    Ceiling,
    Floor,
}

/// <summary>
/// One file-authored detail rule. Cell and side identify a logical room anchor;
/// layer, height, and depth are resolved voxel units within that anchor's
/// vertical wall or floor volume.
/// </summary>
internal sealed record ArchitectureDetailRule(
    string Id,
    string[] Functions,
    ArchitectureDetailKind Kind,
    ArchitectureDetailSurface Surface,
    ArchitectureDetailMaterial Material,
    int Count,
    int Layer,
    int Height,
    int Depth,
    int Spacing,
    bool DeliberateOccluder)
{
    internal bool AppliesTo(string function) => Functions.Contains("*", StringComparer.Ordinal)
        || Functions.Contains(function, StringComparer.Ordinal);
}

/// <summary>Versioned, bounded authored architecture-detail policy.</summary>
internal sealed record ArchitectureDetailDefinition(
    string Id,
    int MaxFacts,
    int MaxFactsPerRoom,
    ArchitectureDetailRule[] Rules)
{
    private const int MaxRules = 64;
    internal const int MaxFactsLimit = 16_384;
    internal const int MaxFactsPerRoomLimit = 1_024;
    private const int MaxLayer = 256;
    private const int MaxHeight = 64;
    private const int MaxDepth = 3;
    private const int MaxSpacing = 64;

    internal string Identity => ComputeIdentity(this);

    internal void Validate()
    {
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(Id) && Id.Length <= 96
            && MaxFacts is > 0 and <= MaxFactsLimit
            && MaxFactsPerRoom is > 0 and <= MaxFactsPerRoomLimit
            && MaxFactsPerRoom <= MaxFacts
            && Rules is { Length: > 0 and <= MaxRules }
            && Rules.All(rule => rule is not null)
            && Rules.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count() == Rules.Length,
            "architecture detail identity/budget");

        foreach (ArchitectureDetailRule rule in Rules)
        {
            GameDefinitions.Require(!string.IsNullOrWhiteSpace(rule.Id)
                && rule.Functions is { Length: > 0 }
                && rule.Functions.All(function => !string.IsNullOrWhiteSpace(function))
                && rule.Functions.Distinct(StringComparer.Ordinal).Count() == rule.Functions.Length
                && Enum.IsDefined(rule.Kind) && Enum.IsDefined(rule.Surface) && Enum.IsDefined(rule.Material)
                && rule.Count is > 0 and <= MaxFactsPerRoomLimit
                && rule.Layer is >= 0 and <= MaxLayer
                && rule.Height is > 0 and <= MaxHeight
                && rule.Depth is >= 0 and <= MaxDepth
                && rule.Spacing is >= 0 and <= MaxSpacing,
                "architecture detail rule");
            GameDefinitions.Require(rule.Kind == ArchitectureDetailKind.MaterialRegion
                ? rule.Depth == 0
                : rule.Surface == ArchitectureDetailSurface.Wall && rule.Depth > 0,
                "architecture detail rule surface/depth");
            if (rule.Kind == ArchitectureDetailKind.MaterialRegion)
                GameDefinitions.Require(!rule.DeliberateOccluder, "architecture material region occluder");
        }
    }

    private static string ComputeIdentity(ArchitectureDetailDefinition definition)
    {
        var text = new StringBuilder();
        void Add(params object?[] fields)
        {
            foreach (object? field in fields) text.Append(field?.ToString() ?? string.Empty).Append('|');
            text.Append('\n');
        }

        Add("architecture-detail", definition.Id, definition.MaxFacts, definition.MaxFactsPerRoom);
        foreach (ArchitectureDetailRule rule in definition.Rules.OrderBy(rule => rule.Id, StringComparer.Ordinal))
        {
            Add("rule", rule.Id, rule.Kind, rule.Surface, rule.Material, rule.Count, rule.Layer,
                rule.Height, rule.Depth, rule.Spacing, rule.DeliberateOccluder);
            foreach (string function in rule.Functions.OrderBy(function => function, StringComparer.Ordinal))
                Add("function", function);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }
}

/// <summary>
/// A resolved detail placement. Boundary facts anchor on an interior room cell
/// and point toward the solid side; material regions have no side. Layer and
/// height are vertical voxel offsets/extents relative to the anchor cell's
/// floor level, while depth is a horizontal near-face voxel band. Boundary
/// depth must leave at least one backing voxel when the scene admits the fact.
/// </summary>
internal sealed record ArchitectureDetailFact(
    string Id,
    string RegionId,
    string RuleId,
    ArchitectureDetailKind Kind,
    ArchitectureDetailSurface Surface,
    ArchitectureDetailMaterial Material,
    GridPoint Cell,
    CardinalDirection? Side,
    int Layer,
    int Height,
    int Depth,
    bool DeliberateOccluder);

/// <summary>Persistable, deterministic architecture-detail result for one floor.</summary>
internal sealed record ArchitectureDetailSnapshot(
    string DefinitionId,
    string DefinitionIdentity,
    string FloorLayoutIdentity,
    int FactBudget,
    int RoomFactBudget,
    ArchitectureDetailFact[] Facts,
    string Identity);

internal static class ArchitectureDetail
{
    private const int MaxGeneratedFactIdLength = 512;
    private const int MaxFactsLimit = ArchitectureDetailDefinition.MaxFactsLimit;
    private const int MaxFactsPerRoomLimit = ArchitectureDetailDefinition.MaxFactsPerRoomLimit;
    private const int MaxLayer = 256;
    private const int MaxHeight = 64;
    private const int MaxDepth = 3;

    internal static ArchitectureDetailSnapshot Resolve(DungeonFloor floor, ArchitectureDetailDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(floor);
        ArgumentNullException.ThrowIfNull(definition);
        floor.Validate();
        definition.Validate();

        HashSet<GridPoint> floorCells = floor.Cells.ToHashSet();
        HashSet<GridPoint> reserved = ReservedCells(floor);
        var facts = new List<ArchitectureDetailFact>();
        foreach (ResolvedRoom room in floor.Rooms.OrderBy(room => room.RegionId, StringComparer.Ordinal))
        {
            var roomFacts = new List<ArchitectureDetailFact>();
            List<DetailPlacement> occupied = [];
            foreach (ArchitectureDetailRule rule in definition.Rules.OrderBy(rule => rule.Id, StringComparer.Ordinal))
            {
                if (!rule.AppliesTo(room.Function)) continue;
                IEnumerable<DetailAnchor> candidates = rule.Kind == ArchitectureDetailKind.MaterialRegion
                    ? InteriorAnchors(room, floorCells, reserved)
                    : BoundaryAnchors(room, floorCells, reserved, IsCarving(rule.Kind));
                DetailAnchor[] selected = Select(candidates, rule, floor.Seed, floor.LayoutIdentity, room.RegionId, occupied);
                foreach (DetailAnchor anchor in selected)
                {
                    ArchitectureDetailFact fact = new(
                        FactId(room.RegionId, rule.Id, anchor), room.RegionId, rule.Id, rule.Kind,
                        rule.Surface, rule.Material, anchor.Cell, anchor.Side, rule.Layer, rule.Height,
                        rule.Depth, rule.DeliberateOccluder);
                    GameDefinitions.Require(fact.Id.Length <= MaxGeneratedFactIdLength, "architecture detail fact identity");
                    roomFacts.Add(fact);
                    occupied.Add(Placement(fact));
                }
            }

            if (roomFacts.Count > definition.MaxFactsPerRoom)
                throw new InvalidDataException($"Architecture detail room budget exceeded for '{room.RegionId}'.");
            facts.AddRange(roomFacts);
        }

        if (facts.Count > definition.MaxFacts)
            throw new InvalidDataException("Architecture detail fact budget exhausted.");

        ArchitectureDetailFact[] resolvedFacts = facts.OrderBy(fact => fact.Id, StringComparer.Ordinal).ToArray();
        var snapshot = new ArchitectureDetailSnapshot(definition.Id, definition.Identity, floor.LayoutIdentity,
            definition.MaxFacts, definition.MaxFactsPerRoom, resolvedFacts, string.Empty);
        return snapshot with { Identity = Identity(snapshot) };
    }

    /// <summary>
    /// Validates a retained artifact against its floor without consulting the
    /// current authored rule set. This is the restore path: the retained facts
    /// and their original definition identity remain authoritative.
    /// </summary>
    internal static void Validate(ArchitectureDetailSnapshot snapshot, DungeonFloor floor)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(floor);
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(snapshot.DefinitionId) && snapshot.DefinitionId.Length <= 96
            && snapshot.DefinitionIdentity is { Length: 64 } && snapshot.DefinitionIdentity.All(Uri.IsHexDigit)
            && snapshot.FloorLayoutIdentity == floor.LayoutIdentity
            && snapshot.FactBudget is > 0 and <= MaxFactsLimit
            && snapshot.RoomFactBudget is > 0 and <= MaxFactsPerRoomLimit
            && snapshot.RoomFactBudget <= snapshot.FactBudget
            && snapshot.Facts is not null && snapshot.Facts.Length <= snapshot.FactBudget
            && snapshot.Identity == Identity(snapshot), "architecture detail snapshot identity");

        ArchitectureDetailFact[] facts = snapshot.Facts!;
        ValidateFacts(snapshot, floor, facts, null);
    }

    /// <summary>
    /// Explicit compatibility check for a current authored definition. A
    /// changed rule file can reject compatibility, while the two-argument
    /// restore validator still accepts a structurally valid retained artifact.
    /// </summary>
    internal static void Validate(ArchitectureDetailSnapshot snapshot, DungeonFloor floor, ArchitectureDetailDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.Validate();
        Validate(snapshot, floor);
        GameDefinitions.Require(snapshot.DefinitionId == definition.Id
            && snapshot.DefinitionIdentity == definition.Identity
            && snapshot.FactBudget == definition.MaxFacts
            && snapshot.RoomFactBudget == definition.MaxFactsPerRoom, "architecture detail definition compatibility");
        ValidateFacts(snapshot, floor, snapshot.Facts!, definition);
    }

    /// <summary>
    /// Checks the voxel resolution that will consume retained facts. Boundary
    /// depth must leave solid backing, and every vertical band must fit the
    /// admitted wall volume. This remains pure product validation; it does not
    /// access Engine state.
    /// </summary>
    internal static void ValidateVoxelResolution(ArchitectureDetailSnapshot snapshot, DungeonFloor floor,
        int voxelsPerCell, int verticalVoxels)
    {
        Validate(snapshot, floor);
        GameDefinitions.Require(voxelsPerCell is >= 1 and <= 4 && verticalVoxels > 0,
            "architecture detail voxel resolution");
        foreach (ArchitectureDetailFact fact in snapshot.Facts!)
        {
            GameDefinitions.Require(fact.Layer + fact.Height <= verticalVoxels,
                "architecture detail vertical band");
            if (fact.Kind != ArchitectureDetailKind.MaterialRegion)
                GameDefinitions.Require(fact.Depth < voxelsPerCell, "architecture detail backing");
        }
    }

    private static void ValidateFacts(ArchitectureDetailSnapshot snapshot, DungeonFloor floor,
        ArchitectureDetailFact[] facts, ArchitectureDetailDefinition? definition)
    {
        HashSet<GridPoint> floorCells = floor.Cells.ToHashSet();
        HashSet<GridPoint> reserved = ReservedCells(floor);
        Dictionary<string, ResolvedRoom> rooms = floor.Rooms.ToDictionary(room => room.RegionId, StringComparer.Ordinal);
        Dictionary<string, ArchitectureDetailRule>? rules = definition?.Rules.ToDictionary(rule => rule.Id, StringComparer.Ordinal);
        HashSet<string> ids = new(StringComparer.Ordinal);
        List<DetailPlacement> occupied = [];
        foreach (ArchitectureDetailFact? fact in facts.OrderBy(fact => fact?.Id, StringComparer.Ordinal))
        {
            GameDefinitions.Require(fact is not null, "architecture detail null fact");
            if (fact is null) continue;
            GameDefinitions.Require(!string.IsNullOrWhiteSpace(fact.Id) && fact.Id.Length <= MaxGeneratedFactIdLength
                && !string.IsNullOrWhiteSpace(fact.RegionId) && !string.IsNullOrWhiteSpace(fact.RuleId)
                && Enum.IsDefined(fact.Kind) && Enum.IsDefined(fact.Surface) && Enum.IsDefined(fact.Material)
                && (fact.Side is null || Enum.IsDefined(fact.Side.Value))
                && fact.Layer is >= 0 and <= MaxLayer && fact.Height is > 0 and <= MaxHeight
                && fact.Depth is >= 0 and <= MaxDepth
                && ids.Add(fact.Id)
                && rooms.TryGetValue(fact.RegionId, out ResolvedRoom? room)
                && room is not null && room.Cells.Contains(fact.Cell) && floorCells.Contains(fact.Cell), "architecture detail fact binding");
            if (!rooms.TryGetValue(fact.RegionId, out ResolvedRoom? resolvedRoom) || resolvedRoom is null)
                throw new InvalidDataException("Architecture detail fact room is missing.");

            if (definition is not null)
            {
                if (!rules!.TryGetValue(fact.RuleId, out ArchitectureDetailRule? resolvedRule) || resolvedRule is null)
                    throw new InvalidDataException("Architecture detail fact rule is missing.");
                GameDefinitions.Require(resolvedRule.AppliesTo(resolvedRoom.Function)
                    && fact.Kind == resolvedRule.Kind && fact.Surface == resolvedRule.Surface && fact.Material == resolvedRule.Material
                    && fact.Layer == resolvedRule.Layer && fact.Height == resolvedRule.Height && fact.Depth == resolvedRule.Depth
                    && fact.DeliberateOccluder == resolvedRule.DeliberateOccluder, "architecture detail authored binding");
            }

            if (fact.Kind == ArchitectureDetailKind.MaterialRegion)
            {
                GameDefinitions.Require(fact.Side is null && fact.Depth == 0 && !fact.DeliberateOccluder
                    && !reserved.Contains(fact.Cell), "architecture material region placement");
                GameDefinitions.Require(occupied.All(existing => !Conflicts(existing, Placement(fact))),
                    "architecture detail overlap");
                occupied.Add(Placement(fact));
                continue;
            }

            GameDefinitions.Require(fact.Side is { } side && fact.Surface == ArchitectureDetailSurface.Wall
                && fact.Depth > 0 && !reserved.Contains(fact.Cell)
                && LeavesFloor(fact.Cell + side.Offset(), floorCells)
                && (!IsCarving(fact.Kind) || HasExclusiveWallNeighbor(fact.Cell, side, floorCells)),
                "architecture boundary placement");
            GameDefinitions.Require(occupied.All(existing => !Conflicts(existing, Placement(fact))),
                "architecture detail overlap");
            occupied.Add(Placement(fact));
        }
        GameDefinitions.Require(facts.SequenceEqual(facts.OrderBy(fact => fact?.Id, StringComparer.Ordinal)),
            "architecture detail fact ordering");
        foreach (IGrouping<string, ArchitectureDetailFact> roomFacts in facts.Where(fact => fact is not null)
            .GroupBy(fact => fact.RegionId, StringComparer.Ordinal))
            GameDefinitions.Require(roomFacts.Count() <= snapshot.RoomFactBudget, "architecture room detail budget");
    }

    internal static string Identity(ArchitectureDetailSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var text = new StringBuilder();
        void Add(params object?[] fields)
        {
            foreach (object? field in fields) text.Append(field?.ToString() ?? string.Empty).Append('|');
            text.Append('\n');
        }

        Add("architecture-detail-snapshot", snapshot.DefinitionId, snapshot.DefinitionIdentity,
            snapshot.FloorLayoutIdentity, snapshot.FactBudget, snapshot.RoomFactBudget);
        foreach (ArchitectureDetailFact? fact in (snapshot.Facts ?? Array.Empty<ArchitectureDetailFact>())
            .OrderBy(fact => fact?.Id, StringComparer.Ordinal))
        {
            if (fact is null)
            {
                Add("null-fact");
                continue;
            }
            Add(fact.Id, fact.RegionId, fact.RuleId, fact.Kind, fact.Surface, fact.Material, fact.Cell.X,
                fact.Cell.Y, fact.Side, fact.Layer, fact.Height, fact.Depth, fact.DeliberateOccluder);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }

    private static DetailAnchor[] Select(
        IEnumerable<DetailAnchor> source,
        ArchitectureDetailRule rule,
        ulong seed,
        string layoutIdentity,
        string roomId,
        IReadOnlyCollection<DetailPlacement> occupied)
    {
        DetailAnchor[] ordered = source
            .Distinct()
            .OrderBy(anchor => Rank(seed, layoutIdentity, roomId, rule.Id, anchor), StringComparer.Ordinal)
            .ThenBy(anchor => anchor.Cell.Y)
            .ThenBy(anchor => anchor.Cell.X)
            .ThenBy(anchor => anchor.Side)
            .ToArray();
        var selected = new List<DetailAnchor>(Math.Min(rule.Count, ordered.Length));
        foreach (DetailAnchor anchor in ordered)
        {
            if (selected.Count >= rule.Count) break;
            DetailPlacement candidate = new(anchor.Cell, anchor.Side, rule.Kind, rule.Surface,
                rule.Layer, rule.Height, rule.Depth);
            if (occupied.Any(previous => Conflicts(previous, candidate))) continue;
            if (selected.Any(previous => previous.Cell.ManhattanDistance(anchor.Cell) < rule.Spacing)) continue;
            selected.Add(anchor);
        }
        return selected.ToArray();
    }

    private static IEnumerable<DetailAnchor> InteriorAnchors(ResolvedRoom room, IReadOnlySet<GridPoint> floorCells, IReadOnlySet<GridPoint> reserved)
    {
        foreach (GridPoint cell in room.Cells.OrderBy(cell => cell.Y).ThenBy(cell => cell.X))
            if (floorCells.Contains(cell) && !reserved.Contains(cell)) yield return new DetailAnchor(cell, null);
    }

    private static IEnumerable<DetailAnchor> BoundaryAnchors(ResolvedRoom room, IReadOnlySet<GridPoint> floorCells,
        IReadOnlySet<GridPoint> reserved, bool requireExclusiveWall)
    {
        foreach (GridPoint cell in room.Cells.OrderBy(cell => cell.Y).ThenBy(cell => cell.X))
        {
            if (reserved.Contains(cell)) continue;
            foreach (CardinalDirection side in CardinalDirections.Ordered)
            {
                GridPoint neighbor = cell + side.Offset();
                if (neighbor.X < 0 || neighbor.Y < 0 || !LeavesFloor(neighbor, floorCells)) continue;
                if (requireExclusiveWall && CardinalDirections.Ordered.Count(direction =>
                        floorCells.Contains(neighbor + direction.Offset())) != 1) continue;
                yield return new DetailAnchor(cell, side);
            }
        }
    }

    private static HashSet<GridPoint> ReservedCells(DungeonFloor floor)
    {
        HashSet<GridPoint> reserved = [floor.Entrance, floor.Exit];
        foreach (ResolvedRoom room in floor.Rooms)
            foreach (GridPoint threshold in room.Thresholds) reserved.Add(threshold);
        foreach (CorridorRoute route in floor.Routes ?? Array.Empty<CorridorRoute>())
            foreach (GridPoint cell in route.Cells) reserved.Add(cell);
        foreach (FloorGrant grant in floor.Grants ?? Array.Empty<FloorGrant>()) reserved.Add(grant.Cell);
        foreach (FloorConnector connector in floor.Connectors ?? Array.Empty<FloorConnector>())
        {
            reserved.Add(connector.From);
            reserved.Add(connector.To);
        }
        return reserved;
    }

    private static bool LeavesFloor(GridPoint cell, IReadOnlySet<GridPoint> floorCells) =>
        cell.X >= 0 && cell.Y >= 0 && cell.X < GenerationPolicyValidation.MaxDimension
        && cell.Y < GenerationPolicyValidation.MaxDimension && !floorCells.Contains(cell);

    private static bool HasExclusiveWallNeighbor(GridPoint cell, CardinalDirection side,
        IReadOnlySet<GridPoint> floorCells)
    {
        GridPoint wall = cell + side.Offset();
        return CardinalDirections.Ordered.Count(direction => floorCells.Contains(wall + direction.Offset())) == 1;
    }

    private static string FactId(string roomId, string ruleId, DetailAnchor anchor) =>
        $"detail/{roomId}/{ruleId}/{anchor.Cell.X},{anchor.Cell.Y}/{anchor.Side?.ToString() ?? "floor"}";

    private static DetailPlacement Placement(ArchitectureDetailFact fact) =>
        new(fact.Cell, fact.Side, fact.Kind, fact.Surface, fact.Layer, fact.Height, fact.Depth);

    private static bool Overlaps(DetailPlacement left, DetailPlacement right) =>
        left.Cell == right.Cell && left.Side == right.Side && left.Surface == right.Surface
        && left.Layer < right.Layer + right.Height && right.Layer < left.Layer + left.Height;

    private static bool Conflicts(DetailPlacement left, DetailPlacement right) =>
        Overlaps(left, right)
        || (IsCarving(left.Kind) && IsCarving(right.Kind)
            && left.Side is { } leftSide && right.Side is { } rightSide
            && left.Cell + leftSide.Offset() == right.Cell + rightSide.Offset());

    private static bool IsCarving(ArchitectureDetailKind kind) =>
        kind is ArchitectureDetailKind.Recess or ArchitectureDetailKind.Damage;

    private static string Rank(ulong seed, string layoutIdentity, string roomId, string ruleId, DetailAnchor anchor) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("/", seed,
            layoutIdentity, roomId, ruleId, anchor.Cell.X, anchor.Cell.Y, anchor.Side?.ToString() ?? "floor"))));

    private readonly record struct DetailAnchor(GridPoint Cell, CardinalDirection? Side);
    private readonly record struct DetailPlacement(GridPoint Cell, CardinalDirection? Side,
        ArchitectureDetailKind Kind, ArchitectureDetailSurface Surface, int Layer, int Height, int Depth);
}
