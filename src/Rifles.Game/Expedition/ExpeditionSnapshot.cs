using Rifles.Game.Generation;
using Rifles.Procgen;
using Rifles.Procgen.Expeditions;
using Rifles.Procgen.Generation;
using System.Buffers;
using Rifles.Game.Combat;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine.Persistence;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Party;
using Rifles.Game.Items;

namespace Rifles.Game.Expedition;

internal sealed record ExpeditionSnapshot(Guid Id, ulong FloorId, ulong PartyId, ulong NextObjectId,
    DungeonFloor Floor, ExplorationSnapshot Exploration, MemberDefinition[] Roster,
    MemberSnapshot[] Members, bool Paused, string SelectedMember, PatrolSnapshot Actor, FeatureSnapshot Features, string Preset, InventorySnapshot Inventory, ItemExplorationSnapshot ItemWorld, CombatSnapshot Combat, ResolvedExpedition Intent, GeneratedFeatureSnapshot GeneratedFeatures, EncounterPlacementResult EncounterPlacement);

internal sealed class ExpeditionCodec : IProductStateCodec<ExpeditionSnapshot>
{
    public uint SchemaVersion => 9;
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

    internal static (ExplorationState Exploration, PartyState Party, PatrolActor Actor) Validate(ExpeditionSnapshot saved, GameDefinitions definitions, IEnumerable<string>? expeditionRewards = null, IReadOnlyDictionary<ulong, string>? expeditionItems = null, long completionExperience = 0)
    {
        GameDefinitions.Require(saved.Id != Guid.Empty && saved.FloorId > 0 && saved.PartyId > 0
            && saved.PartyId != saved.FloorId && saved.NextObjectId > Math.Max(saved.FloorId, saved.PartyId) && saved.NextObjectId <= (ulong)uint.MaxValue + 1, "Save identities");
        saved.Floor.Validate();
        GameDefinitions.Require(saved.Floor.Architecture is not null, "saved architecture artifact");
        saved.EncounterPlacement.Validate(saved.Floor, definitions.Combat, definitions.Crowd);
        GameDefinitions.Require(saved.EncounterPlacement.Accepted && saved.Combat.Enemies.Select(e => e.Spawn).ToHashSet()
            .SetEquals(saved.EncounterPlacement.Instances.Select(e => e.SpawnId)), "saved accepted encounter roster");
        GeneratedFeatures.Validate(saved.GeneratedFeatures, saved.Floor);
        GameDefinitions.Require(saved.GeneratedFeatures.Hazards.All(h => h.Phase < definitions.Hazards.PeriodSeconds), "saved hazard phase");
        Diagnostic[] intentDiagnostics = ExpeditionGenerator.Validate(saved.Intent);
        GameDefinitions.Require(!intentDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Fatal),
            "Save expedition intent: " + string.Join(", ", intentDiagnostics.Select(d => d.Code)));
        ResolvedFloorIntent? floorIntent = saved.Intent.Floors.SingleOrDefault(f => f.Id == saved.Floor.IntentFloorId);
        GameDefinitions.Require(floorIntent is not null && saved.Floor.Seed == floorIntent.Candidate.Seed
            && saved.Floor.IntentGraphIdentity == CanonicalIdentity.Hash(floorIntent.Candidate), "Save floor intent identity");
        GameDefinitions.Require(saved.Floor.Rooms.Select(r => r.NodeId).ToHashSet(StringComparer.Ordinal)
            .SetEquals(floorIntent!.Candidate.Graph.Nodes.Select(n => n.Id)), "Save room graph coverage");
        var hazardNodes = floorIntent.Candidate.Graph.Nodes.Where(n => n.Kind == NodeKind.Hazard).Select(n => n.Id).ToHashSet();
        GameDefinitions.Require(saved.GeneratedFeatures.Hazards.Length == hazardNodes.Count
            && saved.GeneratedFeatures.Hazards.Select(h => h.NodeId).ToHashSet().SetEquals(hazardNodes)
            && saved.GeneratedFeatures.Hazards.All(h => saved.Floor.Rooms.Any(r => r.NodeId == h.NodeId && r.Cells.Contains(h.Cell))),
            "saved hazard graph coverage");
        _ = definitions.Characters.GetPreset(saved.Preset);
        ExplorationItems itemWorld = new(definitions.ItemExploration, saved.ItemWorld);
        itemWorld.Validate(saved.Floor);
        ItemInventory inventory = ItemInventory.Restore(definitions.Items, saved.Inventory);
        foreach (var plate in saved.GeneratedFeatures.Plates)
            GameDefinitions.Require(saved.Combat.Drops.Any(d => d.Owner == plate.Owner && d.Cell == plate.Cell)
                && inventory.Owner(plate.Owner).Label == "Counterweight plate", "saved counterweight anchor");
        foreach (var key in saved.GeneratedFeatures.Keys)
        {
            GameDefinitions.Require(saved.Combat.Drops.Any(d => d.Owner == key.Owner && d.Cell == key.Cell), "saved key source anchor");
            var retained = inventory.Owners.SelectMany(o => inventory.Items(o.Key)).Where(i => i.Entity == key.Entity).ToArray();
            GameDefinitions.Require(expeditionItems is not null
                ? expeditionItems.GetValueOrDefault(key.Entity) == definitions.GeneratedFeatures.KeyItem
                : retained.Length == 1 && retained[0].Definition == definitions.GeneratedFeatures.KeyItem,
                "saved generated key identity");
        }
        string[] expectedOwners = saved.Roster.Select(m => "member:" + m.Id).Concat(definitions.ItemExploration.Anchors.Select(a => a.Key)).ToArray();
        GameDefinitions.Require(inventory.Owners.Where(o => !o.Key.StartsWith("combat:", StringComparison.Ordinal)).Select(o => o.Key).ToHashSet().SetEquals(expectedOwners), "saved inventory owners");
        foreach (PackOwner owner in inventory.Owners)
        {
            bool combatOwner = owner.Key.StartsWith("combat:", StringComparison.Ordinal);
            PackDefinition capacity = combatOwner ? definitions.Combat.DropCapacity : ItemInventory.IsMember(owner.Key) ? definitions.Items.Backpack : owner.Key == "crate" ? definitions.Items.Container : definitions.Items.Anchor;
            GameDefinitions.Require(owner.MassCapacity == capacity.Mass && owner.SpaceCapacity == capacity.Space, "saved pack capacity");
            if (!combatOwner && !ItemInventory.IsMember(owner.Key)) GameDefinitions.Require(owner.Id == itemWorld.Anchor(owner.Key).Id, "saved anchor owner");
        }
        new PartyDefinition(saved.Roster).Validate();
        PartyState party = new(saved.Roster);
        party.Restore(saved.Members);
        foreach (PartyMemberState member in party.Members)
        {
            var bonus = inventory.Bonuses("member:" + member.Definition.Id);
            member.SetEquipmentBonuses(bonus.Power, bonus.Defense);
            GameDefinitions.Require(inventory.Items("member:" + member.Definition.Id).Where(i => i.Slots.Length > 0)
                .All(i => member.Definition.BasePower >= definitions.Items.Item(i.Definition).MinimumPower), "saved equipment requirements");
        }
        ExplorationState exploration = ExplorationState.Restore(saved.Exploration, saved.Floor, definitions.Exploration);
        GameDefinitions.Require(party.Members.Any(m => m.Definition.Id == saved.SelectedMember), "Save.SelectedMember");
        RestoredCombat combat = CombatRestore.Validate(saved.Combat, definitions, saved.Floor, inventory, party, saved.PartyId,
            new[] { saved.Actor.Id, saved.Features.Dressing.ObserverId }, expeditionRewards, completionExperience);
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
        foreach (var connector in saved.Floor.Connectors) grid.SetClearance(connector.From, connector.To, connector.Clearance);
        foreach (var direction in Rifles.Procgen.Generation.CardinalDirections.Ordered)
            if (saved.Floor.Cells.Contains(saved.ItemWorld.Door + direction.Offset()))
                grid.SetClearance(saved.ItemWorld.Door, saved.ItemWorld.Door + direction.Offset(), definitions.Combat.DoorClearance);
        if (party.Members.Any(m => m.IsLiving)) exploration.Bind(grid, saved.PartyId);
        if (combat.Allies.Single(a => a.Id == actor.Id).Vitality > 0) actor.Bind(grid);
        saved.Features.Dressing.Validate(saved.Floor);
        saved.Features.Dressing.Bind(grid, combat.Allies.Single(a => a.Id == saved.Features.Dressing.ObserverId).Vitality > 0);
        foreach (var ally in combat.Allies.Where(a => a.Vitality == 0)) grid.Remove(ally.Id);
        foreach (EnemyState enemy in combat.Enemies.Where(e => e.Alive)) enemy.Motion.Bind(grid, enemy.Id, enemy.Definition.Footprint, enemy.Definition.Faction, enemy.Definition.Share);
        GameDefinitions.Require(saved.ItemWorld.DoorOpen || !grid.Occupied(saved.ItemWorld.Door), "closed gate occupancy");
        GameDefinitions.Require(saved.GeneratedFeatures.Gates.All(g => g.Open || !grid.Occupied(g.Cell)), "closed generated gate occupancy");
        return (exploration, party, actor);
    }
}
