using System.Text.Json;
using Rifles.Game.Combat;
using Rifles.Game.Items;
using Rusty.Engine.Debugging;

namespace Rifles.Game;

public sealed partial class RiflesProduct
{
    private static readonly string[] PlaytestActions = ["forward", "backward", "strafe-left", "strafe-right", "turn-left", "turn-right", "pause", "use", "cycle", "fire", "melee", "reload", "fix-bayonets", "unfix-bayonets", "charge", "save", "load"];

    private DebugCommandResult ObservePlaytest() => DebugCommandResult.Success(JsonSerializer.Serialize(new
    {
        pose = new { x = active.Exploration.Position.X, y = active.Exploration.Position.Y, facing = active.Exploration.Facing.ToString(), moving = active.Exploration.Moving },
        paused, floor = active.Floor.IntentFloorId, difficulty = progress.Difficulty,
        incomingDamageMultiplier = active.Combat.IncomingDamageMultiplier,
        formation = new { open = party.Formation.Open, executing = party.Formation.Executing },
        party = party.Members.Select(member => new { id = member.InstanceId, vitality = member.Vitality, resource = member.Resource, position = member.Position }),
        enemies = active.Combat.Enemies.Select(enemy => new { id = enemy.Id, alive = enemy.Alive, x = enemy.Motion.Position.X, y = enemy.Motion.Position.Y }),
        featureRevision = active.GeneratedFeatures.Revision,
        feedback,
    }));

    private PlaytestAction ResolvePlaytestAction(string id)
    {
        double seconds = definitions.Exploration.StepSeconds / PartySpeed;
        bool hold = false;
        string key = id switch
        {
            "forward" => "KeyW", "backward" => "KeyS", "strafe-left" => "KeyA", "strafe-right" => "KeyD",
            "turn-left" => "KeyQ", "turn-right" => "KeyE", "pause" => "KeyP", "use" => "KeyF", "cycle" => "KeyT",
            "fire" => "Space", "melee" => "KeyV", "reload" => "KeyR", "fix-bayonets" => "KeyB", "unfix-bayonets" => "KeyN",
            "charge" => "KeyC", "save" => "KeyK", "load" => "KeyL",
            _ => throw new InvalidDataException("Unknown playtest action '" + id + "'."),
        };
        if (id is "forward" or "backward" or "strafe-left" or "strafe-right") hold = true;
        else if (id is "turn-left" or "turn-right") { hold = true; seconds = definitions.Exploration.TurnSeconds; }
        else if (id is "fire" or "melee" or "reload" or "fix-bayonets" or "unfix-bayonets")
        {
            CombatActionKind kind = id switch
            {
                "fire" => CombatActionKind.Fire, "melee" => CombatActionKind.Melee,
                "reload" => CombatActionKind.Reload, "fix-bayonets" => CombatActionKind.FixBayonet,
                _ => CombatActionKind.UnfixBayonet,
            };
            var durations = party.Soldiers.Select(member =>
            {
                WeaponCapabilities? weapon = active.Combat.WeaponCapabilities(member.InstanceId);
                BayonetDefinition? bayonet = weapon is null ? null : definitions.Items.Item(weapon.WeaponId).Weapon?.Bayonet;
                if (id is "fix-bayonets" or "unfix-bayonets")
                {
                    BayonetActionTiming? timing = id == "fix-bayonets" ? bayonet?.Fix : bayonet?.Unfix;
                    return timing is null ? 0 : timing.WindupSeconds + timing.RecoverySeconds;
                }
                var fallback = definitions.Combat.Action(kind);
                return weapon is null ? fallback.Windup + fallback.Recovery
                    : (id == "reload" ? weapon.ReloadSeconds : weapon.WindupSeconds) + weapon.RecoverySeconds;
            });
            seconds = durations.DefaultIfEmpty(seconds).Max();
        }
        // One extra admitted update starts the action; completion uses the authored duration.
        seconds += lastFixedDeltaSeconds;
        bool available = id is "pause" or "save" or "load" || !paused && !active.Combat.Defeated;
        return new(id, key, seconds * 1000, hold, available, available ? null : "Resume with a living party first.");
    }

    private double lastFixedDeltaSeconds;
}
