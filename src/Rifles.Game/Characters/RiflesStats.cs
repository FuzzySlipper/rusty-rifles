using Rusty.Engine.Mechanics;
using Rifles.Game.Content;
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

    /// <summary>
    /// Captures live stats for persistence with modifiers stripped. Bonuses
    /// always re-apply through their owning flows (equipment, development,
    /// conditions); serializing live modifiers would double-apply them.
    /// </summary>
    internal static StatsComponentSnapshot SnapshotForPersistence(StatsComponent live)
    {
        ArgumentNullException.ThrowIfNull(live);
        StatsComponentSnapshot captured = StatsComponentCapture.Capture(live);
        return captured with
        {
            Stats = captured.Stats.Select(stat => stat with { Modifiers = [] }).ToArray(),
        };
    }

    /// <summary>
    /// Admits a persisted stat snapshot against freshly authored stats. Bases,
    /// bounds, and alias relationships must match the authored construction
    /// exactly (saves carry values, never formulas); derived values must equal
    /// their bases (modifier-free); tracks must sit inside their maxima.
    /// </summary>
    internal static void AdmitStats(StatsComponentSnapshot saved, StatsComponent authored)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(authored);
        StatsComponentSnapshot reference = StatsComponentCapture.Capture(authored);
        GameDefinitions.Require(saved.Stats.Select(s => s.Id).ToHashSet(StringComparer.Ordinal)
                .SetEquals(reference.Stats.Select(s => s.Id).ToHashSet(StringComparer.Ordinal))
            && saved.Tracks.Select(t => t.Id).ToHashSet(StringComparer.Ordinal)
                .SetEquals(reference.Tracks.Select(t => t.Id).ToHashSet(StringComparer.Ordinal))
            && saved.StatAliases.Select(a => a.Id + "=" + a.TargetId).OrderBy(a => a, StringComparer.Ordinal)
                .SequenceEqual(reference.StatAliases.Select(a => a.Id + "=" + a.TargetId).OrderBy(a => a, StringComparer.Ordinal))
            && saved.TrackAliases.Select(a => a.Id + "=" + a.TargetId).OrderBy(a => a, StringComparer.Ordinal)
                .SequenceEqual(reference.TrackAliases.Select(a => a.Id + "=" + a.TargetId).OrderBy(a => a, StringComparer.Ordinal)),
            "saved stat composition");
        foreach (StatCapture capture in saved.Stats)
        {
            StatCapture master = reference.Stats.Single(s => s.Id == capture.Id);
            // Modifier-free by construction (stripped at capture): live value
            // re-derives from base, then owning flows re-apply sources once.
            GameDefinitions.Require(capture.BaseValue == master.BaseValue && capture.Minimum == master.Minimum
                && capture.Maximum == master.Maximum && capture.Quantum == master.Quantum
                && capture.Modifiers.Count == 0,
                "saved stat values");
        }
        foreach (TrackCapture track in saved.Tracks)
        {
            TrackCapture master = reference.Tracks.Single(t => t.Id == track.Id);
            double maximum = saved.Stats.Single(s => s.Id == track.MaximumId).Maximum;
            double masterMaximum = reference.Stats.Single(s => s.Id == master.MaximumId).Maximum;
            GameDefinitions.Require(track.MaximumId == master.MaximumId && track.Minimum == master.Minimum
                && track.Quantum == master.Quantum && maximum == masterMaximum
                && track.Current >= track.Minimum && track.Current <= maximum,
                "saved track values");
        }
    }

    /// <summary>
    /// Structural snapshot equality. Engine capture records compare their
    /// stat lists by reference, so DTO equality needs this element-wise
    /// comparison to preserve value semantics across rebuilds.
    /// </summary>
    internal static bool StatsEqual(StatsComponentSnapshot first, StatsComponentSnapshot second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return first.Stats.Count == second.Stats.Count && first.Tracks.Count == second.Tracks.Count
            && first.Stats.Zip(second.Stats).All(pair => pair.First.Id == pair.Second.Id
                && pair.First.BaseValue == pair.Second.BaseValue && pair.First.Minimum == pair.Second.Minimum
                && pair.First.Maximum == pair.Second.Maximum && pair.First.Quantum == pair.Second.Quantum
                && pair.First.Rounding == pair.Second.Rounding && pair.First.IntegerRounding == pair.Second.IntegerRounding
                && pair.First.Modifiers.Count == pair.Second.Modifiers.Count)
            && first.Tracks.Zip(second.Tracks).All(pair => pair.First.Id == pair.Second.Id
                && pair.First.MaximumId == pair.Second.MaximumId && pair.First.Current == pair.Second.Current
                && pair.First.Minimum == pair.Second.Minimum && pair.First.MaximumChangePolicy == pair.Second.MaximumChangePolicy
                && pair.First.Quantum == pair.Second.Quantum && pair.First.Rounding == pair.Second.Rounding
                && pair.First.IntegerRounding == pair.Second.IntegerRounding)
            && first.StatAliases.Select(a => a.Id + "=" + a.TargetId).OrderBy(a => a, StringComparer.Ordinal)
                .SequenceEqual(second.StatAliases.Select(a => a.Id + "=" + a.TargetId).OrderBy(a => a, StringComparer.Ordinal))
            && first.TrackAliases.Select(a => a.Id + "=" + a.TargetId).OrderBy(a => a, StringComparer.Ordinal)
                .SequenceEqual(second.TrackAliases.Select(a => a.Id + "=" + a.TargetId).OrderBy(a => a, StringComparer.Ordinal));
    }

    /// <summary>
    /// Reads one track's saved current value, or zero when the snapshot has
    /// no such track (zero-maximum tracks are omitted at construction).
    /// </summary>
    internal static double TrackCurrent(StatsComponentSnapshot saved, TrackId track)
    {
        ArgumentNullException.ThrowIfNull(saved);
        return saved.Tracks.SingleOrDefault(t => t.Id == track.Value)?.Current ?? 0;
    }

    /// <summary>
    /// Rebuilds a modifier-free admitted snapshot. Callers re-apply equipment,
    /// development, and condition sources afterwards through their own flows.
    /// </summary>
    internal static StatsComponent RebuildForRestore(StatsComponentSnapshot saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        return StatsComponentCapture.Rebuild(saved, (_, _, _) => { });
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
