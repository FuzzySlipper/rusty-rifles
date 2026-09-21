using Rifles.Game.Generation;
using Rifles.Game.Debugging;
using Rifles.Game.Audio;
using Rifles.Game.Expedition;
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

internal sealed record GameDefinitions(ExplorationTuning Exploration, PartyDefinition Party, FormationDefinition Formation,
    GenerationDefinition Generation, AppearanceDefinition Appearance, FeatureDefinition Features, WorldArtDefinition Art,
    CharacterOptionsDefinition Characters, ItemDefinitions Items, ItemExplorationDefinition ItemExploration, ItemArtDefinition ItemArt, CombatDefinition Combat, CrowdDefinition Crowd, MagicDefinition Magic, HudTuning Hud, RoomCatalogue Rooms, GeneratedFeatureDefinition GeneratedFeatures, RouteSupplyDefinition RouteSupplies, HazardDefinition Hazards, EncounterPlacementDefinition EncounterPlacement, ArchitectureDetailDefinition Architecture, RunDefinition Run, AudioDefinition Audio, ChargeDefinition Charge, MartialDrillDefinitionSet MartialDrills)
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
            Read<FormationDefinition>("definitions/formation.json", x => x.Validate()),
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
            Read<HudTuning>("tuning/hud.json", x => x.Validate()),
            Read<RoomCatalogue>("definitions/rooms.json", x => x.Validate()),
            Read<GeneratedFeatureDefinition>("definitions/generated-features.json", x => x.Validate()),
            Read<RouteSupplyDefinition>("definitions/route-supplies.json", x => x.Validate()),
            Read<HazardDefinition>("definitions/generated-hazards.json", x => x.Validate()),
            Read<EncounterPlacementDefinition>("definitions/encounter-placement.json", x => x.Validate()),
            Read<ArchitectureDetailDefinition>("definitions/architecture-detail.json", x => x.Validate()),
            Read<RunDefinition>("definitions/expedition.json", x => x.Validate()),
            Read<AudioDefinition>("definitions/audio.json", x => x.Validate()),
            Read<ChargeDefinition>("definitions/charge.json", x => x.Validate()),
            Read<MartialDrillDefinitionSet>("definitions/martial-drills.json", x => x.Validate()));
        result.MartialDrills.ValidateAgainst(result.Characters, result.Formation, result.Combat, result.Crowd);
        // The formation file is the one authored layout. Adapt its integral
        // 3x3 coordinates to the existing half-cell projection once at admission.
        result = result with { Party = result.Party with { Positions = result.Formation.Cells.Select(cell =>
            new FormationPositionDefinition(cell.Id, cell.Name, 1 - cell.Forward,
                cell.Forward / 2f, cell.Left / 2f, cell.Commander)).ToArray() } };
        Require(result.Appearance.InitialStyle == result.Art.InitialStyle
            && result.Appearance.Styles.Select(s => s.Id).ToHashSet(StringComparer.Ordinal)
                .SetEquals(result.Art.Styles.Select(s => s.Id)), "tuning/appearance.json and definitions/world-art.json must have matching treatments and initial style");
        Require(result.Items.Items.All(i => result.ItemArt.Images.Any(image => image.Id == i.Image)), "item image references");
        foreach (GearDefinition item in result.Items.Items.Where(item => item.Weapon is not null))
        {
            MartialWeaponDefinition weapon = item.Weapon!;
            Require(result.Formation.Weapons.Any(reach => reach.Id == weapon.Reach), $"weapon reach for '{item.Id}'");
            Require(weapon.Bayonet is null || result.Formation.Weapons.Any(reach => reach.Id == weapon.Bayonet.Reach), $"bayonet reach for '{item.Id}'");
        }
        // Starting loadouts resolve against the selected actual roster: a
        // preset-agnostic grant must name an instance present in every preset,
        // while a preset-scoped grant must name one of that preset's instances.
        foreach (StarterPartyPresetDefinition preset in result.Characters.Presets)
        {
            string[] rosterKeys = preset.Members.Select(m => "member:" + m.Id)
                .Append(ItemInventory.PartyKey)
                .Concat(result.ItemExploration.Anchors.Select(a => a.Key)).ToArray();
            Require(result.Items.StartingItems.Where(g => g.Preset is null || g.Preset == preset.Id)
                .All(g => rosterKeys.Contains(g.Owner)), $"starting loadout owners for preset '{preset.Id}'");
        }

        Require(result.Items.StartingItems.All(g => g.Preset is null || result.Characters.Presets.Any(p => p.Id == g.Preset)), "starting loadout presets");
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
        HashSet<string> positionIds = result.Party.Positions.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var preset in result.Characters.Presets)
        {
            Require(preset.Members.Length == result.Party.MaxPartySize, "preset party capacity");
            MemberDefinition[] roster = result.Characters.ResolvePreset(preset.Id);
            Require(roster.Count(member => member.Commander) == 1, "preset commander");
            Require(preset.Members.All(m => positionIds.Contains(m.Position)), "preset formation positions");
            Require(roster.All(member => result.Party.Positions.Single(position => position.Id == member.Position).Commander == member.Commander), "preset commander position");
            // Spell and resistance configuration is referenced by archetype, so
            // every archetype on the selected actual roster must resolve.
            Require(preset.Members.All(m => result.Magic.StartingSpells.ContainsKey(m.Archetype)),
                $"starting spells for preset '{preset.Id}' roster");
            Require(preset.Members.All(m => result.Magic.Resistances.ContainsKey(m.Archetype)),
                $"magic resistance for preset '{preset.Id}' roster");
        }

        Require(result.Combat.Enemies.Select(e => e.Id).All(result.Magic.Resistances.ContainsKey), "magic resistance profiles");
        Require(result.Magic.EnemySpells.Keys.All(id => result.Combat.Enemies.Any(e => e.Id == id)), "enemy spell profiles");
        Require(result.Items.Item(result.Magic.RestItem).Kind == Rusty.Engine.Mechanics.ItemKind.Fungible
            && result.Items.Item(result.Magic.RevivalItem).Kind == Rusty.Engine.Mechanics.ItemKind.Fungible, "recovery consumables");
        Require(result.Items.Item(result.GeneratedFeatures.KeyItem).Kind == Rusty.Engine.Mechanics.ItemKind.Unique
            && result.Items.Item(result.GeneratedFeatures.KeyItem).Use == ItemUse.Key
            && result.Items.Item(result.GeneratedFeatures.KeyItem).Cost == 0 && result.Combat.RecoverThrownItems,
            "recoverable generated key and weight puzzles");
        Require(result.Items.Item(result.GeneratedFeatures.WeightItem).Mass >= result.GeneratedFeatures.PlateWeight,
            "generated counterweight supply");
        result.EncounterPlacement.ValidateAgainst(result.Combat, result.Crowd);
        Require(result.RouteSupplies.Supplies.All(s => s.Quantity <= result.Items.Item(s.Item).MaximumQuantity), "generated supply items");
        return result;
    }

    internal static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string field)
    {
        if (!condition) throw new InvalidDataException($"Invalid {field}.");
    }
}

internal sealed record GenerationDefinition(ulong Seed, ExpeditionDefinition Expedition, GenerationPolicy Policy, ElevationDefinition Elevation)
{
    internal void Validate()
    {
        Expedition.Validate();
        Elevation.Validate();
        GameDefinitions.Require(GenerationPolicyValidation.IsValid(Policy), nameof(Policy));
    }
}

/// <summary>
/// Authored formation and capacity policy. The roster is not duplicated here:
/// parties build from the selected character preset's archetype references.
/// </summary>
internal sealed record PartyDefinition(int MaxPartySize)
{
    // Resolved at content admission from FormationDefinition; never authored twice.
    internal FormationPositionDefinition[] Positions { get; init; } = [];
    internal void Validate() => GameDefinitions.Require(MaxPartySize >= 1, "MaxPartySize capacity");
}
