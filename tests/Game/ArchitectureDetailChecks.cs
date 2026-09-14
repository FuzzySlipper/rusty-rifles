using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Generation;
using Rifles.Procgen.Generation;

internal static class ArchitectureDetailChecks
{
    internal static void Run(GameDefinitions definitions)
    {
        ArchitectureDetailDefinition definition = definitions.Architecture;
        definition.Validate();
        DungeonFloor floor = DungeonFloor.Generate(29, definitions.Generation, definitions.Rooms);

        ArchitectureDetailSnapshot first = ArchitectureDetail.Resolve(floor, definition);
        ArchitectureDetailSnapshot repeated = ArchitectureDetail.Resolve(floor, definition);
        Require(first.Identity == repeated.Identity && first.Facts.SequenceEqual(repeated.Facts),
            "Architecture detail selection is deterministic for one resolved floor.");
        ArchitectureDetail.Validate(first, floor);
        ArchitectureDetail.Validate(first, floor, definition);
        ArchitectureDetail.ValidateVoxelResolution(first, floor, definitions.Appearance.VoxelsPerCell,
            checked((definitions.Exploration.CeilingCells + 1) * definitions.Appearance.VoxelsPerCell));
        Require(first.Facts.Length <= definition.MaxFacts
            && first.Facts.GroupBy(fact => fact.RegionId).All(group => group.Count() <= definition.MaxFactsPerRoom),
            "Architecture detail facts stay within global and room budgets.");

        ArchitectureDetailKind[] kinds = first.Facts.Select(fact => fact.Kind).Distinct().OrderBy(kind => kind).ToArray();
        Require(kinds.SequenceEqual(Enum.GetValues<ArchitectureDetailKind>().OrderBy(kind => kind)),
            "Authored architecture rules resolve trim, recess, support, damage, and material regions.");
        Require(first.Facts.Select(fact => fact.Material).Distinct().OrderBy(material => material).SequenceEqual(
            [ArchitectureDetailMaterial.Brick, ArchitectureDetailMaterial.Limewash, ArchitectureDetailMaterial.Floor]),
            "Architecture detail resolves the brick, limewash, and floor material families.");

        HashSet<GridPoint> floorCells = floor.Cells.ToHashSet();
        HashSet<GridPoint> reserved = [floor.Entrance, floor.Exit, .. floor.Rooms.SelectMany(room => room.Thresholds),
            .. floor.Routes.SelectMany(route => route.Cells), .. floor.Grants.Select(grant => grant.Cell),
            .. floor.Connectors.SelectMany(connector => new[] { connector.From, connector.To })];
        foreach (ArchitectureDetailFact fact in first.Facts)
        {
            Require(floor.Rooms.Single(room => room.RegionId == fact.RegionId).Cells.Contains(fact.Cell)
                && !reserved.Contains(fact.Cell), "Architecture detail anchors remain in usable room cells.");
            if (fact.Kind == ArchitectureDetailKind.MaterialRegion)
            {
                Require(fact.Side is null && fact.Surface == ArchitectureDetailSurface.Floor && fact.Depth == 0,
                    "Material regions are explicit floor facts without wall sides.");
                continue;
            }

            CardinalDirection side = fact.Side ?? throw new InvalidOperationException("Boundary detail side missing.");
            GridPoint neighbor = fact.Cell + side.Offset();
            Require(fact.Surface == ArchitectureDetailSurface.Wall && !floorCells.Contains(neighbor),
                "Boundary detail points into solid space without closing a floor cell.");
        }
        ArchitectureDetailFact[] carvings = first.Facts.Where(fact => fact.Kind is ArchitectureDetailKind.Recess or ArchitectureDetailKind.Damage).ToArray();
        Require(carvings.Length > 0 && carvings.Select(fact => fact.Cell + fact.Side!.Value.Offset()).Distinct().Count() == carvings.Length,
            "Carving details never target the same physical wall cell twice.");
        foreach (ArchitectureDetailFact fact in carvings)
        {
            GridPoint wall = fact.Cell + fact.Side!.Value.Offset();
            Require(CardinalDirections.Ordered.Count(direction => floorCells.Contains(wall + direction.Offset())) == 1,
                "Carving details avoid piers with a walkable opposite face.");
        }

        ArchitectureDetailFact changed = first.Facts[0] with { Material = ArchitectureDetailMaterial.Brick == first.Facts[0].Material
            ? ArchitectureDetailMaterial.Limewash : ArchitectureDetailMaterial.Brick };
        ArchitectureDetailSnapshot forged = first with { Facts = [changed, .. first.Facts.Skip(1)] };
        RequireRejected(() => ArchitectureDetail.Validate(forged, floor, definition),
            "Modified architecture detail facts fail retained identity validation.");

        ArchitectureDetailDefinition retuned = definition with
        {
            Rules = definition.Rules.Select(rule => rule.Id == "wall-trim" ? rule with { Count = 1 } : rule).ToArray(),
        };
        retuned.Validate();
        ArchitectureDetail.Validate(first, floor);
        RequireRejected(() => ArchitectureDetail.Validate(first, floor, retuned),
            "Changed authored detail rules require explicit compatibility before replacing a retained artifact.");

        ArchitectureDetailFact boundary = first.Facts.First(fact => fact.Kind != ArchitectureDetailKind.MaterialRegion);
        ArchitectureDetailSnapshot shallowBacking = first with
        {
            Facts = first.Facts.Select(fact => fact.Id == boundary.Id ? fact with { Depth = 1 } : fact).ToArray(),
        };
        shallowBacking = shallowBacking with { Identity = ArchitectureDetail.Identity(shallowBacking) };
        RequireRejected(() => ArchitectureDetail.ValidateVoxelResolution(shallowBacking, floor, 1,
                checked((definitions.Exploration.CeilingCells + 1) * definitions.Appearance.VoxelsPerCell)),
            "Boundary detail cannot consume the only voxel in a wall cell.");

        Console.WriteLine($"Architecture detail checks passed: {first.Facts.Length} deterministic budgeted facts across {first.Facts.Select(f => f.RegionId).Distinct().Count()} rooms.");
    }

    private static void RequireRejected(Action action, string message)
    {
        try { action(); }
        catch (Exception) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
