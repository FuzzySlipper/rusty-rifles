using System.Numerics;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Procgen.Generation;
using Rusty.Engine.Mechanics;

namespace Rifles.Game.Combat;

internal sealed record EnemySnapshot(ulong Id, string Definition, ExplorationSnapshot Motion, long Vitality,
    ActionSnapshot? Action, double DecisionRemaining, bool Aware, bool Loaded, string Owner);
internal sealed record MemberActionSnapshot(string Member, ActionSnapshot? Action);
internal sealed record FlightSnapshot(ulong Id, ulong Shooter, string? Member, CombatActionKind Kind, float X, float Y, float Z,
    float DirectionX, float DirectionY, float DirectionZ, float Remaining, GridPoint LastCell, string? Owner, string? Destination);
internal sealed record DropSnapshot(string Owner, GridPoint Cell);
internal sealed record AllySnapshot(ulong Id, long Vitality);
internal sealed record CombatSnapshot(EnemySnapshot[] Enemies, MemberActionSnapshot[] Members, ulong[] LoadedWeapons,
    FlightSnapshot[] Flights, DropSnapshot[] Drops, AllySnapshot[] Allies, ulong SelectedTarget);

internal sealed class EnemyState
{
    private readonly ExactTrack vitality;
    internal ulong Id { get; }
    internal EnemyDefinition Definition { get; }
    internal ExplorationState Motion { get; }
    internal ActionState Action { get; }
    internal string Owner { get; }
    internal double DecisionRemaining { get; set; }
    internal bool Aware { get; set; }
    internal bool Loaded { get; set; }
    internal long Vitality => vitality.Current.Raw;
    internal bool Alive => Vitality > 0;
    internal EnemyState(EnemySnapshot saved, EnemyDefinition definition, DungeonFloor floor, ExplorationTuning tuning)
    {
        GameDefinitions.Require(saved.Vitality >= 0 && saved.Vitality <= definition.Vitality
            && double.IsFinite(saved.DecisionRemaining) && saved.DecisionRemaining >= 0
            && saved.DecisionRemaining <= definition.DecisionSeconds, "saved enemy state");
        Id = saved.Id; Definition = definition; Owner = saved.Owner;
        Motion = ExplorationState.Restore(saved.Motion, floor, tuning with { StepSeconds = definition.StepSeconds });
        Action = ActionState.Restore(saved.Action);
        GameDefinitions.Require(saved.Vitality > 0 || !Motion.Moving && !Action.Busy, "dead enemy activity");
        vitality = new ExactTrack(new ExactTrackDefinition(TrackId.Parse("rifles.enemy.vitality"), ExactValue.Zero,
            new ExactTrackMaximum.Fixed(new ExactValue(definition.Vitality))), new ExactValue(saved.Vitality));
        DecisionRemaining = saved.DecisionRemaining; Aware = saved.Aware; Loaded = saved.Loaded;
    }
    internal long Damage(long amount)
    {
        long applied = Math.Min(Vitality, amount);
        vitality.Spend(new ExactValue(applied));
        return applied;
    }
    internal EnemySnapshot Capture() => new(Id, Definition.Id, Motion.Capture(), Vitality, Action.Capture(), DecisionRemaining, Aware, Loaded, Owner);
}
