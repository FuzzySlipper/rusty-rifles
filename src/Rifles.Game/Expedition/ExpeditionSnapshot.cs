using Rifles.Game.Generation;
using Rifles.Procgen;
using Rifles.Procgen.Expeditions;
using Rifles.Procgen.Generation;
using System.Buffers;
using Rifles.Game.Characters;
using Rifles.Game.Combat;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine.Persistence;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Magic;
using Rifles.Game.Party;
using Rifles.Game.Items;

namespace Rifles.Game.Expedition;

internal sealed record ExpeditionSnapshot(Guid Id, ulong FloorId, ulong PartyId, ulong NextObjectId,
    DungeonFloor Floor, ExplorationSnapshot Exploration, MemberDefinition[] Roster,
    MemberSnapshot[] Members, bool Paused, string SelectedMember, PatrolSnapshot Actor, FeatureSnapshot Features, string Preset, InventorySnapshot Inventory, ItemExplorationSnapshot ItemWorld, CombatSnapshot Combat, ResolvedExpedition Intent, GeneratedFeatureSnapshot GeneratedFeatures, EncounterPlacementResult EncounterPlacement, double RestRemaining = 0, string RestOwner = "", FormationExecutionSnapshot? Formation = null);

/// <summary>
/// Active-floor save admission. Encoding is the run codec's job; this class
/// owns validation, returning one reconstruction consumed once by the floor
/// aggregate.
/// </summary>
internal sealed record AdmittedFloor(ExplorationState Exploration, PartyState Party, PatrolActor Actor,
    ItemInventory Inventory, ExplorationItems ItemWorld, RestoredCombat Combat);

internal static class ExpeditionCodec
{

    internal static AdmittedFloor Validate(ExpeditionSnapshot saved, GameDefinitions definitions, IReadOnlyDictionary<ulong, string>? expeditionItems = null,
        PartyState? travellingParty = null, IReadOnlyDictionary<string, MagicState.MagicBook>? travellingBooks = null)
    {
        GameDefinitions.Require(saved.Id != Guid.Empty && saved.FloorId > 0 && saved.PartyId > 0
            && saved.PartyId != saved.FloorId && saved.NextObjectId > Math.Max(saved.FloorId, saved.PartyId) && saved.NextObjectId <= (ulong)uint.MaxValue + 1, "Save identities");
        FloorFacts.Validate(saved.Floor, saved.GeneratedFeatures, saved.EncounterPlacement,
            saved.Combat.Enemies, saved.Intent, definitions);
        _ = definitions.Characters.GetPreset(saved.Preset);
        ExplorationItems itemWorld = new(definitions.ItemExploration, saved.ItemWorld);
        itemWorld.Validate(saved.Floor);
        ItemInventory inventory = ItemInventory.Restore(definitions.Items, saved.Inventory);
        FloorFacts.ValidateAnchors(saved.GeneratedFeatures, saved.Combat.Drops, inventory, definitions, expeditionItems);
        string[] expectedOwners = saved.Roster.Select(m => "member:" + m.Id).Append(Rifles.Game.Items.ItemInventory.PartyKey).Concat(definitions.ItemExploration.Anchors.Select(a => a.Key)).ToArray();
        GameDefinitions.Require(inventory.Owners.Where(o => InventoryOwner.Parse(o.Key) is not CombatOwner).Select(o => o.Key).ToHashSet().SetEquals(expectedOwners), "saved inventory owners");
        FloorFacts.ValidatePackCapacities(inventory, itemWorld, definitions);
        ValidateWorldObstructions(saved.Floor, saved.Actor, saved.Features.Dressing, saved.ItemWorld, saved.GeneratedFeatures);
        // A travelling party moves untouched: its vitals, entities, books,
        // and markers are live-continuous. Only fresh boots rebuild.
        PartyState party = travellingParty ?? PartyState.FromSnapshot(definitions.Party.Positions, definitions.Party.MaxPartySize, saved.Roster, saved.Members);
        GameDefinitions.Require(party.Members.Count == definitions.Party.MaxPartySize && party.Commander is not null, "saved commander and squad roster");
        inventory.BindMembers(party.Entities, party.Members);
        foreach (RiflesCharacter member in party.Members)
        {
            GameDefinitions.Require(inventory.Items("member:" + member.Definition.Id).Where(i => i.Slots.Length > 0)
                .All(i => member.Definition.BasePower >= definitions.Items.Item(i.Definition).MinimumPower), "saved equipment requirements");
        }
        party.Formation.Restore(saved.Formation, definitions.Formation.RepositionSeconds);
        GameDefinitions.Require(saved.Formation is null || saved.Exploration.Action is null && saved.RestRemaining == 0, "formation locks party movement");
        ExplorationState exploration = ExplorationState.Restore(saved.Exploration, saved.Floor, definitions.Exploration);
        GameDefinitions.Require(party.Members.Any(m => m.Definition.Id == saved.SelectedMember), "Save.SelectedMember");
        GameDefinitions.Require(double.IsFinite(saved.RestRemaining) && saved.RestRemaining >= 0
            && saved.RestRemaining <= definitions.Magic.RestSeconds, "Save.RestRemaining");
        GameDefinitions.Require(saved.RestRemaining > 0
            ? party.Members.Any(m => m.Definition.Id == saved.RestOwner && m.IsLiving) : saved.RestOwner.Length == 0, "Save.RestOwner");
        party.RestoreRest(saved.RestRemaining, saved.RestOwner);
        RestoredCombat combat = CombatRestore.Validate(saved.Combat, definitions, saved.Floor, inventory, party, saved.PartyId,
            new[] { saved.Actor.Id, saved.Features.Dressing.ObserverId }, travellingBooks);
        ChargeState.ValidateSaved(saved.Combat.Charge, saved.Exploration, definitions, saved.Floor, party, inventory);
        GameDefinitions.Require(saved.Combat.Charge is null || saved.Formation is null && saved.RestRemaining == 0, "charge cannot overlap formation or rest");
        inventory.BindRemaining(party.Entities);
        ulong[] ids = [saved.FloorId, saved.PartyId, saved.Actor.Id, saved.Features.LanternId, saved.Features.ExitId,
            saved.Features.Dressing.BenchId, saved.Features.Dressing.CrateId, saved.Features.Dressing.ObserverId, saved.ItemWorld.DoorId, saved.ItemWorld.LeverId, saved.ItemWorld.PlateId,
            .. saved.GeneratedFeatures.Plates.Select(p => p.Id), .. saved.GeneratedFeatures.Gates.Select(g => g.Id), .. saved.GeneratedFeatures.Hazards.Select(h => h.Id),
            .. combat.Enemies.Select(e => e.Id), .. combat.Flights.Select(f => f.Id),
            .. inventory.Owners.Select(o => o.Id), .. saved.Inventory.Packs.SelectMany(p => p.Items).Select(i => i.Id)];
        GameDefinitions.Require(ids.All(id => id > 0 && id <= uint.MaxValue && id < saved.NextObjectId) && ids.Distinct().Count() == ids.Length, "Save object identities");
        GameDefinitions.Require(saved.Features.LanternRevision > 0 && saved.Features.LanternRevision <= uint.MaxValue, "Save lantern revision");
        PatrolActor actor = PatrolActor.Restore(saved.Actor, saved.Floor, definitions.Exploration with { StepSeconds = definitions.Features.ActorStepSeconds });
        // Validate occupancy and both in-flight reservations before realizing any replacement scene.
        MovementGrid grid = new(saved.Floor.Cells.ToHashSet(), (_, _) => true, definitions.Crowd);
        FloorFactory.ConfigureDoorClearance(grid, saved.Floor, saved.ItemWorld.Door, definitions.Combat.DoorClearance);
        if (!party.Defeated) exploration.Bind(grid, saved.PartyId);
        if (combat.Allies.Single(a => a.Id == actor.Id).Vitality > 0) actor.Bind(grid);
        saved.Features.Dressing.Validate(saved.Floor);
        saved.Features.Dressing.Bind(grid, combat.Allies.Single(a => a.Id == saved.Features.Dressing.ObserverId).Vitality > 0);
        foreach (var ally in combat.Allies.Where(a => a.Vitality == 0)) grid.Remove(ally.Id);
        foreach (EnemyState enemy in combat.Enemies.Where(e => e.Alive)) enemy.Motion.Bind(grid, enemy.Id, enemy.Definition.Footprint, enemy.Definition.Faction, enemy.Definition.Share);
        GameDefinitions.Require(saved.ItemWorld.DoorOpen || !grid.Occupied(saved.ItemWorld.Door), "closed gate occupancy");
        GameDefinitions.Require(saved.GeneratedFeatures.Gates.All(g => g.Open || !grid.Occupied(g.Cell)), "closed generated gate occupancy");
        return new(exploration, party, actor, inventory, itemWorld, combat);
    }

    internal static void ValidateWorldObstructions(DungeonFloor floor, PatrolSnapshot actor, RoomDressing dressing,
        ItemExplorationSnapshot itemWorld, GeneratedFeatureSnapshot generated)
    {
        GridPoint[] staticObstacles = [actor.Start, actor.End, dressing.Bench,
            dressing.Crate, dressing.Observer];
        GameDefinitions.Require(staticObstacles.Distinct().Count() == staticObstacles.Length
            && DressingPlacement.CanBlock(floor, staticObstacles), "saved static obstacle placement");

        GridPoint door = itemWorld.Door;
        GameDefinitions.Require(door != floor.Entrance && door != floor.Exit
            && !staticObstacles.Contains(door)
            && !generated.Gates.Any(gate => gate.Cell == door)
            && !floor.Grants.Any(grant => grant.Cell == door)
            && !floor.Connectors.Any(connector => connector.From == door || connector.To == door),
            "saved item gate placement");
    }
}
