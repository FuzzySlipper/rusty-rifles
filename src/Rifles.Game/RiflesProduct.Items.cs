using Rifles.Game.Items;
using Rifles.Game.Party;
using Rifles.Game.Presentation;
using Rusty.Engine.Interaction;

namespace Rifles.Game;

public sealed partial class RiflesProduct
{
    private string preset = "";
    private ItemInventory? inventory;
    private ExplorationItems? itemWorld;
    private ItemArt? itemArt;
    private PartyMemberState Member(string id) => party.Members.SingleOrDefault(m => m.Definition.Id == id)
        ?? throw new InvalidDataException("Character unavailable.");

    private void ApplyEquipment()
    {
        foreach (PartyMemberState member in party.Members)
        {
            var bonus = inventory!.Bonuses("member:" + member.Definition.Id);
            member.SetEquipmentBonuses(bonus.Power, bonus.Defense);
        }
    }
    private void ItemCommand(SessionCommand command)
    {
        string source = command.Source ?? "member:" + selectedMember;
        ulong revision = ulong.TryParse(command.InventoryRevision, out ulong parsed) ? parsed : throw new InvalidDataException("Inventory proposal expired.");
        if (revision != inventory!.Revision) throw new InvalidDataException("Inventory changed; select the item again.");
        RequireItemAccess(source);
        string token = command.Item ?? throw new InvalidDataException("Select an item first.");
        switch (command.Action)
        {
            case "transfer":
                string destination = command.Destination ?? throw new InvalidDataException("Choose a destination.");
                RequireItemAccess(destination);
                inventory.Transfer(source, destination, token, command.Quantity ?? 1, revision);
                feedback = "Item transferred";
                break;
            case "equip":
                string gearOwner = command.Destination ?? "member:" + selectedMember;
                if (!ItemInventory.IsMember(gearOwner) || !Member(gearOwner["member:".Length..]).IsLiving)
                    throw new InvalidDataException("Choose a living character to equip.");
                inventory.Equip(source, token, command.Slot ?? "", Member(gearOwner["member:".Length..]).Definition.BasePower, revision, gearOwner);
                feedback = "Equipment changed";
                break;
            case "unequip": inventory.Unequip(source, token, revision); feedback = "Item returned to pack"; break;
            case "consume":
                BeginCombat(command);
                break;
            case "item-feature":
                if (paused) throw new InvalidDataException("Resume before using world features.");
                GearDefinition key = definitions.Items.Item(inventory.Find(source, token).Definition);
                if (key.Use != ItemUse.Key || command.Target != itemWorld!.Capture().DoorId)
                    throw new InvalidDataException("This item does not fit that feature.");
                // Reusable keys intentionally have no cost: the puzzle is recoverable after any plate/lever change.
                if (key.Cost != 0) throw new InvalidDataException("This gate requires a reusable key.");
                itemWorld!.Unlock(exploration, scene!, command.TargetRevision ?? 0);
                feedback = "Gate unlocked; the key is retained. Set the lever and weight the plate.";
                break;
        }
        ApplyEquipment();
    }
    private IEnumerable<InteractionCandidate> ItemCandidates()
    {
        foreach (var route in Connections().Where(c => !c.Forward))
            yield return new InteractionCandidate(new(floorId, 1), "Return stair — " + expedition.Floors.Single(f => f.Id == route.Destination).Title,
                scene!.Eye(floor.Entrance), definitions.Features.Reach,
                scene.Visibility(scene.Eye(exploration.Position), scene.Eye(floor.Entrance)),
                exploration.Moving ? InteractionAvailability.Unavailable : InteractionAvailability.Available);
        foreach (var generated in GeneratedCandidates()) yield return generated;
        foreach (WorldAnchor anchor in itemWorld!.Anchors)
        {
            bool container = anchor.Key == "crate";
            string label = itemWorld.AnchorDefinition(anchor.Key).Name;
            var contents = inventory!.Items(anchor.Key);
            if (container) label += itemWorld.OpenContainer == anchor.Key ? " — close" : " — open";
            else label += contents.Count == 0 ? " — empty" : " — " + definitions.Items.Item(contents[0].Definition).Name;
            yield return Candidate(anchor.Id, label, itemWorld.Point(anchor.Key, scene!));
        }
        ItemExplorationSnapshot state = itemWorld!.Capture();
        yield return Candidate(state.DoorId, state.Unlocked ? "Gate — unlocked" : "Gate — use brass key", itemWorld.LeverPoint(scene!));
        yield return Candidate(state.LeverId, "Gate lever — " + (state.LeverOn ? "switch off" : "switch on"), itemWorld.LeverPoint(scene!));
    }
    private InteractionCandidate Candidate(ulong id, string label, System.Numerics.Vector3 point) =>
        new(new InteractionTarget(id, itemWorld!.Revision), label, point, definitions.ItemExploration.Reach,
            scene!.Visibility(scene.Eye(exploration.Position), point), exploration.Moving ? InteractionAvailability.Unavailable : InteractionAvailability.Available);
    private string UseItemFeature(InteractionTarget target)
    {
        if (generatedFeatures.Gates.Any(g => g.Id == target.Id) || generatedFeatures.Hazards.Any(h => h.Id == target.Id) || generatedFeatures.Plates.Any(p => p.Id == target.Id)) return UseGeneratedFeature(target);
        ItemExplorationSnapshot state = itemWorld!.Capture();
        if (target.Id == state.LeverId) { itemWorld.ToggleLever(exploration, scene!, target.Revision); return "Lever " + (itemWorld.LeverOn ? "on" : "off"); }
        if (target.Id == state.DoorId) return state.Unlocked ? "Gate needs the lever and sufficient plate weight." : "Select the brass key and use it on the gate.";
        WorldAnchor anchor = itemWorld.Anchors.Single(a => a.Id == target.Id);
        if (anchor.Key == "crate")
        {
            if (itemWorld.OpenContainer == "crate") { itemWorld.Close(); return "Crate closed"; }
            itemWorld.Open("crate", exploration, scene!); return "Crate opened";
        }
        itemWorld.RequireAccess(anchor.Key, exploration, scene!);
        CarriedItem? item = inventory!.Items(anchor.Key).SingleOrDefault();
        if (item is null) return "Choose a pack item and place it here from Inventory.";
        inventory.Transfer(anchor.Key, "member:" + selectedMember, item.Token, item.Quantity, inventory.Revision);
        return "Picked up " + definitions.Items.Item(item.Definition).Name;
    }
}
