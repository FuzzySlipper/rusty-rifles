using Rifles.Procgen.Expeditions;
using System.Text.Json;
using Rifles.Game.Combat;
using Rifles.Game.Magic;
using System.Text.Json.Serialization;
using Rusty.Engine;
using Rifles.Game.Dungeon;
using Rifles.Game.Party;
using Rifles.Game.Items;
using Rifles.Procgen;
using Rifles.Procgen.Generation;

namespace Rifles.Game.Content;

internal sealed record GameDefinitions(ExplorationTuning Exploration, PartyDefinition Party,
    GenerationDefinition Generation, AppearanceDefinition Appearance, FeatureDefinition Features, WorldArtDefinition Art,
    CharacterOptionsDefinition Characters, ItemDefinitions Items, ItemExplorationDefinition ItemExploration, ItemArtDefinition ItemArt, CombatDefinition Combat, CrowdDefinition Crowd, MagicDefinition Magic, HudTuning Hud)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter() },
    };

    internal static GameDefinitions Load(ProductContent content) => Load(content.ReadBytes);
    internal static GameDefinitions Load(Func<string, ReadOnlyMemory<byte>> read)
    {
        T Read<T>(string path, Action<T> validate)
        {
            try
            {
                T result = JsonSerializer.Deserialize<T>(read(path).Span, Json)
                    ?? throw new InvalidDataException("Document must not be null.");
                validate(result);
                return result;
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                throw new InvalidDataException($"Content '{path}': {error.Message}", error);
            }
        }
        GameDefinitions result = new(Read<ExplorationTuning>("tuning/exploration.json", x => x.Validate()),
            Read<PartyDefinition>("definitions/party.json", x => x.Validate()),
            Read<GenerationDefinition>("tuning/generation.json", x => x.Validate()),
            Read<AppearanceDefinition>("tuning/appearance.json", x => x.Validate()),
            Read<FeatureDefinition>("definitions/exploration-features.json", x => x.Validate()),
            Read<WorldArtDefinition>("definitions/world-art.json", x => x.Validate()),
            Read<CharacterOptionsDefinition>("definitions/character-options.json", x => x.Validate()),
            Read<ItemDefinitions>("definitions/items.json", x => x.Validate()),
            Read<ItemExplorationDefinition>("definitions/item-exploration.json", x => x.Validate()),
            Read<ItemArtDefinition>("definitions/item-art.json", x => x.Validate()),
            Read<CombatDefinition>("definitions/combat.json", x => x.Validate()),
            Read<CrowdDefinition>("definitions/crowds.json", x => x.Validate()),
            Read<MagicDefinition>("definitions/spells.json", x => x.Validate()),
            Read<HudTuning>("tuning/hud.json", x => x.Validate()));
        Require(result.Appearance.InitialStyle == result.Art.InitialStyle
            && result.Appearance.Styles.Select(s => s.Id).ToHashSet(StringComparer.Ordinal)
                .SetEquals(result.Art.Styles.Select(s => s.Id)), "tuning/appearance.json and definitions/world-art.json must have matching treatments and initial style");
        Require(result.Items.Items.All(i => result.ItemArt.Images.Any(image => image.Id == i.Image)), "item image references");
        string[] ownerKeys = result.Characters.Presets[0].Members.Select(m => "member:" + m.Id)
            .Concat(result.ItemExploration.Anchors.Select(a => a.Key)).ToArray();
        Require(result.Items.StartingItems.All(g => ownerKeys.Contains(g.Owner)
            && (g.Preset is null || result.Characters.Presets.Any(p => p.Id == g.Preset))), "starting loadout owners/presets");
        Require(result.Items.Item(result.Combat.AmmunitionItem).Kind == Rusty.Engine.Mechanics.ItemKind.Fungible, "combat ammunition");
        Require(result.Items.Items.Where(i => i.Kind == Rusty.Engine.Mechanics.ItemKind.Unique && i.Ammunition.Length > 0)
            .All(i => i.Ammunition == result.Items.Item(result.Combat.AmmunitionItem).Ammunition), "combat rifle ammunition compatibility");
        foreach (var enemy in result.Combat.Enemies)
        {
            Require(result.Crowd.Footprints.ContainsKey(enemy.Footprint), "enemy footprint reference");
            foreach (var loot in enemy.Loot) Require(loot.Quantity <= result.Items.Item(loot.Definition).MaximumQuantity, "enemy loot");
            Require(enemy.Attack != CombatActionKind.Fire || enemy.Loot.Any(l => result.Items.Item(l.Definition).Kind == Rusty.Engine.Mechanics.ItemKind.Unique
                && result.Items.Item(l.Definition).Ammunition == result.Items.Item(result.Combat.AmmunitionItem).Ammunition), "ranged enemy rifle");
        }
        string[] memberIds = result.Characters.Presets[0].Members.Select(m => m.Id).ToArray();
        Require(result.Magic.StartingSpells.Keys.ToHashSet().SetEquals(memberIds), "starting spell roster");
        Require(memberIds.Concat(result.Combat.Enemies.Select(e => e.Id)).All(result.Magic.Resistances.ContainsKey), "magic resistance profiles");
        Require(result.Magic.EnemySpells.Keys.All(id => result.Combat.Enemies.Any(e => e.Id == id)), "enemy spell profiles");
        Require(result.Items.Item(result.Magic.RestItem).Kind == Rusty.Engine.Mechanics.ItemKind.Fungible
            && result.Items.Item(result.Magic.RevivalItem).Kind == Rusty.Engine.Mechanics.ItemKind.Fungible, "recovery consumables");
        return result;
    }

    internal static void Require(bool condition, string field)
    {
        if (!condition) throw new InvalidDataException($"Invalid {field}.");
    }
}

internal sealed record GenerationDefinition(ulong Seed, ExpeditionDefinition Expedition, GenerationPolicy Policy)
{
    internal void Validate()
    {
        Expedition.Validate();
        GameDefinitions.Require(GenerationPolicyValidation.IsValid(Policy), nameof(Policy));
    }
}

internal sealed record PartyDefinition(MemberDefinition[] Members)
{
    internal void Validate()
    {
        GameDefinitions.Require(Members is { Length: 4 }, nameof(Members));
        GameDefinitions.Require(Members.All(m => m is not null && !string.IsNullOrWhiteSpace(m.Id)
            && !string.IsNullOrWhiteSpace(m.Name) && Enum.IsDefined(m.Slot) && m.MaximumVitality > 0), "Members fields");
        GameDefinitions.Require(Members.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() == Members.Length, "Members.Id uniqueness");
        GameDefinitions.Require(Members.Select(m => m.Slot).Distinct().Count() == Members.Length, "Members.Slot uniqueness");
    }
}
