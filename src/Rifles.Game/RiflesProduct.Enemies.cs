using Rifles.Game.Audio;
using System.Numerics;
using Rifles.Game.Combat;
using Rifles.Game.Dungeon;
using Rifles.Procgen.Generation;
using Rusty.Engine;

namespace Rifles.Game;

public sealed partial class RiflesProduct
{
    private enum NoiseKind { Footstep, Gunfire, Alarm }
    private Vector3 EnemyAim(EnemyState enemy) => Aim(enemy.Motion.Position)
        + new Vector3(enemy.Motion.CrowdOffset.X, 0, enemy.Motion.CrowdOffset.Y) * scene!.LogicalCellSize;



    private void EmitNoise(GridPoint cell, NoiseKind kind, ulong emitter = 0)
    {
        if (kind == NoiseKind.Gunfire) audio!.Play(SoundCue.Rifle, Aim(cell));
        // Events are consumed now on the active floor, never replayed from a save.
        foreach (EnemyState enemy in enemies.Where(e => e.Alive && e.Id != emitter))
        {
            EnemyBrainDefinition tuning = enemy.Definition.Brain;
            float range = kind switch { NoiseKind.Gunfire => tuning.GunfireHearingRange,
                NoiseKind.Alarm => tuning.AlarmHearingRange, _ => tuning.FootstepHearingRange };
            if (Vector3.Distance(EnemyAim(enemy), Aim(cell)) <= range)
                enemy.Brain.Observe(null, cell);
        }
    }



    private bool SeesParty(EnemyState enemy)
    {
        Vector3 delta = Aim(exploration.Position) - EnemyAim(enemy);
        if (delta.Length() > enemy.Definition.AwarenessRange) return false;
        GridPoint forward = enemy.Motion.Facing.Offset();
        if (delta.LengthSquared() > 0 && Vector3.Dot(Vector3.Normalize(delta), new(forward.X, 0, forward.Y))
            < Math.Cos(enemy.Definition.Brain.SightConeDegrees * Math.PI / 360)) return false;
        RoomDressing dressing = features!.Capture().Dressing;
        SpatialEntityCollider[] sightBodies = CombatBodies().Where(body => Combat.ActorsBlockSight
            || body.Entity == partyId || body.Entity == dressing.BenchId || body.Entity == dressing.CrateId).ToArray();
        SpatialHit sight = scene!.Trace(EnemyAim(enemy), Aim(exploration.Position), sightBodies, enemy.Id);
        return sight.Present && sight.Kind == SpatialHitKind.Entity && sight.Entity == partyId;
    }

    private bool Face(EnemyState enemy, GridPoint target)
    {
        int dx = target.X - enemy.Motion.Position.X, dy = target.Y - enemy.Motion.Position.Y;
        if (dx == 0 && dy == 0) return true;
        CardinalDirection facing = Math.Abs(dx) >= Math.Abs(dy) ? dx >= 0 ? CardinalDirection.East : CardinalDirection.West
            : dy >= 0 ? CardinalDirection.South : CardinalDirection.North;
        if (enemy.Motion.Facing == facing) return true;
        enemy.Motion.Act(enemy.Motion.Facing.Rotate(-1) == facing ? ExplorationAction.TurnLeft : ExplorationAction.TurnRight);
        return false;
    }

    private void AdvanceEnemies(double seconds)
    {
        foreach (EnemyState enemy in enemies.Where(e => e.Alive))
        {
            enemy.Motion.Advance(seconds, magic!.Speed("enemy:" + enemy.Id));
            enemy.Brain.Advance(seconds);
            enemy.DecisionRemaining = Math.Max(0, enemy.DecisionRemaining - seconds);
            if (Defeated) { enemy.Action.Cancel(); enemy.Motion.Stop(); continue; }
            try { enemy.Action.Advance(seconds * magic!.Speed("enemy:" + enemy.Id), action => CommitEnemy(enemy, action)); }
            catch (InvalidOperationException error) { CombatMessage(enemy.Definition.Name + ": " + error.Message); }
        }
        if (Defeated || enemies.Length == 0) return;
        int queries = Combat.PathQueriesPerStep;
        // Rotate the first decision slot so bounded path work cannot starve later actors.
        for (int index = 0; index < enemies.Length; index++)
        {
            EnemyState enemy = enemies[(pathCursor + index) % enemies.Length];
            if (!enemy.Alive || enemy.Motion.Moving || enemy.Action.Busy || enemy.DecisionRemaining > 0) continue;
            enemy.DecisionRemaining = enemy.Definition.DecisionSeconds;
            bool sees = SeesParty(enemy);
            enemy.Brain.Observe(sees ? exploration.Position : null, null);
            GridPoint? goal = enemy.Brain.Goal(enemy.Motion.Position);
            if (goal is null || goal == enemy.Motion.Position)
            {
                enemy.NavigationStatus = "Holding";
                if (enemy.Brain.Mode == EnemyBrainMode.Search) enemy.Motion.Act(ExplorationAction.TurnRight);
                continue;
            }
            if (!sees && !Face(enemy, goal.Value)) continue;
            bool retreat = false;
            if (sees)
            {
                if (!Face(enemy, exploration.Position)) continue;
                float distance = Vector3.Distance(EnemyAim(enemy), Aim(exploration.Position));
                CombatActionKind kind = enemy.Definition.Attack;
                bool ranged = kind == CombatActionKind.Fire && (enemy.Loaded || Ammo(enemy.Owner) > 0);
                if (!ranged) kind = CombatActionKind.Melee;
                retreat = ranged && enemy.Brain.NeedsRetreat(distance) && enemy.Brain.RetreatReady;
                SpatialHit shot = scene!.Trace(EnemyAim(enemy), Aim(exploration.Position), CombatBodies(), enemy.Id);
                bool clearShot = shot.Present && shot.Kind == SpatialHitKind.Entity && shot.Entity == partyId;
                if (clearShot && TryEnemySpell(enemy, distance)) { enemy.NavigationStatus = "Casting"; continue; }
                if (!retreat && clearShot && distance <= Combat.Action(kind).Range
                    && (!ranged || distance <= enemy.Definition.Brain.PreferredMaximumRange))
                {
                    BeginEnemyAttack(enemy, kind);
                    enemy.NavigationStatus = "Engaging"; continue;
                }
            }
            if (!enemy.Brain.CanRequestPath) { enemy.NavigationStatus = "Waiting to replan"; continue; }
            if (queries == 0) { enemy.DecisionRemaining = 0; enemy.NavigationStatus = "Waiting for path budget"; continue; }
            GridPoint[] goals = EnemyGoals(enemy, goal.Value, sees, retreat).Take(Math.Min(queries, Combat.PathGoalsPerDecision)).ToArray();
            queries -= goals.Length;
            GridPoint door = itemWorld!.Capture().Door;
            bool tooWideForDoor = definitions.Crowd.Footprints[enemy.Definition.Footprint].EdgeClearance > Combat.DoorClearance;
            var narrowLandings = floor.Connectors.Where(c => c.Clearance < definitions.Crowd.Footprints[enemy.Definition.Footprint].EdgeClearance)
                .Select(c => c.To).ToHashSet();
            IEnumerable<GridPoint> blocked = floor.Cells.Where(cell => !movement!.CanFit(enemy.Id, cell)
                || tooWideForDoor && cell == door || narrowLandings.Contains(cell));
            GridPoint? next = scene!.NextStep(enemy.Motion.Position, goals, blocked);
            if (next is { } step && enemy.Motion.StepTo(step))
            {
                if (retreat) enemy.Brain.TrySpendRetreatStep();
                enemy.NavigationStatus = retreat ? "Retreating" : "Moving";
            }
            else
            {
                enemy.Brain.ReportPathUnavailable();
                if (retreat && sees)
                {
                    SpatialHit shot = scene!.Trace(EnemyAim(enemy), Aim(exploration.Position), CombatBodies(), enemy.Id);
                    if (shot.Present && shot.Kind == SpatialHitKind.Entity && shot.Entity == partyId
                        && Vector3.Distance(EnemyAim(enemy), Aim(exploration.Position)) <= Combat.Action(CombatActionKind.Fire).Range)
                    {
                        BeginEnemyAttack(enemy, CombatActionKind.Fire);
                        enemy.NavigationStatus = "Holding ground";
                        continue;
                    }
                }
                enemy.NavigationStatus = movement!.Blocked(enemy.Id) is { } waiting
                    ? waiting.Expired ? "Route blocked" : "Waiting for occupied step" : "Route blocked";
            }
        }
        pathCursor = (pathCursor + 1) % enemies.Length;
    }

    private void BeginEnemyAttack(EnemyState enemy, CombatActionKind kind)
    {
        if (kind == CombatActionKind.Fire && !enemy.Loaded) enemy.Action.Start(NewAction(CombatActionKind.Reload));
        else enemy.Action.Start(NewAction(kind, target: partyId, aim: exploration.Position));
    }

    private IEnumerable<GridPoint> EnemyGoals(EnemyState enemy, GridPoint known, bool sees, bool retreat)
    {
        GridPoint current = enemy.Motion.Position;
        if (!sees)
            return movement!.CanFit(enemy.Id, known) ? new[] { known }
                : CardinalDirections.Ordered.Select(d => known + d.Offset()).Where(c => floor.Cells.Contains(c) && movement.CanFit(enemy.Id, c))
                    .OrderBy(c => c.ManhattanDistance(current));
        if (enemy.Definition.Attack != CombatActionKind.Fire || !enemy.Loaded && Ammo(enemy.Owner) == 0)
            return CardinalDirections.Ordered.Select(d => known + d.Offset()).Where(floor.Cells.Contains)
                .OrderBy(c => c.ManhattanDistance(current));
        EnemyBrainDefinition tuning = enemy.Definition.Brain;
        float oldDistance = Vector3.Distance(Aim(current), Aim(known));
        // Candidate facts use the currently visible target only; last-known pursuit never queries its live pose.
        return floor.Cells.Where(c => c != current && movement!.CanFit(enemy.Id, c))
            .Where(c => !retreat || c.ManhattanDistance(current) == 1)
            .Select(c => (Cell: c, Distance: Vector3.Distance(Aim(c), Aim(known))))
            .Where(c => c.Distance <= tuning.PreferredMaximumRange && (!retreat || c.Distance > oldDistance))
            .OrderBy(c => c.Distance < tuning.PreferredMinimumRange ? tuning.PreferredMinimumRange - c.Distance : 0)
            .ThenBy(c => c.Cell.ManhattanDistance(current)).ThenBy(c => c.Cell.Y).ThenBy(c => c.Cell.X)
            .Take(Combat.CandidateCellsPerDecision)
            .Where(c => { SpatialHit hit = scene!.Trace(Aim(c.Cell), Aim(known), CombatBodies(), enemy.Id);
                return hit.Present && hit.Kind == SpatialHitKind.Entity && hit.Entity == partyId; })
            .Select(c => c.Cell);
    }
}
