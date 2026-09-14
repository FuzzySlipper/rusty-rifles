using Rusty.Engine;
using Rusty.Engine.Interaction;
using Rifles.Game.Dungeon;
using Rifles.Game.Party;
using Rifles.Game.Items;

namespace Rifles.Game.Presentation;

internal sealed class SessionProjection : IDisposable
{
    private readonly IUiService ui;
    private readonly UiStream stream;
    private ulong sequence;
    internal SessionProjection(IUiService ui)
    {
        this.ui = ui;
        stream = ui.OpenStream(new UiStreamRequest("rifles.session", "rifles.session.v1"));
    }

    internal void Publish(DungeonFloor floor, ExplorationState exploration, PartyState party, bool paused, string feedback, string selectedMember, ulong commandRevision, InteractionReadout? focus, string artStyle, bool roomLights, int lightPosition, ItemInventory inventory, ExplorationItems world, DungeonScene scene, CharacterOptionsDefinition characters, string preset, PatrolActor actor, ItemArtDefinition art, Func<SessionValueBuilder, uint> combat, Func<string, bool> dropReachable, Func<SessionValueBuilder, uint> run)
    {
        SessionValueBuilder value = new();
        uint roster = value.Object(party.Members.Select(member => (member.Definition.Id, value.Object(
            ("name", value.String(member.Definition.Name)),
            ("slot", value.String(member.Slot.ToString())),
            ("vitality", value.Number(member.Vitality)),
            ("maximumVitality", value.Number(member.MaximumVitality)),
            ("power", value.Number(member.Power)), ("defense", value.Number(member.Defense)),
            ("resource", value.Number(member.Resource)), ("maxResource", value.Number(member.MaximumResource)),
            ("melee", value.Number(party.CanUseReach(member.Definition.Id, PartyReach.Melee) ? 1 : 0)),
            ("ranged", value.Number(party.CanUseReach(member.Definition.Id, PartyReach.Ranged) ? 1 : 0)),
            ("casting", value.Number(party.CanUseReach(member.Definition.Id, PartyReach.Casting) ? 1 : 0))))).ToArray());
        InteractionObservation? selected = focus?.Candidates.FirstOrDefault(c => c.Selected);
        uint root = value.Object(
            ("seed", value.String(floor.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            ("level", value.Number(floor.Level(exploration.Position))),
            ("room", value.String(floor.Rooms.FirstOrDefault(r => r.Cells.Contains(exploration.Position))?.Title ?? "Passage")),
            ("x", value.Number(exploration.Position.X)), ("y", value.Number(exploration.Position.Y)),
            ("facing", value.String(exploration.Facing.ToString())),
            ("seconds", value.Number(exploration.ElapsedSeconds)),
            ("status", value.String(paused ? "Paused" : exploration.Position == floor.Exit ? "Exit reached" : "Exploring")),
            ("focusLabel", value.String(selected?.Candidate.Label ?? focus?.Reason.ToString() ?? "No feature")),
            ("focusId", value.String(focus?.Selected?.Id.ToString() ?? "")),
            ("focusRevision", value.String(focus?.Selected?.Revision.ToString() ?? "")),
            ("selectedMember", value.String(selectedMember)),
            ("commandRevision", value.String(commandRevision.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            ("paused", value.Number(paused ? 1 : 0)),
            ("feedback", value.String(feedback)),
            ("artStyle", value.String(artStyle)),
            ("roomLights", value.Number(roomLights ? 1 : 0)),
            ("lightPosition", value.Number(lightPosition + 1)),
            ("party", roster),
            ("combat", combat(value)),
            ("run", run(value)),
            ("inventory", InventoryProjection.Build(value, inventory, world, scene, exploration, party, selectedMember, art, dropReachable)),
            ("equipmentSlots", InventoryProjection.Equipment(value, inventory, selectedMember)),
            ("preset", value.String(preset)),
            ("presets", value.Object(characters.Presets.Select(p => (p.Id, value.Object(("name", value.String(p.Name))))).ToArray())),
            ("puzzle", value.Object(("status", value.String(world.Status(world.Weight(inventory, exploration, actor)))),
                ("doorId", value.String(world.Capture().DoorId.ToString())), ("doorRevision", value.String(world.Revision.ToString())),
                ("leverId", value.String(world.Capture().LeverId.ToString())), ("leverRevision", value.String(world.Revision.ToString())))));

        ui.PublishProjection(new UiProjection(stream, checked(++sequence), value.Build(root)));
    }
    public void Dispose() => stream.Dispose();
}
