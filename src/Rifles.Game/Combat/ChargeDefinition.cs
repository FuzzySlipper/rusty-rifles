using Rifles.Game.Content;

namespace Rifles.Game.Combat;

/// <summary>Authored policy for the party's one straight forward charge.</summary>
internal sealed record ChargeDefinition(int MaximumCells, double StepSeconds, long BonusDamage, double RecoverySeconds)
{
    internal void Validate()
    {
        GameDefinitions.Require(MaximumCells is > 0 and <= 8 && double.IsFinite(StepSeconds) && StepSeconds > 0
            && BonusDamage is >= 0 and <= 1000 && double.IsFinite(RecoverySeconds) && RecoverySeconds > 0, "charge tuning");
    }
}
