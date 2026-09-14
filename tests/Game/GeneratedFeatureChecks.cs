using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Generation;
using Rifles.Procgen;
using Rifles.Procgen.Expeditions;

internal static class GeneratedFeatureChecks
{
    internal static void Run(GameDefinitions definitions)
    {
        var plan = new ExpeditionGenerator().Generate(definitions.Generation.Expedition, definitions.Generation.Seed).Expedition!;
        // The final-floor shortcut and locked gate are both realized, not just the arrival loop.
        var floor = DungeonFloor.Generate(plan.Floors.Single(f => f.Id == "redoubt"), definitions.Generation.Policy, definitions.Rooms, definitions.Generation.Elevation);
        floor.Validate();
        if (!floor.Connectors.Any(c => c.Kind == FloorConnectorKind.Stair)
            || !floor.Connectors.Any(c => c.Kind == FloorConnectorKind.Bridge))
            throw new Exception("Raised rooms and bridge routes must retain actual step connectors.");
        foreach (var connector in floor.Connectors)
            if (Math.Abs(floor.Level(connector.From) - floor.Level(connector.To)) != 1)
                throw new Exception("Stairs must fit the Engine adjacent-level step contract.");
        // At every tread column, presentation feet must stay above its actual voxel top.
        foreach (var edge in floor.Connectors.Where(c => floor.Level(c.From) >= 0 && floor.Level(c.To) > floor.Level(c.From)))
            for (int x = 0; x < definitions.Appearance.VoxelsPerCell; x++)
                for (int z = 0; z < definitions.Appearance.VoxelsPerCell; z++)
                {
                    int resolution = definitions.Appearance.VoxelsPerCell;
                    float size = definitions.Exploration.CellSize;
                    var point = new System.Numerics.Vector2(edge.From.X - .5f + (x + .5f) / resolution,
                        edge.From.Y - .5f + (z + .5f) / resolution);
                    float top = (1 + floor.Level(edge.From)) * size + FloorSurface.TreadHeight(x, z,
                        edge.To.X - edge.From.X, edge.To.Y - edge.From.Y, resolution) * size / resolution;
                    if (FloorSurface.Height(floor, point, size, resolution) < top)
                        throw new Exception("Camera and sprite support must not interpolate through a solid stair tread.");
                }
        var elevationReplay = FloorElevation.Resolve(floor, definitions.Generation.Elevation);
        if (!elevationReplay.Cells.SequenceEqual(floor.Elevations) || !elevationReplay.Connectors.SequenceEqual(floor.Connectors))
            throw new Exception("Elevation and connector identities must replay exactly.");
        ulong id = 1;
        var gates = GeneratedFeatures.Resolve(floor, () => id++);
        if (!gates.Any(g => g.Traversal == TraversalKind.Locked) || !gates.Any(g => g.Traversal == TraversalKind.OneWayReturn))
            throw new Exception("Generated protected routes must become feature instances.");
        var keys = GeneratedFeatures.RequiredKeys(floor).Select(g => new GeneratedKey(g.Item, id++, "test:" + g.Item, g.Cell)).ToArray();
        var plates = gates.Where(g => g.Traversal == TraversalKind.Locked).Select(g => new GeneratedPlate(id++, g.Id, "test",
            floor.Routes.Single(r => r.Id == g.RouteId).Cells[0], floor.Grants.Single(k => k.Item == g.RequiredItem).Cell, definitions.GeneratedFeatures.PlateWeight)).ToArray();
        var state = new GeneratedFeatureSnapshot(1, gates, keys, [], [], plates);
        GeneratedFeatures.Validate(state, floor);
        if (!FloorProgression.Inspect(floor, gates, plates).Accepted)
            throw new Exception("Composed key and counterweight prerequisites must have a physical solution.");
        if (FloorProgression.Inspect(floor, gates, plates.Select(p => p with { Cell = floor.Exit }).ToArray()).Accepted)
            throw new Exception("A counterweight plate behind its protected gate must reject.");
        bool rejected = false;
        try { GeneratedFeatures.Validate(state with { Gates = [] }, floor); }
        catch (InvalidDataException) { rejected = true; }
        if (!rejected) throw new Exception("Missing generated barriers must reject the save.");
        var supplies = RouteSupplies.Resolve(floor, definitions.RouteSupplies, 120);
        if (supplies.Single(s => s.Item == definitions.RouteSupplies.Ammunition).Quantity != 30)
            throw new Exception("Ammo allowance must reflect encounter pressure.");
        if (!supplies.SequenceEqual(RouteSupplies.Resolve(floor, definitions.RouteSupplies, 120)))
            throw new Exception("Supply placements must replay exactly.");
        rejected = false;
        try { RouteSupplies.Resolve(floor, definitions.RouteSupplies with { MaximumAmmunition = 1 }, 120); }
        catch (InvalidDataException) { rejected = true; }
        if (!rejected) throw new Exception("Insufficient ammo budget must reject explicitly.");
        long damage = 0;
        var hazard = new GeneratedHazard(1, "test", floor.Entrance, 0, false, false);
        hazard = GeneratedHazards.Advance(hazard, definitions.Hazards, .1, floor.Entrance, value => damage += value);
        hazard = GeneratedHazards.Advance(hazard, definitions.Hazards, .1, floor.Entrance, value => damage += value);
        if (damage != definitions.Hazards.Damage) throw new Exception("A pulse hits once, not once per frame.");
        var capturedHazard = hazard;
        var advanced = GeneratedHazards.Advance(hazard, definitions.Hazards, .1, floor.Entrance, _ => { });
        if (advanced != GeneratedHazards.Advance(capturedHazard, definitions.Hazards, .1, floor.Entrance, _ => { }))
            throw new Exception("Saved hazard phase must replay without a separate clock.");
        var disabled = hazard with { Disabled = true, HitThisPulse = false };
        GeneratedHazards.Advance(disabled, definitions.Hazards, .1, floor.Entrance, _ => throw new Exception("Disabled trap hurt the party."));
        Console.WriteLine("Generated feature checks passed: protected routes, key bindings and missing-barrier rejection.");
    }
}
