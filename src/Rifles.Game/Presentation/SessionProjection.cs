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
    private UiValue? previous;
    internal SessionProjection(IUiService ui)
    {
        this.ui = ui;
        stream = ui.OpenStream(new UiStreamRequest("rifles.session", "rifles.session.v1"));
    }

    internal void Publish(DungeonFloor floor, ExplorationState exploration, PartyState party, bool paused, string feedback, string selectedMember, InteractionReadout? focus, string artStyle, bool roomLights, int lightPosition, ItemInventory inventory, ExplorationItems world, DungeonScene scene, CharacterOptionsDefinition characters, string preset, PatrolActor actor, ItemArtDefinition art, Func<SessionValueBuilder, uint> combat, Func<string, bool> dropReachable, Func<SessionValueBuilder, uint> run, FormationDefinition formation)
    {
        SessionValueBuilder value = new();
        Dictionary<string, string> positionNames = party.Positions.ToDictionary(p => p.Id, p => p.Name);
        string Protection(Rifles.Game.Characters.RiflesCharacter member)
        {
            if (!member.Definition.Commander)
                return !member.IsLiving ? "Fallen: no screening" : string.Join(", ", formation.Cell(member.Position).Screening.Select(screen => $"{screen.Sector} {screen.Lane}"));
            var exposed = formation.Cells.SelectMany(cell => cell.Screening).Distinct()
                .Where(screen => party.ScreenedRecipient(formation, new(screen.Sector, screen.Lane)) == member)
                .Select(screen => $"{screen.Sector} {screen.Lane}");
            return "Exposed: " + string.Join(", ", exposed);
        }
        uint roster = value.Object(party.Members.Select(member => (member.Definition.Id, value.Object(
            ("name", value.String(member.Definition.Name)),
            ("position", value.String(member.Position)),
            ("positionName", value.String(positionNames.GetValueOrDefault(member.Position, member.Position))),
            ("rank", value.Number(member.Rank)),
            // All formation positions rotate with the party.
            ("facing", value.String(exploration.Facing.ToString())),
            ("vitality", value.Number(member.Vitality)),
            ("maximumVitality", value.Number(member.MaximumVitality)),
            ("power", value.Number(member.Power)), ("defense", value.Number(member.Defense)),
            ("resource", value.Number(member.Resource)), ("maxResource", value.Number(member.MaximumResource)),
            ("commander", value.Number(member.Definition.Commander ? 1 : 0)),
            ("protection", value.String(Protection(member))),
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
            ("seconds", value.Number(Math.Floor(exploration.ElapsedSeconds))),
            ("status", value.String(paused ? "Paused" : exploration.Position == floor.Exit ? "Exit reached" : "Exploring")),
            ("focusLabel", value.String(selected?.Candidate.Label ?? focus?.Reason.ToString() ?? "No feature")),
            ("focusId", value.String(focus?.Selected?.Id.ToString() ?? "")),
            ("focusRevision", value.String(focus?.Selected?.Revision.ToString() ?? "")),
            ("selectedMember", value.String(selectedMember)),
            ("paused", value.Number(paused ? 1 : 0)),
            ("feedback", value.String(feedback)),
            ("artStyle", value.String(artStyle)),
            ("roomLights", value.Number(roomLights ? 1 : 0)),
            ("lightPosition", value.Number(lightPosition + 1)),
            ("party", roster),
            ("formation", value.Object(
                ("open", value.Number(party.Formation.Open ? 1 : 0)),
                ("executing", value.Number(party.Formation.Executing ? 1 : 0)),
                ("remaining", value.Number(party.Formation.Execution?.Remaining ?? 0)),
                ("duration", value.Number(formation.RepositionSeconds)),
                ("changed", value.Number(party.Formation.Open ? party.Formation.PlannedMoves().Length : 0)),
                ("draft", value.Object((party.Formation.Draft ?? new Dictionary<string, string>())
                    .Select(entry => (entry.Key, value.String(entry.Value))).ToArray())),
                ("moves", value.Object((party.Formation.Execution?.Moves ?? [])
                    .Select(move => (move.Member, value.String(move.To))).ToArray())))),
            ("positions", value.Object(party.Positions.Select(p => (p.Id, value.Object(
                ("name", value.String(p.Name)), ("rank", value.Number(p.Rank)),
                ("offsetForward", value.Number(p.OffsetForward)), ("offsetLeft", value.Number(p.OffsetLeft))))).ToArray())),
            ("combat", combat(value)),
            ("run", run(value)),
            ("inventory", InventoryProjection.Build(value, inventory, world, scene, exploration, party, selectedMember, art, dropReachable)),
            ("equipmentSlots", InventoryProjection.Equipment(value, inventory, selectedMember)),
            ("preset", value.String(preset)),
            ("presets", value.Object(characters.Presets.Select(p => (p.Id, value.Object(("name", value.String(p.Name))))).ToArray())),
            ("puzzle", value.Object(("status", value.String(world.Status(world.Weight(inventory, exploration, actor)))),
                ("doorId", value.String(world.DoorId.ToString())), ("doorRevision", value.String(world.Revision.ToString())),
                ("leverId", value.String(world.LeverId.ToString())), ("leverRevision", value.String(world.Revision.ToString())))));

        UiValue snapshot = value.Build(root);
        // Engine retains the last complete projection for attachment/recovery.
        // Do not send an identical full HUD merely because its refresh interval elapsed.
        if (previous is not null && snapshot.Root == previous.Root
            && snapshot.Nodes.Span.SequenceEqual(previous.Nodes.Span)
            && snapshot.Edges.Span.SequenceEqual(previous.Edges.Span)
            && snapshot.Utf8.Span.SequenceEqual(previous.Utf8.Span)) return;
        ui.PublishProjection(new UiProjection(stream, checked(++sequence), snapshot));
        previous = snapshot;
    }
    public void Dispose() => stream.Dispose();
}
