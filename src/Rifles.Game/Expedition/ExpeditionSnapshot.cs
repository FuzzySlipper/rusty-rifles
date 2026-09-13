using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine.Persistence;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Party;

namespace Rifles.Game.Expedition;

internal sealed record ExpeditionSnapshot(Guid Id, ulong FloorId, ulong PartyId, ulong NextObjectId,
    DungeonFloor Floor, ExplorationSnapshot Exploration, MemberDefinition[] Roster,
    MemberSnapshot[] Members, bool Paused, string SelectedMember, PatrolSnapshot Actor, FeatureSnapshot Features);

internal sealed class ExpeditionCodec : IProductStateCodec<ExpeditionSnapshot>
{
    public uint SchemaVersion => 1;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };
    public void Encode(in ExpeditionSnapshot state, IBufferWriter<byte> destination)
    {
        using Utf8JsonWriter writer = new(destination);
        JsonSerializer.Serialize(writer, state, Json);
    }
    public ExpeditionSnapshot Decode(ReadOnlySpan<byte> payload) => JsonSerializer.Deserialize<ExpeditionSnapshot>(payload, Json)
        ?? throw new InvalidDataException("Empty expedition save.");

    internal static (ExplorationState Exploration, PartyState Party, PatrolActor Actor) Validate(ExpeditionSnapshot saved, GameDefinitions definitions)
    {
        GameDefinitions.Require(saved.Id != Guid.Empty && saved.FloorId > 0 && saved.PartyId > 0
            && saved.PartyId != saved.FloorId && saved.NextObjectId > Math.Max(saved.FloorId, saved.PartyId), "Save identities");
        saved.Floor.Validate();
        new PartyDefinition(saved.Roster).Validate();
        PartyState party = new(saved.Roster);
        party.Restore(saved.Members);
        ExplorationState exploration = ExplorationState.Restore(saved.Exploration, saved.Floor, definitions.Exploration);
        GameDefinitions.Require(party.Members.Any(m => m.Definition.Id == saved.SelectedMember), "Save.SelectedMember");
        ulong[] ids = [saved.FloorId, saved.PartyId, saved.Actor.Id, saved.Features.LanternId, saved.Features.ExitId];
        GameDefinitions.Require(ids.All(id => id > 0 && id <= uint.MaxValue && id < saved.NextObjectId) && ids.Distinct().Count() == ids.Length, "Save object identities");
        GameDefinitions.Require(saved.Features.LanternRevision > 0, "Save lantern revision");
        PatrolActor actor = PatrolActor.Restore(saved.Actor, saved.Floor, definitions.Exploration with { StepSeconds = definitions.Features.ActorStepSeconds });
        // Validate occupancy and both in-flight reservations before realizing any replacement scene.
        MovementGrid grid = new(saved.Floor.Cells.ToHashSet(), (_, _) => true);
        exploration.Bind(grid, saved.PartyId);
        actor.Bind(grid);
        return (exploration, party, actor);
    }
}
