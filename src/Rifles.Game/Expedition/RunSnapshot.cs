using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rifles.Game.Content;
using Rifles.Game.Items;
using Rusty.Engine.Persistence;

namespace Rifles.Game.Expedition;

internal sealed record RunSnapshot(ExpeditionSnapshot Active, RetainedFloor[] Inactive);

internal sealed class RunCodec : IProductStateCodec<RunSnapshot>
{
    public uint SchemaVersion => 10;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };
    public void Encode(in RunSnapshot state, IBufferWriter<byte> destination)
    {
        using Utf8JsonWriter writer = new(destination);
        JsonSerializer.Serialize(writer, state, Json);
    }
    public RunSnapshot Decode(ReadOnlySpan<byte> payload) => JsonSerializer.Deserialize<RunSnapshot>(payload, Json)
        ?? throw new InvalidDataException("Empty run save.");

    internal static string[] Rewards(RunSnapshot run) => run.Active.Combat.Enemies
        .Concat(run.Inactive.SelectMany(f => f.Enemies)).Where(e => e.Vitality == 0)
        .Select(e => e.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();

    internal static Dictionary<ulong, string> Items(RunSnapshot run) => run.Active.Inventory.Packs
        .Concat(run.Inactive.SelectMany(f => f.Inventory.Packs)).SelectMany(p => p.Items)
        .ToDictionary(i => i.Id, i => i.Definition);

    internal static void Validate(RunSnapshot run, GameDefinitions definitions)
    {
        GameDefinitions.Require(run.Inactive is not null, "retained floors");
        var active = run.Active;
        string[] floorKeys = [active.Floor.IntentFloorId, .. run.Inactive.Select(f => f.Floor.IntentFloorId)];
        GameDefinitions.Require(floorKeys.Distinct().Count() == floorKeys.Length
            && floorKeys.All(k => active.Intent.Floors.Any(f => f.Id == k)), "visited floor identities");
        GameDefinitions.Require(run.Inactive.All(f => f.Inventory.Packs.All(p => !ItemInventory.IsMember(p.Owner.Key))), "retained floor inventory ownership");
        string[] rewards = Rewards(run);
        var items = Items(run);
        ExpeditionCodec.Validate(active, definitions, rewards, items);
        foreach (var floor in run.Inactive)
        {
            // Party actions belong only to the active floor. A resting floor has
            // no phantom travelling action or party reservation to restore.
            var idle = active with { Combat = active.Combat with
            {
                Members = active.Combat.Members.Select(m => m with { Action = null }).ToArray(),
                Magic = active.Combat.Magic! with { RestRemaining = 0, RestOwner = "" },
            }};
            ExpeditionCodec.Validate(floor.Join(idle, floor.Departure), definitions, rewards, items);
        }
        var floors = new[] { RetainedFloor.Capture(active) }.Concat(run.Inactive).ToArray();
        var ids = floors.SelectMany(f => FloorIds(f)).Concat(active.Inventory.Packs
            .Where(p => ItemInventory.IsMember(p.Owner.Key)).SelectMany(p => p.Items.Select(i => i.Id).Append(p.Owner.Id)))
            .Append(active.PartyId).ToArray();
        GameDefinitions.Require(ids.Distinct().Count() == ids.Length && ids.All(i => i > 0 && i < active.NextObjectId), "cross-floor object identities");
    }

    private static IEnumerable<ulong> FloorIds(RetainedFloor f) => new[]
    {
        f.Id, f.Actor.Id, f.Features.LanternId, f.Features.ExitId,
        f.Features.Dressing.BenchId, f.Features.Dressing.CrateId, f.Features.Dressing.ObserverId,
        f.ItemWorld.DoorId, f.ItemWorld.LeverId, f.ItemWorld.PlateId,
    }.Concat(f.GeneratedFeatures.Gates.Select(g => g.Id)).Concat(f.GeneratedFeatures.Plates.Select(p => p.Id))
        .Concat(f.GeneratedFeatures.Hazards.Select(h => h.Id)).Concat(f.Enemies.Select(e => e.Id))
        .Concat(f.Flights.Select(flight => flight.Id)).Concat(f.Inventory.Packs.SelectMany(p => p.Items.Select(i => i.Id).Append(p.Owner.Id)));
}
