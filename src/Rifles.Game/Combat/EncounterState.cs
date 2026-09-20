using System.Numerics;
using Rifles.Game.Magic;
using Rifles.Game.Characters;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Procgen.Generation;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;

namespace Rifles.Game.Combat;

internal sealed record EnemySnapshot(ulong Id, string Definition, ExplorationSnapshot Motion, long Vitality,
    ActionSnapshot? Action, double DecisionRemaining, bool Aware, bool Loaded, string Owner, EnemyBrainSnapshot Brain, string Spawn, long Resource = 0);
internal sealed record MemberActionSnapshot(string Member, ActionSnapshot? Action);
internal sealed record FlightSnapshot(ulong Id, ulong Shooter, string? Member, CombatActionKind Kind, float X, float Y, float Z,
    float DirectionX, float DirectionY, float DirectionZ, float Remaining, GridPoint LastCell, string? Owner, string? Destination, string? Spell = null);
internal sealed record DropSnapshot(string Owner, GridPoint Cell);
internal sealed record AllySnapshot(ulong Id, long Vitality);
internal sealed record CombatSnapshot(EnemySnapshot[] Enemies, MemberActionSnapshot[] Members, ulong[] LoadedWeapons,
    FlightSnapshot[] Flights, DropSnapshot[] Drops, AllySnapshot[] Allies, ulong SelectedTarget, MagicSnapshot? Magic = null);

internal sealed class EnemyState
{
    private readonly StatsComponent stats;
    internal ulong Id { get; }
    internal EntityId Entity { get; }
    internal string Spawn { get; }
    internal EnemyDefinition Definition { get; }
    internal ExplorationState Motion { get; }
    internal ActionState Action { get; }
    internal string Owner { get; }
    internal double DecisionRemaining { get; set; }
    internal bool Aware => Brain.Mode is EnemyBrainMode.Pursue or EnemyBrainMode.Search;
    internal EnemyBrain Brain { get; }
    internal string NavigationStatus { get; set; } = "Ready";
    internal bool Loaded { get; set; }
    internal long Resource => stats.TryGetTrack(RiflesStatIds.Resource, out Track? resource) ? resource.ValueInt64 : 0;
    internal void SpendResource(long cost)
    {
        // No resource track when the maximum is zero: spending is a silent
        // no-op, matching the previous zero-maximum track behavior.
        if (stats.TryGetTrack(RiflesStatIds.Resource, out Track? resource)) resource.Spend(cost);
    }
    internal long Vitality => stats.GetTrack(RiflesStatIds.Vitality).ValueInt64;
    internal bool Alive => Vitality > 0;
    internal EnemyState(EnemySnapshot saved, EnemyDefinition definition, DungeonFloor floor, ExplorationTuning tuning, CharacterEntities entities, long maximumResource = 0)
    {
        ArgumentNullException.ThrowIfNull(entities);
        GameDefinitions.Require(saved.Vitality >= 0 && saved.Vitality <= definition.Vitality
            && double.IsFinite(saved.DecisionRemaining) && saved.DecisionRemaining >= 0
            && saved.DecisionRemaining <= definition.DecisionSeconds, "saved enemy state");
        GameDefinitions.Require(saved.Resource >= 0 && saved.Resource <= maximumResource, "saved enemy resource");
        (Entity, stats) = entities.ReplaceStats("enemy:" + saved.Id, "rifles:enemy:" + definition.Id,
            () => RiflesStats.ForVitality(definition.Vitality, saved.Vitality, maximumResource, saved.Resource));
        Id = saved.Id; Spawn = saved.Spawn; Definition = definition; Owner = saved.Owner;
        Motion = ExplorationState.Restore(saved.Motion, floor, tuning with { StepSeconds = definition.StepSeconds });
        Action = ActionState.Restore(saved.Action);
        NavigationStatus = Motion.Moving ? "Moving" : "Ready";
        GameDefinitions.Require(saved.Vitality > 0 || !Motion.Moving && !Action.Busy, "dead enemy activity");
        DecisionRemaining = saved.DecisionRemaining; Loaded = saved.Loaded;
        Brain = EnemyBrain.FromSnapshot(definition.Brain, saved.Brain, floor.Cells.ToHashSet());
    }
    internal long Damage(long amount)
    {
        long applied = Math.Min(Vitality, amount);
        stats.GetTrack(RiflesStatIds.Vitality).Spend(applied);
        return applied;
    }
    internal EnemySnapshot Capture() => new(Id, Definition.Id, Motion.Capture(), Vitality, Action.Capture(), DecisionRemaining, Aware, Loaded, Owner, Brain.Capture(), Spawn, Resource);
}
