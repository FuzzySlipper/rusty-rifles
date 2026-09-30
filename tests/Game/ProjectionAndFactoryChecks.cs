using Rifles.Procgen.Generation;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Expedition;
using Rifles.Game.Items;
using Rifles.Game.Party;
using Rifles.Game.Presentation;
using Rifles.Procgen.Expeditions;
using Rusty.Engine;

internal static class ProjectionAndFactoryChecks
{
    internal static void Run(GameDefinitions definitions)
    {
        PartyState party = new(definitions.Party.Positions, definitions.Party.MaxPartySize,
            definitions.Characters, definitions.Characters.DefaultPresetId);
        party.Soldiers[0].ApplyDamage(1);
        SessionValueBuilder values = new();
        UiValue projection = values.Build(SessionProjection.Roster(values, party, CardinalDirection.West, definitions.Formation));
        foreach (var member in party.Members)
        {
            var node = Field(projection, projection.Root, member.InstanceId);
            Check.Require(Field(projection, node, "vitality").NumberValue == member.Vitality, "Roster projects current vitality.");
            Check.Require(Text(projection, Field(projection, node, "position")) == member.Position, "Roster projects the actual formation position.");
            Check.Require(Text(projection, Field(projection, node, "facing")) == "West", "Every member faces with the party.");
            Check.Require(Field(projection, node, "commander").NumberValue == (member.Definition.Commander ? 1 : 0), "Roster projects the commander role.");
            Check.Require(!Children(projection, node).Any(child => Key(projection, child) is "melee" or "ranged" or "casting"), "Roster has no obsolete rank reach fields.");
        }
        var intent = new ExpeditionGenerator().Generate(definitions.Generation.Expedition, definitions.Generation.Seed).Expedition!;
        var floor = DungeonFloor.Generate(intent.Floors[0], definitions.Generation.Policy, definitions.Rooms, definitions.Generation.Elevation).WithArchitecture(definitions.Architecture);
        ulong next = 100;
        ItemInventory inventory = new(definitions.Items, []);
        Dictionary<string, Rifles.Procgen.Generation.GridPoint> drops = [];
        var features = FloorFactory.CreateGeneratedFeatures(intent, floor, definitions, inventory, drops, () => next++);
        Check.Require(features.Gates.Length == floor.Routes.Count(route => route.Traversal != Rifles.Procgen.TraversalKind.Open), "Floor factory composes every generated gate.");
        Check.Require(features.Plates.Length == features.Gates.Count(gate => gate.Traversal == Rifles.Procgen.TraversalKind.Locked), "Every locked gate receives a counterweight plate.");
        Check.Require(features.Keys.All(key => inventory.Items(key.Owner).Any(item => item.Entity == key.Entity)), "Generated keys are granted to their actual floor inventory owners.");
        features = FloorFactory.AddRouteSupplies(floor, definitions, inventory, drops, features, () => next++);
        FloorFacts.ValidateAnchors(features, drops.Select(drop => new Rifles.Game.Combat.DropSnapshot(drop.Key, drop.Value)).ToArray(), inventory, definitions, null);
        MovementGrid movement = new(floor.Cells.ToHashSet(), (_, _) => true, definitions.Crowd);
        FloorFactory.ConfigureDoorClearance(movement, floor, floor.Entrance, definitions.ItemExploration.Reach);
        Check.Require(features.Revision > 0 && next > 100, "Pure floor composition allocates distinct admitted feature identities.");
    }

    internal static void SaveEnums()
    {
        HashSet<Type> visited = [], enums = [];
        void Visit(Type type)
        {
            if (!visited.Add(type)) return;
            if (type.IsEnum) { enums.Add(type); return; }
            if (type.IsArray) { Visit(type.GetElementType()!); return; }
            if (type.IsGenericType) foreach (Type argument in type.GetGenericArguments()) Visit(argument);
            if (type == typeof(string) || type.IsPrimitive || type == typeof(decimal) || type == typeof(Guid)) return;
            if (type.Namespace?.StartsWith("Rifles.") != true && type.Namespace?.StartsWith("Rusty.Engine.") != true) return;
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)) Visit(property.PropertyType);
        }
        Visit(typeof(RunSnapshot));
        Type[] converters = typeof(RunSaveJsonContext).GetCustomAttribute<JsonSourceGenerationOptionsAttribute>()!.Converters!;
        foreach (Type type in enums)
        {
            Check.Require(converters.Contains(typeof(JsonStringEnumConverter<>).MakeGenericType(type)), $"Save enum {type.Name} has a named generic converter.");
            string json = JsonSerializer.Serialize(Enum.GetValues(type).GetValue(0), type, RunSaveJsonContext.Default.Options);
            Check.Require(json.StartsWith('"'), $"Save enum {type.Name} serializes by name.");
        }
        Check.Require(enums.Count > 0, "Save enum graph walk finds enum contracts.");
    }

    private static IEnumerable<StructuredValueNode> Children(UiValue value, StructuredValueNode node) =>
        value.Edges.Span.Slice((int)node.FirstEdge, (int)node.ChildCount).ToArray().Select(index => value.Nodes.Span[(int)index]);
    private static StructuredValueNode Field(UiValue value, uint root, string name) => Field(value, value.Nodes.Span[(int)root], name);
    private static StructuredValueNode Field(UiValue value, StructuredValueNode root, string name) => Children(value, root).Single(child => Key(value, child) == name);
    private static string Key(UiValue value, StructuredValueNode node) => Encoding.UTF8.GetString(value.Utf8.Span.Slice((int)node.KeyOffset, (int)node.KeyLen));
    private static string Text(UiValue value, StructuredValueNode node) => Encoding.UTF8.GetString(value.Utf8.Span.Slice((int)node.TextOffset, (int)node.TextLen));
}
