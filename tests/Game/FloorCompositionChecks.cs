using System.Text.Json;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Generation;
using Rifles.Game.Combat;
using Rifles.Procgen;
using Rifles.Procgen.Generation;

internal static class FloorCompositionChecks
{
    internal static void Check(DungeonFloor floor, GameDefinitions definitions, DungeonGenerationResult generation)
    {
        floor = floor.WithArchitecture(definitions.Architecture);
        floor.Validate();
        ulong nextId = 1;
        var gates = GeneratedFeatures.Resolve(floor, () => nextId++);
        var keys = GeneratedFeatures.RequiredKeys(floor).Select(g => new GeneratedKey(g.Item, nextId++, "inspection:" + g.Item, g.Cell)).ToArray();
        var plates = gates.Where(g => g.Traversal == TraversalKind.Locked).Select(g => new GeneratedPlate(nextId++, g.Id,
            "inspection", floor.Routes.Single(r => r.Id == g.RouteId).Cells[0],
            keys.Single(k => k.Item == g.RequiredItem).Cell, definitions.GeneratedFeatures.PlateWeight)).ToArray();
        var progression = FloorProgression.Inspect(floor, gates, plates);
        if (!progression.Accepted) throw new Exception("Floor composition: " + string.Join(",", progression.Diagnostics));
        var exclusions = gates.Select(g => g.Cell).Concat(keys.Select(k => k.Cell)).Append(floor.Entrance).ToHashSet();
        var encounters = new EncounterPlacementResolver(definitions.EncounterPlacement)
            .Resolve(floor.Seed, floor, definitions.Combat, definitions.Crowd, exclusions);
        if (!encounters.Accepted) throw new Exception($"Encounter composition {floor.Seed}/{floor.IntentFloorId}: "
            + JsonSerializer.Serialize(encounters.Rejections));
        encounters.Validate(floor, definitions.Combat, definitions.Crowd);
        var supplies = RouteSupplies.Resolve(floor, definitions.RouteSupplies,
            encounters.Instances.Sum(e => definitions.Combat.Enemy(e.EnemyId).Vitality));
        string[] args = Environment.GetCommandLineArgs();
        int export = Array.IndexOf(args, "--export-floors");
        if (export >= 0 && export + 1 < args.Length)
        {
            string directory = Path.GetFullPath(args[export + 1]);
            Directory.CreateDirectory(directory);
            string name = $"{floor.Seed}-{floor.IntentFloorId}";
            File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(floor));
            File.WriteAllText(Path.Combine(directory, name + "-inspection.json"), JsonSerializer.Serialize(new
            { generation.Attempts, generation.Metrics, progression, encounters, supplies, plates, gates }));
        }
    }
}
