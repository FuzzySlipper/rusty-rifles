using Rifles.Game.Content;
using Rifles.Procgen.Generation;

namespace Rifles.Game.Generation;

internal sealed record HazardDefinition(double PeriodSeconds, double ActiveSeconds, long Damage, string Clue)
{
    internal void Validate() => GameDefinitions.Require(double.IsFinite(PeriodSeconds) && PeriodSeconds > 0
        && double.IsFinite(ActiveSeconds) && ActiveSeconds > 0 && ActiveSeconds < PeriodSeconds
        && Damage > 0 && !string.IsNullOrWhiteSpace(Clue), "generated hazard tuning");
}
internal sealed record GeneratedHazard(ulong Id, string NodeId, GridPoint Cell, double Phase, bool Disabled, bool HitThisPulse);
internal static class GeneratedHazards
{
    internal static GeneratedHazard Advance(GeneratedHazard state, HazardDefinition definition, double seconds,
        GridPoint party, Action<long> hurt)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || seconds >= definition.PeriodSeconds)
            throw new InvalidDataException("Hazard update requires one admitted sub-period step.");
        double phase = state.Phase + seconds;
        bool hit = state.HitThisPulse;
        if (phase >= definition.PeriodSeconds) { phase -= definition.PeriodSeconds; hit = false; }
        if (!state.Disabled && phase < definition.ActiveSeconds && party == state.Cell && !hit)
        { hurt(definition.Damage); hit = true; }
        return state with { Phase = phase, HitThisPulse = hit };
    }
}
