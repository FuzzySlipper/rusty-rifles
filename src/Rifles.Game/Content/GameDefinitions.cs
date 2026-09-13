using System.Text.Json;
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
    CharacterOptionsDefinition Characters, ItemDefinitions Items, ItemExplorationDefinition ItemExploration, ItemArtDefinition ItemArt)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter() },
    };

    internal static GameDefinitions Load(IEngineContext engine) => Load(path =>
    {
        using ContentReference reference = engine.Content.OpenReference(new ContentOpenRequest(path));
        ContentReferenceInfo info = engine.Content.ReadReferenceInfo(reference).Span[0];
        return engine.Content.ReadBytes(new ContentReadBytesRequest(reference, 0, checked((uint)info.ByteLength)));
    });
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
            Read<ItemArtDefinition>("definitions/item-art.json", x => x.Validate()));
        Require(result.Appearance.InitialStyle == result.Art.InitialStyle
            && result.Appearance.Styles.Select(s => s.Id).ToHashSet(StringComparer.Ordinal)
                .SetEquals(result.Art.Styles.Select(s => s.Id)), "tuning/appearance.json and definitions/world-art.json must have matching treatments and initial style");
        Require(result.Items.Items.All(i => result.ItemArt.Images.Any(image => image.Id == i.Image)), "item image references");
        string[] ownerKeys = result.Characters.Presets[0].Members.Select(m => "member:" + m.Id)
            .Concat(result.ItemExploration.Anchors.Select(a => a.Key)).ToArray();
        Require(result.Items.StartingItems.All(g => ownerKeys.Contains(g.Owner)
            && (g.Preset is null || result.Characters.Presets.Any(p => p.Id == g.Preset))), "starting loadout owners/presets");
        return result;
    }

    internal static void Require(bool condition, string field)
    {
        if (!condition) throw new InvalidDataException($"Invalid {field}.");
    }
}

internal sealed record GenerationDefinition(ulong Seed, string IntentId, string Title, string[] Tags,
    GraphRule[] Rules, GenerationPolicy Policy)
{
    internal void Validate()
    {
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(IntentId), nameof(IntentId));
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(Title), nameof(Title));
        GameDefinitions.Require(Tags is not null && Tags.All(t => !string.IsNullOrWhiteSpace(t)), nameof(Tags));
        GameDefinitions.Require(Rules is not null && Rules.All(Enum.IsDefined), nameof(Rules));
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
