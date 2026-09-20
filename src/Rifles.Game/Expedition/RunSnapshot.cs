using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rifles.Game.Content;
using Rifles.Game.Items;
using Rusty.Engine.Persistence;

namespace Rifles.Game.Expedition;

internal sealed record RunSnapshot(ExpeditionSnapshot Active, RetainedFloor[] Inactive, RunProgress Progress);

internal sealed class RunCodec : IProductStateCodec<RunSnapshot>
{
    public uint SchemaVersion => 11;
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
        GameDefinitions.Require(run.Progress is not null && run.Progress.Maps is not null, "run progress");
        _ = definitions.Run.Difficulty(run.Progress.Difficulty);
        var visited = new[] { active.Floor }.Concat(run.Inactive.Select(f => f.Floor)).ToArray();
        GameDefinitions.Require(run.Progress.Maps.Select(m => m.FloorKey).Distinct().Count() == run.Progress.Maps.Length
            && run.Progress.Maps.All(m => visited.Any(f => f.IntentFloorId == m.FloorKey)
                && m.Cells.Distinct().Count() == m.Cells.Length
                && m.Cells.All(visited.Single(f => f.IntentFloorId == m.FloorKey).Cells.Contains)), "discovered map cells");
        if (run.Progress.Completed)
        {
            var finale = active.Floor.IntentFloorId == active.Intent.ObjectiveFloor
                ? RetainedFloor.Capture(active) : run.Inactive.SingleOrDefault(f => f.Floor.IntentFloorId == active.Intent.ObjectiveFloor);
            GameDefinitions.Require(finale is not null && finale.Enemies.All(e => e.Vitality == 0)
                && finale.Features.ExitUsed && visited.Length == active.Intent.Floors.Length
                && active.Floor.IntentFloorId == active.Intent.ObjectiveFloor && active.Paused
                && active.Exploration.Position == active.Floor.Exit, "completed expedition objective");
        }
        long completionExperience = run.Progress.Completed ? definitions.Run.FinaleExperience : 0;
        string[] floorKeys = [active.Floor.IntentFloorId, .. run.Inactive.Select(f => f.Floor.IntentFloorId)];
        GameDefinitions.Require(floorKeys.Distinct().Count() == floorKeys.Length
            && floorKeys.All(k => active.Intent.Floors.Any(f => f.Id == k)), "visited floor identities");
        GameDefinitions.Require(run.Inactive.All(f => f.Inventory.Packs.All(p => InventoryOwner.Parse(p.Owner.Key) is not (MemberOwner or PartyOwner))), "retained floor inventory ownership");
        string[] rewards = Rewards(run);
        var items = Items(run);
        ExpeditionCodec.Validate(active, definitions, rewards, items, completionExperience);
        foreach (var floor in run.Inactive)
        {
            // Party actions belong only to the active floor. A resting floor has
            // no phantom travelling action or party reservation to restore.
            var idle = active with { RestRemaining = 0, RestOwner = "", Combat = active.Combat with
            {
                Members = active.Combat.Members.Select(m => m with { Action = null }).ToArray(),
            }};
            ExpeditionCodec.Validate(floor.Join(idle, floor.Departure), definitions, rewards, items, completionExperience);
        }
        var floors = new[] { RetainedFloor.Capture(active) }.Concat(run.Inactive).ToArray();
        var ids = floors.SelectMany(f => FloorIds(f)).Concat(active.Inventory.Packs
            .Where(p => InventoryOwner.Parse(p.Owner.Key) is (MemberOwner or PartyOwner)).SelectMany(p => p.Items.Select(i => i.Id).Append(p.Owner.Id)))
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
