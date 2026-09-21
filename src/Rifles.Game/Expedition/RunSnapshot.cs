using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rifles.Game.Content;
using Rifles.Game.Items;
using Rusty.Engine.Persistence;

namespace Rifles.Game.Expedition;

internal sealed record RunSnapshot(ExpeditionSnapshot Active, RetainedFloor[] Inactive, RunProgress Progress);

/// <summary>
/// Run-level save admission over the one current schema. Encoding itself is
/// the Engine source-generated codec; this class owns validation only.
/// </summary>
internal static class RunCodec
{
    internal static IProductStateCodec<RunSnapshot> CreateStoreCodec() =>
        new JsonProductStateCodec<RunSnapshot>(RunSaveJsonContext.Default.RunSnapshot);


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
            GameDefinitions.Require(finale is not null && SaveIdentities.AllDefeated(finale.Enemies)
                && finale.Features.ExitUsed && visited.Length == active.Intent.Floors.Length
                && active.Floor.IntentFloorId == active.Intent.ObjectiveFloor && active.Paused
                && active.Exploration.Position == active.Floor.Exit, "completed expedition objective");
        }
        string[] floorKeys = [active.Floor.IntentFloorId, .. run.Inactive.Select(f => f.Floor.IntentFloorId)];
        GameDefinitions.Require(floorKeys.Distinct().Count() == floorKeys.Length
            && floorKeys.All(k => active.Intent.Floors.Any(f => f.Id == k)), "visited floor identities");
        GameDefinitions.Require(run.Inactive.All(f => f.Inventory.Packs.All(p => InventoryOwner.Parse(p.Owner.Key) is not (MemberOwner or PartyOwner))), "retained floor inventory ownership");
        var items = Items(run);
        ExpeditionCodec.Validate(active, definitions, items);
        // Retained floors validate as floors: floor-local consistency only,
        // no entities, no party, no synthetic expedition merge.
        IReadOnlyDictionary<string, string> roster = active.Roster.ToDictionary(m => m.Id, m => m.Archetype, StringComparer.Ordinal);
        foreach (var floor in run.Inactive) RetainedFloor.Validate(floor, definitions, active.Intent, items, roster, active.PartyId);
        SaveIdentities.RequireCrossFloorUnique(run);
    }
}
