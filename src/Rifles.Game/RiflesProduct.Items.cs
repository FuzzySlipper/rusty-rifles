using Rifles.Game.Audio;
using Rifles.Game.Items;
using Rifles.Game.Characters;
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
    private RiflesCharacter Member(string id) => party.Members.SingleOrDefault(m => m.Definition.Id == id)
        ?? throw new InvalidDataException("Character unavailable.");

    private GameOutcome ItemCommand(SessionCommand command)
    {
        if (command.Action == "consume") return combat.BeginCombat(command, selectedMember, paused);
        string source = command.Source ?? "member:" + selectedMember;
        // Fresh ledger revision: the UI no longer passes one. Optimistic
        // atomicity still applies inside each mutation via this revision.
        ulong revision = inventory!.Revision;
        try
        {
            combat.RequireItemAccess(source);
        }
        catch (InvalidDataException error) { return GameOutcome.Reject(error.Message); }
        string token = command.Item ?? "";
        if (token.Length == 0) return GameOutcome.Reject("Select an item first.");
        ItemRef subject = ItemRef.Parse(source, token);
        GameOutcome outcome = command.Action switch
        {
            "transfer" => TransferItem(subject, command.Destination, command.Quantity ?? 1, revision),
            "equip" => EquipItem(subject, command.Destination ?? "member:" + selectedMember, command.Slot ?? "", revision),
            "unequip" => UnequipItem(subject, revision),
            "arrange" => ArrangeItem(token, command.PartySlot, revision),
            "item-feature" => UseItemOnFeature(source, token, command.Target, command.TargetRevision ?? 0),
            _ => GameOutcome.Reject("Unknown command"),
        };
        if (outcome.Accepted) audio!.Play(SoundCue.Interaction, Aim(exploration.Position));
        return outcome;
    }

    private GameOutcome TransferItem(ItemRef subject, string? destination, ulong quantity, ulong revision)
    {
        if (destination is null) return GameOutcome.Reject("Choose a destination.");
        try
        {
            combat.RequireItemAccess(destination);
        }
        catch (InvalidDataException error) { return GameOutcome.Reject(error.Message); }
        inventory!.Transfer(subject, InventoryOwner.Parse(destination), quantity, revision);
        return GameOutcome.Accept("Item transferred");
    }

    private GameOutcome EquipItem(ItemRef subject, string gearOwner, string slot, ulong revision)
    {
        if (InventoryOwner.Parse(gearOwner) is not MemberOwner wearer || !Member(wearer.Instance).IsLiving)
            return GameOutcome.Reject("Choose a living character to equip.");
        inventory!.Equip(subject, slot, Member(wearer.Instance).Power, revision, wearer);
        return GameOutcome.Accept("Equipment changed");
    }

    private GameOutcome UnequipItem(ItemRef subject, ulong revision)
    {
        inventory!.Unequip(subject, revision);
        return GameOutcome.Accept("Item returned to pack");
    }

    private GameOutcome ArrangeItem(string token, int? slot, ulong revision)
    {
        if (slot is null) return GameOutcome.Reject("Choose a grid slot.");
        inventory!.Arrange(token, slot.Value, revision);
        return GameOutcome.Accept("Item rearranged");
    }

    private GameOutcome UseItemOnFeature(string source, string token, ulong? target, ulong targetRevision)
    {
        if (paused) return GameOutcome.Reject("Resume before using world features.");
        GearDefinition key = definitions.Items.Item(inventory!.Find(source, token).Definition);
        if (key.Use != ItemUse.Key || target != itemWorld!.Capture().DoorId)
            return GameOutcome.Reject("This item does not fit that feature.");
        // Reusable keys intentionally have no cost: the puzzle is recoverable after any plate/lever change.
        if (key.Cost != 0) return GameOutcome.Reject("This gate requires a reusable key.");
        itemWorld!.Unlock(exploration, scene!, targetRevision);
        return GameOutcome.Accept("Gate unlocked; the key is retained. Set the lever and weight the plate.");
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
        if (target.Id == state.LeverId) { itemWorld.ToggleLever(exploration, scene!, target.Revision); audio!.Play(SoundCue.Interaction, Aim(exploration.Position)); return "Lever " + (itemWorld.LeverOn ? "on" : "off"); }
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
        inventory.Transfer(ItemRef.Parse(anchor.Key, item.Token), new PartyOwner(), item.Quantity, inventory.Revision);
        return "Picked up " + definitions.Items.Item(item.Definition).Name;
    }
}
