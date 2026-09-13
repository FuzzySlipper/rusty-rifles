using Rifles.Game.Content;
using Rifles.Game.Items;

namespace Rifles.Game.Combat;

internal sealed record ActionDefinition(CombatActionKind Kind, double Windup, double Recovery, float Range, long Damage, float Speed, long ResourceCost);
internal sealed record EnemyDefinition(string Id, string Name, CombatActionKind Attack, long Vitality, long Defense,
    double StepSeconds, double DecisionSeconds, float AwarenessRange, int SpawnDistance, float Scale, StartingItem[] Loot);
internal sealed record CombatDefinition(ActionDefinition[] Actions, EnemyDefinition[] Enemies, float BodyWidth,
    float BodyHeight, float AimHeight, float TargetAngle, long MinimumDamage, bool FriendlyFire, long AllyVitality,
    float CorpseScale, float WindupScale, float BoltScale, int LogLength, string AmmunitionItem,
    PackDefinition DropCapacity, bool RecoverThrownItems, float[] BoltColor)
{
    internal ActionDefinition Action(CombatActionKind kind) => Actions.Single(a => a.Kind == kind);
    internal EnemyDefinition Enemy(string id) => Enemies.Single(e => e.Id == id);
    internal void Validate()
    {
        GameDefinitions.Require(Actions.Length == Enum.GetValues<CombatActionKind>().Length
            && Actions.Select(a => a.Kind).Distinct().Count() == Actions.Length, "combat actions");
        foreach (ActionDefinition action in Actions)
            GameDefinitions.Require(Enum.IsDefined(action.Kind) && double.IsFinite(action.Windup) && action.Windup > 0
                && double.IsFinite(action.Recovery) && action.Recovery > 0 && float.IsFinite(action.Range) && action.Range > 0
                && action.Damage >= 0 && action.Damage <= 1000 && float.IsFinite(action.Speed) && action.Speed > 0
                && action.ResourceCost >= 0 && action.ResourceCost <= 1000, "combat action " + action.Kind);
        GameDefinitions.Require(Enemies.Length > 0 && Enemies.Select(e => e.Id).Distinct().Count() == Enemies.Length, "enemy ids");
        foreach (EnemyDefinition enemy in Enemies)
            GameDefinitions.Require(!string.IsNullOrWhiteSpace(enemy.Id) && !string.IsNullOrWhiteSpace(enemy.Name)
                && enemy.Attack is CombatActionKind.Melee or CombatActionKind.Fire && enemy.Vitality is > 0 and <= 1000
                && enemy.Defense is >= 0 and <= 1000 && double.IsFinite(enemy.StepSeconds) && enemy.StepSeconds > 0
                && double.IsFinite(enemy.DecisionSeconds) && enemy.DecisionSeconds > 0 && float.IsFinite(enemy.AwarenessRange)
                && enemy.AwarenessRange > 0 && enemy.SpawnDistance > 0 && float.IsFinite(enemy.Scale) && enemy.Scale > 0
                && enemy.Loot.All(l => l.Quantity > 0 && !l.Equipped), "enemy " + enemy.Id);
        GameDefinitions.Require(BoltColor.Length == 4 && BoltColor.All(v => float.IsFinite(v) && v >= 0 && v <= 1), "bolt color");
        GameDefinitions.Require(new[] { BodyWidth, BodyHeight, AimHeight, CorpseScale, WindupScale, BoltScale }.All(v => float.IsFinite(v) && v > 0)
            && AimHeight < BodyHeight && float.IsFinite(TargetAngle) && TargetAngle is > 0 and <= 180
            && MinimumDamage is >= 0 and <= 1000 && AllyVitality is > 0 and <= 1000 && LogLength is > 0 and <= 100
            && DropCapacity.Mass > 0 && DropCapacity.Space > 0, "combat tuning");
    }
}
