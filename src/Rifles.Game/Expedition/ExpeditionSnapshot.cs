using System.Buffers;
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
    MemberSnapshot[] Members, bool Paused, string SelectedMember, PatrolSnapshot Actor, FeatureSnapshot Features, string Preset, InventorySnapshot Inventory, ItemExplorationSnapshot ItemWorld);

internal sealed class ExpeditionCodec : IProductStateCodec<ExpeditionSnapshot>
{
    public uint SchemaVersion => 3;
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
            && saved.PartyId != saved.FloorId && saved.NextObjectId > Math.Max(saved.FloorId, saved.PartyId) && saved.NextObjectId <= (ulong)uint.MaxValue + 1, "Save identities");
        saved.Floor.Validate();
        _ = definitions.Characters.GetPreset(saved.Preset);
        ExplorationItems itemWorld = new(definitions.ItemExploration, saved.ItemWorld);
        itemWorld.Validate(saved.Floor);
        ItemInventory inventory = ItemInventory.Restore(definitions.Items, saved.Inventory);
        string[] expectedOwners = saved.Roster.Select(m => "member:" + m.Id).Concat(definitions.ItemExploration.Anchors.Select(a => a.Key)).ToArray();
        GameDefinitions.Require(inventory.Owners.Select(o => o.Key).ToHashSet().SetEquals(expectedOwners), "saved inventory owners");
        foreach (PackOwner owner in inventory.Owners)
        {
            PackDefinition capacity = ItemInventory.IsMember(owner.Key) ? definitions.Items.Backpack : owner.Key == "crate" ? definitions.Items.Container : definitions.Items.Anchor;
            GameDefinitions.Require(owner.MassCapacity == capacity.Mass && owner.SpaceCapacity == capacity.Space, "saved pack capacity");
            if (!ItemInventory.IsMember(owner.Key)) GameDefinitions.Require(owner.Id == itemWorld.Anchor(owner.Key).Id, "saved anchor owner");
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
        ulong[] ids = [saved.FloorId, saved.PartyId, saved.Actor.Id, saved.Features.LanternId, saved.Features.ExitId,
            saved.Features.Dressing.BenchId, saved.Features.Dressing.CrateId, saved.Features.Dressing.ObserverId, saved.ItemWorld.DoorId, saved.ItemWorld.LeverId, saved.ItemWorld.PlateId,
            .. inventory.Owners.Select(o => o.Id), .. saved.Inventory.Packs.SelectMany(p => p.Items).Select(i => i.Id)];
        GameDefinitions.Require(ids.All(id => id > 0 && id <= uint.MaxValue && id < saved.NextObjectId) && ids.Distinct().Count() == ids.Length, "Save object identities");
        GameDefinitions.Require(saved.Features.LanternRevision > 0 && saved.Features.LanternRevision <= uint.MaxValue, "Save lantern revision");
        PatrolActor actor = PatrolActor.Restore(saved.Actor, saved.Floor, definitions.Exploration with { StepSeconds = definitions.Features.ActorStepSeconds });
        // Validate occupancy and both in-flight reservations before realizing any replacement scene.
        MovementGrid grid = new(saved.Floor.Cells.ToHashSet(), (_, _) => true);
        exploration.Bind(grid, saved.PartyId);
        actor.Bind(grid);
        saved.Features.Dressing.Validate(saved.Floor);
        saved.Features.Dressing.Bind(grid);
        GameDefinitions.Require(saved.ItemWorld.DoorOpen || !grid.Occupied(saved.ItemWorld.Door), "closed gate occupancy");
        return (exploration, party, actor);
    }
}
