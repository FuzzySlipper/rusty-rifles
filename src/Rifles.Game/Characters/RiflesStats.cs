using Rusty.Engine.Mechanics;
using Rifles.Game.Party;

namespace Rifles.Game.Characters;

/// <summary>
/// Builds the canonical <see cref="StatsComponent"/> for one character from
/// admitted values. Tracks share their maximum <see cref="Stat"/> references,
/// so later maximum changes reach the same track. Formulas, rounding, and
/// bounds match the previous private-stat behavior exactly.
/// </summary>
internal static class RiflesStats
{
    internal static StatsComponent ForMember(MemberDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        StatsComponent stats = new();
        stats.AddStat(RiflesStatIds.Power, Derived(definition.BasePower));
        stats.AddStat(RiflesStatIds.Defense, Derived(definition.BaseDefense));
        stats.AddStat(RiflesStatIds.Speed, new Stat(1, minimum: 0, maximum: 1));
        // Stat rounding stays at the Engine AwayFromZero default, matching the
        // previous private-stat construction which passed integerRounding only.
        Stat vitalityMax = new(definition.MaximumVitality, minimum: 0, maximum: RiflesCharacter.MaximumDerivedStatistic,
            quantum: 1, integerRounding: MidpointRounding.ToZero);
        stats.AddStat(RiflesStatIds.VitalityMax, vitalityMax);
        stats.AddTrack(RiflesStatIds.Vitality, VitalityTrack(vitalityMax, definition.InitialVitality));
        if (definition.MaximumResource > 0)
        {
            Stat resourceMax = new(definition.MaximumResource, minimum: 0, maximum: RiflesCharacter.MaximumDerivedStatistic,
                quantum: 1, integerRounding: MidpointRounding.ToZero);
            stats.AddStat(RiflesStatIds.ResourceMax, resourceMax);
            stats.AddTrack(RiflesStatIds.Resource, VitalityTrack(resourceMax, definition.InitialResource));
        }

        return stats;
    }

    internal static StatsComponent ForVitality(long maximumVitality, long initialVitality, long maximumResource, long initialResource)
    {
        if (maximumVitality <= 0 || maximumResource < 0) throw new ArgumentOutOfRangeException(nameof(maximumVitality));
        StatsComponent stats = new();
        stats.AddStat(RiflesStatIds.Speed, new Stat(1, minimum: 0, maximum: 1));
        Stat vitalityMax = new(maximumVitality, minimum: 0, maximum: RiflesCharacter.MaximumDerivedStatistic,
            quantum: 1, integerRounding: MidpointRounding.ToZero);
        stats.AddStat(RiflesStatIds.VitalityMax, vitalityMax);
        stats.AddTrack(RiflesStatIds.Vitality, VitalityTrack(vitalityMax, initialVitality));
        if (maximumResource > 0)
        {
            Stat resourceMax = new(maximumResource, minimum: 0, maximum: RiflesCharacter.MaximumDerivedStatistic,
                quantum: 1, integerRounding: MidpointRounding.ToZero);
            stats.AddStat(RiflesStatIds.ResourceMax, resourceMax);
            stats.AddTrack(RiflesStatIds.Resource, VitalityTrack(resourceMax, initialResource));
        }

        return stats;
    }

    private static Stat Derived(long baseValue) => new(
        baseValue,
        minimum: 0,
        maximum: RiflesCharacter.MaximumDerivedStatistic,
        quantum: 1,
        integerRounding: MidpointRounding.ToZero);

    private static Track VitalityTrack(Stat maximum, long initial) => new(
        maximum,
        initial,
        quantum: 1,
        rounding: MidpointRounding.ToZero,
        integerRounding: MidpointRounding.ToZero);
}
