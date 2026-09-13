using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine;
using Rifles.Game.Dungeon;
using Rifles.Game.Party;
using Rifles.Procgen;
using Rifles.Procgen.Generation;

namespace Rifles.Game.Content;

internal sealed record GameDefinitions(ExplorationTuning Exploration, PartyDefinition Party,
    GenerationDefinition Generation, AppearanceDefinition Appearance)
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
        return new(Read<ExplorationTuning>("tuning/exploration.json", x => x.Validate()),
            Read<PartyDefinition>("definitions/party.json", x => x.Validate()),
            Read<GenerationDefinition>("tuning/generation.json", x => x.Validate()),
            Read<AppearanceDefinition>("tuning/appearance.json", x => x.Validate()));
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
