using System.Numerics;
using Rifles.Game.Characters;
using Rifles.Game.Combat;
using Rifles.Game.Dungeon;
using Rifles.Game.Magic;
using Rifles.Game.Items;
using Rifles.Game.Party;
using Rifles.Game.Presentation;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;

namespace Rifles.Game;

public sealed partial class RiflesProduct
{
    private uint CombatProjection(SessionValueBuilder value)
    {
        uint foes = value.Object(active.Combat.Enemies.Select(enemy => (enemy.Id.ToString(), value.Object(
            ("name", value.String(enemy.Definition.Name)), ("vitality", value.Number(enemy.Vitality)),
            ("maxVitality", value.Number(enemy.Definition.Vitality)), ("visible", value.Number(active.Combat.Visible(enemy) ? 1 : 0)),
            ("phase", value.String(enemy.Alive ? enemy.Action.Current is { } a ? a.Kind + " " + a.Phase : enemy.Brain.Mode.ToString() : "Dead")),
            ("conditions", value.String(active.Magic.Describe(new EnemyTarget(enemy.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))))),
            ("position", value.String($"({enemy.Motion.Position.X}, {enemy.Motion.Position.Y}) · slot {enemy.Motion.CrowdOffset.X:0.##}, {enemy.Motion.CrowdOffset.Y:0.##}")),
            ("remaining", value.Number(enemy.Action.Current?.Remaining ?? 0)), ("kind", value.String(enemy.Definition.Attack.ToString()))))).ToArray());
        uint members = value.Object(party.Members.Select(member =>
        {
            CarriedItem? weapon = active.Combat.Weapon(member.Definition.Id);
            ActionSnapshot? action = active.Combat.ActionOf(member).Current;
            return (member.Definition.Id, value.Object(("phase", value.String(!member.IsLiving ? "Dead" : action is null ? "Ready" : action.Kind + " " + action.Phase)),
                ("remaining", value.Number(action?.Remaining ?? 0)), ("weapon", value.String(weapon is null ? "Unarmed" : definitions.Items.Item(weapon.Definition).Name)),
                ("loaded", value.Number(weapon is not null && active.Combat.LoadedWeapons.Contains(weapon.Entity) ? 1 : 0)),
                ("ammunition", value.Number(active.Combat.PartyAmmo()))));
        }).ToArray());
        return value.Object(("selectedTarget", value.String(active.Combat.SelectedTarget.ToString())), ("enemies", foes), ("members", members),
            ("magic", MagicProjection(value)), ("log", value.String(string.Join("\n", active.Combat.Log))), ("defeated", value.Number(active.Combat.Defeated ? 1 : 0)));
    }
    private IEnumerable<AppearanceFact> CombatFacts()
    {
        foreach (EnemyState enemy in active.Combat.Enemies)
        {
            string image = SentryView.Select((enemy.Motion.VisualCell + enemy.Motion.VisualCrowdOffset), enemy.Motion.Facing, active.Exploration.VisualCell);
            float scale = enemy.Definition.Scale * (!enemy.Alive ? Combat.CorpseScale : enemy.Action.Current?.Phase == ActionPhase.Windup ? Combat.WindupScale : 1);
            Vector3 position = active.Scene.Eye((enemy.Motion.VisualCell + enemy.Motion.VisualCrowdOffset)) with { Y = active.Scene.GroundHeight(enemy.Motion.VisualCell) };
            yield return new(enemy.Id, false, 0, new Transform(position, Quaternion.Identity, new Vector3(scale)), combatArt!.Image(active.Features!.Style, image), true, RenderLayer.Scene);
        }
        foreach (FlightState flight in active.Combat.Flights)
        {
            CarriedItem? item = flight.Owner is null ? null : active.Inventory!.Items(flight.Owner).Single();
            Vector3 position = new(flight.X, flight.Y, flight.Z);
            if (item is null)
                yield return new(flight.Id, false, 0, new Transform(position, Quaternion.Identity, new Vector3(Combat.BoltScale)), boltAppearance!, true, RenderLayer.Scene);
            else yield return itemArt!.At(flight.Id, position, definitions.Items.Item(item.Definition).Image, 1);
        }
        foreach ((string owner, var cell) in active.Combat.Drops)
            foreach (CarriedItem item in active.Inventory!.Items(owner).Where(i => i.Entity != 0).Concat(active.Inventory.Items(owner).Where(i => i.Entity == 0).Take(1)))
                yield return itemArt!.At(item.Entity == 0 ? active.Inventory.Owner(owner).Id : item.Entity,
                    active.Scene.Eye(cell) with { Y = active.Scene.GroundHeight(cell) }, definitions.Items.Item(item.Definition).Image, 1);
    }
}
