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
    private ItemArt? itemArt;
    private RiflesCharacter Member(string id) => party.Members.SingleOrDefault(m => m.Definition.Id == id)
        ?? throw new InvalidDataException("Character unavailable.");

    private GameOutcome ItemCommand(SessionCommand command)
    {
        if (command.Action == "consume") return active.Combat.BeginCombat(command, selectedMember, paused);
        string source = command.Source ?? "member:" + selectedMember;
        // Fresh ledger revision: the UI no longer passes one. Optimistic
        // atomicity still applies inside each mutation via this revision.
        ulong revision = active.Inventory.Revision;
        try
        {
            active.Combat.RequireItemAccess(source);
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
        if (outcome.Accepted) audio!.Play(SoundCue.Interaction, Aim(active.Exploration.Position));
        return outcome;
    }

    private GameOutcome TransferItem(ItemRef subject, string? destination, ulong quantity, ulong revision)
    {
        if (destination is null) return GameOutcome.Reject("Choose a destination.");
        try
        {
            active.Combat.RequireItemAccess(destination);
        }
        catch (InvalidDataException error) { return GameOutcome.Reject(error.Message); }
        active.Inventory.Transfer(subject, InventoryOwner.Parse(destination), quantity, revision);
        return GameOutcome.Accept("Item transferred");
    }

    private GameOutcome EquipItem(ItemRef subject, string gearOwner, string slot, ulong revision)
    {
        if (InventoryOwner.Parse(gearOwner) is not MemberOwner wearer || !Member(wearer.Instance).IsLiving)
            return GameOutcome.Reject("Choose a living character to equip.");
        active.Inventory.Equip(subject, slot, Member(wearer.Instance).Power, revision, wearer);
        return GameOutcome.Accept("Equipment changed");
    }

    private GameOutcome UnequipItem(ItemRef subject, ulong revision)
    {
        active.Inventory.Unequip(subject, revision);
        return GameOutcome.Accept("Item returned to pack");
    }

    private GameOutcome ArrangeItem(string token, int? slot, ulong revision)
    {
        if (slot is null) return GameOutcome.Reject("Choose a grid slot.");
        active.Inventory.Arrange(token, slot.Value, revision);
        return GameOutcome.Accept("Item rearranged");
    }

    private GameOutcome UseItemOnFeature(string source, string token, ulong? target, ulong targetRevision)
    {
        if (paused) return GameOutcome.Reject("Resume before using world features.");
        GearDefinition key = definitions.Items.Item(active.Inventory.Find(source, token).Definition);
        if (key.Use != ItemUse.Key || target != active.ItemWorld.DoorId)
            return GameOutcome.Reject("This item does not fit that feature.");
        // Reusable keys intentionally have no cost: the puzzle is recoverable after any plate/lever change.
        if (key.Cost != 0) return GameOutcome.Reject("This gate requires a reusable key.");
        active.ItemWorld.Unlock(active.Exploration, active.Scene, targetRevision);
        return GameOutcome.Accept("Gate unlocked; the key is retained. Set the lever and weight the plate.");
    }
    private IEnumerable<InteractionCandidate> ItemCandidates()
    {
        foreach (var route in Connections().Where(c => !c.Forward))
            yield return new InteractionCandidate(new(active.FloorId, 1), "Return stair — " + expedition.Floors.Single(f => f.Id == route.Destination).Title,
                active.Scene.Eye(active.Floor.Entrance), definitions.Features.Reach,
                active.Scene.Visibility(active.Scene.Eye(active.Exploration.Position), active.Scene.Eye(active.Floor.Entrance)),
                active.Exploration.Moving ? InteractionAvailability.Unavailable : InteractionAvailability.Available);
        foreach (var generated in GeneratedCandidates()) yield return generated;
        foreach (WorldAnchor anchor in active.ItemWorld.Anchors)
        {
            bool container = anchor.Key == "crate";
            string label = active.ItemWorld.AnchorDefinition(anchor.Key).Name;
            var contents = active.Inventory.Items(anchor.Key);
            if (container) label += active.ItemWorld.OpenContainer == anchor.Key ? " — close" : " — open";
            else label += contents.Count == 0 ? " — empty" : " — " + definitions.Items.Item(contents[0].Definition).Name;
            yield return Candidate(anchor.Id, label, active.ItemWorld.Point(anchor.Key, active.Scene));
        }
        ItemExplorationSnapshot state = active.ItemWorld.Capture();
        yield return Candidate(state.DoorId, state.Unlocked ? "Gate — unlocked" : "Gate — use brass key", active.ItemWorld.LeverPoint(active.Scene));
        yield return Candidate(state.LeverId, "Gate lever — " + (state.LeverOn ? "switch off" : "switch on"), active.ItemWorld.LeverPoint(active.Scene));
    }
    private InteractionCandidate Candidate(ulong id, string label, System.Numerics.Vector3 point) =>
        new(new InteractionTarget(id, active.ItemWorld.Revision), label, point, definitions.ItemExploration.Reach,
            active.Scene.Visibility(active.Scene.Eye(active.Exploration.Position), point), active.Exploration.Moving ? InteractionAvailability.Unavailable : InteractionAvailability.Available);
    private string UseItemFeature(InteractionTarget target)
    {
        if (active.GeneratedFeatures.Gates.Any(g => g.Id == target.Id) || active.GeneratedFeatures.Hazards.Any(h => h.Id == target.Id) || active.GeneratedFeatures.Plates.Any(p => p.Id == target.Id)) return UseGeneratedFeature(target);
        ItemExplorationSnapshot state = active.ItemWorld.Capture();
        if (target.Id == state.LeverId) { active.ItemWorld.ToggleLever(active.Exploration, active.Scene, target.Revision); audio!.Play(SoundCue.Interaction, Aim(active.Exploration.Position)); return "Lever " + (active.ItemWorld.LeverOn ? "on" : "off"); }
        if (target.Id == state.DoorId) return state.Unlocked ? "Gate needs the lever and sufficient plate weight." : "Select the brass key and use it on the gate.";
        WorldAnchor anchor = active.ItemWorld.Anchors.Single(a => a.Id == target.Id);
        if (anchor.Key == "crate")
        {
            if (active.ItemWorld.OpenContainer == "crate") { active.ItemWorld.Close(); return "Crate closed"; }
            active.ItemWorld.Open("crate", active.Exploration, active.Scene); return "Crate opened";
        }
        active.ItemWorld.RequireAccess(anchor.Key, active.Exploration, active.Scene);
        CarriedItem? item = active.Inventory.Items(anchor.Key).SingleOrDefault();
        if (item is null) return "Choose a pack item and place it here from Inventory.";
        active.Inventory.Transfer(ItemRef.Parse(anchor.Key, item.Token), new PartyOwner(), item.Quantity, active.Inventory.Revision);
        return "Picked up " + definitions.Items.Item(item.Definition).Name;
    }
}
