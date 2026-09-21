using Rifles.Game.Content;
using Rifles.Game.Items;

namespace Rifles.Game.Combat;

internal sealed record ActionDefinition(CombatActionKind Kind, double Windup, double Recovery, float Range, long Damage, float Speed, long ResourceCost);
internal sealed record EnemyDefinition(string Id, string Name, CombatActionKind Attack, long Vitality, long Defense,
    double StepSeconds, double DecisionSeconds, float AwarenessRange, float Scale, StartingItem[] Loot, string Footprint, string Faction, bool Share, EnemyBrainDefinition Brain);
internal sealed record EnemySpawnDefinition(string Id, string Enemy, int Distance, int[][] PatrolOffsets);
internal sealed record CombatDefinition(ActionDefinition[] Actions, EnemyDefinition[] Enemies, EnemySpawnDefinition[] Encounter, float BodyWidth,
    float BodyHeight, float AimHeight, float TargetAngle, long MinimumDamage, bool FriendlyFire, long AllyVitality,
    float CorpseScale, float WindupScale, float BoltScale, int LogLength, string AmmunitionItem,
    PackDefinition DropCapacity, bool RecoverThrownItems, float[] BoltColor, int PathQueriesPerStep, int PathGoalsPerDecision, float DoorClearance, int CandidateCellsPerDecision, bool ActorsBlockSight)
{
    internal static bool UsesCombatTuning(CombatActionKind kind) => kind is CombatActionKind.Melee or CombatActionKind.Fire
        or CombatActionKind.Reload or CombatActionKind.Throw or CombatActionKind.Consume;

    internal ActionDefinition Action(CombatActionKind kind) => UsesCombatTuning(kind)
        ? Actions.Single(a => a.Kind == kind) : throw new InvalidOperationException($"{kind} has item-authored timing.");
    internal EnemyDefinition Enemy(string id) => Enemies.Single(e => e.Id == id);
    internal void Validate()
    {
        GameDefinitions.Require(Actions.Length == Enum.GetValues<CombatActionKind>().Count(UsesCombatTuning)
            && Actions.Select(a => a.Kind).Distinct().Count() == Actions.Length, "combat actions");
        foreach (ActionDefinition action in Actions)
            GameDefinitions.Require(UsesCombatTuning(action.Kind) && double.IsFinite(action.Windup) && action.Windup > 0
                && double.IsFinite(action.Recovery) && action.Recovery > 0 && float.IsFinite(action.Range) && action.Range > 0
                && action.Damage >= 0 && action.Damage <= 1000 && float.IsFinite(action.Speed) && action.Speed > 0
                && action.ResourceCost >= 0 && action.ResourceCost <= 1000, "combat action " + action.Kind);
        GameDefinitions.Require(Enemies.Length > 0 && Enemies.Select(e => e.Id).Distinct().Count() == Enemies.Length, "enemy ids");
        foreach (EnemyDefinition enemy in Enemies)
        {
            enemy.Brain.Validate();
            GameDefinitions.Require(!string.IsNullOrWhiteSpace(enemy.Footprint) && !string.IsNullOrWhiteSpace(enemy.Faction), "enemy crowd profile");
            GameDefinitions.Require(!string.IsNullOrWhiteSpace(enemy.Id) && !string.IsNullOrWhiteSpace(enemy.Name)
                && enemy.Attack is CombatActionKind.Melee or CombatActionKind.Fire && enemy.Vitality is > 0 and <= 1000
                && enemy.Defense is >= 0 and <= 1000 && double.IsFinite(enemy.StepSeconds) && enemy.StepSeconds > 0
                && double.IsFinite(enemy.DecisionSeconds) && enemy.DecisionSeconds > 0 && float.IsFinite(enemy.AwarenessRange)
                && enemy.AwarenessRange > 0 && float.IsFinite(enemy.Scale) && enemy.Scale > 0
                && enemy.Loot.All(l => l.Quantity > 0 && !l.Equipped), "enemy " + enemy.Id);
        }
        GameDefinitions.Require(Encounter.Length > 0 && Encounter.Select(e => e.Id).Distinct().Count() == Encounter.Length, "encounter identities");
        foreach (var spawn in Encounter)
            GameDefinitions.Require(!string.IsNullOrWhiteSpace(spawn.Id) && Enemies.Any(e => e.Id == spawn.Enemy) && spawn.Distance > 0
                && spawn.PatrolOffsets.Length > 0 && spawn.PatrolOffsets.All(p => p.Length == 2 && p.All(v => Math.Abs((long)v) <= 16)), "encounter spawn " + spawn.Id);
        GameDefinitions.Require(CandidateCellsPerDecision > 0 && CandidateCellsPerDecision <= 128, "enemy candidate budget");
        GameDefinitions.Require(PathQueriesPerStep > 0 && PathQueriesPerStep <= 32 && PathGoalsPerDecision > 0 && PathGoalsPerDecision <= 16
            && float.IsFinite(DoorClearance) && DoorClearance > 0 && DoorClearance <= 1, "enemy navigation work and clearance");
        GameDefinitions.Require(BoltColor.Length == 4 && BoltColor.All(v => float.IsFinite(v) && v >= 0 && v <= 1), "bolt color");
        GameDefinitions.Require(new[] { BodyWidth, BodyHeight, AimHeight, CorpseScale, WindupScale, BoltScale }.All(v => float.IsFinite(v) && v > 0)
            && AimHeight < BodyHeight && float.IsFinite(TargetAngle) && TargetAngle is > 0 and <= 180
            && MinimumDamage is >= 0 and <= 1000 && AllyVitality is > 0 and <= 1000 && LogLength is > 0 and <= 100
            && DropCapacity.Mass > 0 && DropCapacity.Space > 0, "combat tuning");
    }
}
