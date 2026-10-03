using Rifles.Game.Content;

namespace Rifles.Game.Combat;

internal sealed record CompanyCombatDefinition(double EnemyStepTimeScale, float MeleeRange)
{
    internal void Validate()
    {
        GameDefinitions.Require(double.IsFinite(EnemyStepTimeScale) && EnemyStepTimeScale > 0
            && float.IsFinite(MeleeRange) && MeleeRange > 0, "company enemy movement and melee reach");
    }
}
