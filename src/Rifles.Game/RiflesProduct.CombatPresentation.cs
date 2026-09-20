using System.Numerics;
using Rifles.Game.Combat;
using Rifles.Game.Dungeon;
using Rifles.Game.Items;
using Rifles.Game.Party;
using Rifles.Game.Presentation;
using Rusty.Engine;

namespace Rifles.Game;

public sealed partial class RiflesProduct
{
    private uint CombatProjection(SessionValueBuilder value)
    {
        uint foes = value.Object(enemies.Select(enemy => (enemy.Id.ToString(), value.Object(
            ("name", value.String(enemy.Definition.Name)), ("vitality", value.Number(enemy.Vitality)),
            ("maxVitality", value.Number(enemy.Definition.Vitality)), ("visible", value.Number(Visible(enemy) ? 1 : 0)),
            ("phase", value.String(enemy.Alive ? enemy.Action.Current is { } a ? a.Kind + " " + a.Phase : enemy.Brain.Mode.ToString() : "Dead")),
            ("conditions", value.String(magic!.Describe("enemy:" + enemy.Id))),
            ("position", value.String($"({enemy.Motion.Position.X}, {enemy.Motion.Position.Y}) · slot {enemy.Motion.CrowdOffset.X:0.##}, {enemy.Motion.CrowdOffset.Y:0.##}")),
            ("remaining", value.Number(enemy.Action.Current?.Remaining ?? 0)), ("kind", value.String(enemy.Definition.Attack.ToString()))))).ToArray());
        uint members = value.Object(party.Members.Select(member =>
        {
            CarriedItem? weapon = Weapon(member.Definition.Id);
            ActionSnapshot? action = actions[member.Definition.Id].Current;
            return (member.Definition.Id, value.Object(("phase", value.String(!member.IsLiving ? "Dead" : action is null ? "Ready" : action.Kind + " " + action.Phase)),
                ("remaining", value.Number(action?.Remaining ?? 0)), ("weapon", value.String(weapon is null ? "Unarmed" : definitions.Items.Item(weapon.Definition).Name)),
                ("loaded", value.Number(weapon is not null && loadedWeapons.Contains(weapon.Entity) ? 1 : 0)),
                ("ammunition", value.Number(PartyAmmo()))));
        }).ToArray());
        return value.Object(("selectedTarget", value.String(selectedTarget.ToString())), ("enemies", foes), ("members", members),
            ("magic", MagicProjection(value)), ("log", value.String(string.Join("\n", combatLog))), ("defeated", value.Number(Defeated ? 1 : 0)));
    }
    private IEnumerable<AppearanceFact> CombatFacts()
    {
        foreach (EnemyState enemy in enemies)
        {
            string image = SentryView.Select((enemy.Motion.VisualCell + enemy.Motion.VisualCrowdOffset), enemy.Motion.Facing, exploration.VisualCell);
            float scale = enemy.Definition.Scale * (!enemy.Alive ? Combat.CorpseScale : enemy.Action.Current?.Phase == ActionPhase.Windup ? Combat.WindupScale : 1);
            Vector3 position = scene!.Eye((enemy.Motion.VisualCell + enemy.Motion.VisualCrowdOffset)) with { Y = scene.GroundHeight(enemy.Motion.VisualCell) };
            yield return new(enemy.Id, false, 0, new Transform(position, Quaternion.Identity, new Vector3(scale)), combatArt!.Image(features!.Style, image), true, RenderLayer.Scene);
        }
        foreach (FlightSnapshot flight in flights)
        {
            CarriedItem? item = flight.Owner is null ? null : inventory!.Items(flight.Owner).Single();
            Vector3 position = new(flight.X, flight.Y, flight.Z);
            if (item is null)
                yield return new(flight.Id, false, 0, new Transform(position, Quaternion.Identity, new Vector3(Combat.BoltScale)), boltAppearance!, true, RenderLayer.Scene);
            else yield return itemArt!.At(flight.Id, position, definitions.Items.Item(item.Definition).Image, 1);
        }
        foreach ((string owner, var cell) in drops)
            foreach (CarriedItem item in inventory!.Items(owner).Where(i => i.Entity != 0).Concat(inventory.Items(owner).Where(i => i.Entity == 0).Take(1)))
                yield return itemArt!.At(item.Entity == 0 ? inventory.Owner(owner).Id : item.Entity,
                    scene!.Eye(cell) with { Y = scene.GroundHeight(cell) }, definitions.Items.Item(item.Definition).Image, 1);
    }
    private CombatSnapshot CaptureCombat() => new(enemies.Select(e => e.Capture()).ToArray(),
        actions.Select(a => new MemberActionSnapshot(a.Key, a.Value.Capture())).ToArray(), loadedWeapons.ToArray(), flights.ToArray(),
        drops.Select(d => new DropSnapshot(d.Key, d.Value)).ToArray(), allies.Select(a => new AllySnapshot(a.Key, a.Value.Vitality)).ToArray(), selectedTarget, magic!.Capture());
    private void ApplyCombatRestore(RestoredCombat restored)
    {
        enemies = restored.Enemies; actions = restored.Actions;
        loadedWeapons.Clear(); loadedWeapons.UnionWith(restored.LoadedWeapons);
        flights.Clear(); flights.AddRange(restored.Flights);
        drops.Clear(); foreach (DropSnapshot drop in restored.Drops) drops.Add(drop.Owner, drop.Cell);
        allies.Clear(); foreach (AllySnapshot ally in restored.Allies)
            // Synthetic stand-in, not an admitted archetype instance: the
            // archetype tag names the concept for #8358/#8360 to adopt.
            // Never validated or resolved; never touches spellbooks.
            allies.Add(ally.Id, new PartyMemberState(new MemberDefinition(ally.Id.ToString(), "garrison-ally", "Garrison ally",
                definitions.Party.Positions.OrderBy(p => p.Rank).First().Id, Combat.AllyVitality, StartingVitality: ally.Vitality),
                definitions.Party.Positions.OrderBy(p => p.Rank).First().Rank));
        selectedTarget = restored.SelectedTarget; combatLog.Clear();
        magic = restored.Magic; RecomputeMagic();
    }
}
