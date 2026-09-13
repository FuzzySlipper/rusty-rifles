using Rusty.Engine;
using Rifles.Game.Dungeon;
using Rifles.Game.Party;

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

    internal void Publish(DungeonFloor floor, ExplorationState exploration, PartyState party, bool paused)
    {
        SessionValueBuilder value = new();
        uint roster = value.Object(party.Members.Select(member => (member.Definition.Id, value.Object(
            ("name", value.String(member.Definition.Name)),
            ("slot", value.String(member.Definition.Slot.ToString())),
            ("vitality", value.Number(member.Vitality)),
            ("maximumVitality", value.Number(member.Definition.MaximumVitality))))).ToArray());
        uint root = value.Object(
            ("seed", value.String(floor.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            ("x", value.Number(exploration.Position.X)), ("y", value.Number(exploration.Position.Y)),
            ("facing", value.String(exploration.Facing.ToString())),
            ("seconds", value.Number(exploration.ElapsedSeconds)),
            ("status", value.String(paused ? "Paused" : exploration.Position == floor.Exit ? "Exit reached" : "Exploring")),
            ("party", roster));
        ui.PublishProjection(new UiProjection(stream, checked(++sequence), value.Build(root)));
    }
    public void Dispose() => stream.Dispose();
}
