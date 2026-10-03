using System.Text.Json.Nodes;
using Rifles.Game.Content;

internal static class ContentChecks
{
    internal static void Run(string root)
    {
        byte[] Read(string path) => path == "definitions/company-experiment.json" ? "{\"enabled\":false}"u8.ToArray() : File.ReadAllBytes(Path.Combine(root, path));
        GameDefinitions lazy = GameDefinitions.Load(path => path == "definitions/martial-drills.json" ? "{"u8.ToArray() : Read(path));
        Check.Require(lazy.Characters.Presets.Length > 0, "Invalid debug-only drill content does not block ordinary content admission.");
        Check.Rejected(() => _ = lazy.MartialDrills, "Invalid drills fail only when requested.", "definitions/martial-drills.json");
        foreach ((string asset, string array, string field) in new[]
        {
            ("definitions/character-options.json", "archetypes", "basePower"),
            ("definitions/spells.json", "spells", "stacking"),
            ("definitions/items.json", "startingItems", "preset"),
        })
        {
            JsonNode document = JsonNode.Parse(Read(asset))!;
            JsonObject first = document[array]![0]!.AsObject();
            Check.Require(first.Remove(field), $"Missing-field fixture actually removes {field}.");
            byte[] missing = System.Text.Encoding.UTF8.GetBytes(document.ToJsonString());
            Check.Rejected(() => GameDefinitions.Load(path => path == asset ? missing : Read(path)),
                $"Required authored field {field} is refused by name.", field);
        }
    }
}
