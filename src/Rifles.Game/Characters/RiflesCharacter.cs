using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using Rifles.Game.Party;

namespace Rifles.Game.Characters;

internal sealed record EquipmentStatBonuses(long Power, long Defense);

/// <summary>
/// Composed character facade over one canonical Engine entity. Every member
/// reads the same attached <see cref="StatsComponent"/> live — never copies
/// or a mirrored state graph. Formation position and rank stay party-level
/// mutable state on the facade; stats, tracks, and bonuses live in the
/// component. Wrapping an entity attaches nothing.
/// </summary>
internal sealed class RiflesCharacter
{
    internal const long MaximumDerivedStatistic = 1_000_000_000_000;

    private readonly Actor actor;
    private readonly StatsComponent stats;
    private EquipmentStatBonuses equipmentBonuses = new(0, 0);
    private EquipmentStatBonuses developmentBonuses = new(0, 0);
    private StatModifierHandle? equipmentPowerModifier;
    private StatModifierHandle? equipmentDefenseModifier;
    private StatModifierHandle? developmentPowerModifier;
    private StatModifierHandle? developmentDefenseModifier;

    internal RiflesCharacter(Actor actor, MemberDefinition definition, StatsComponent stats, string position, int rank)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(stats);
        if (string.IsNullOrWhiteSpace(position)) throw new ArgumentOutOfRangeException(nameof(position));
        if (rank < 0) throw new ArgumentOutOfRangeException(nameof(rank));
        this.actor = actor;
        Definition = definition;
        this.stats = stats;
        Position = position;
        Rank = rank;
    }

    internal EntityId Entity => actor.Entity;
    internal MemberDefinition Definition { get; }
    internal string InstanceId => Definition.Id;
    internal string ArchetypeId => Definition.Archetype;
    internal StatsComponent Stats => stats;
    internal string Position { get; private set; }
    internal int Rank { get; private set; }

    internal long Vitality => stats.GetTrack(RiflesStatIds.Vitality).ValueInt64;
    internal long MaximumVitality => checked((long)stats.GetTrack(RiflesStatIds.Vitality).MaximumValue);
    internal long Resource => stats.TryGetTrack(RiflesStatIds.Resource, out Track? resource) ? resource.ValueInt64 : 0;
    internal long MaximumResource => stats.TryGetTrack(RiflesStatIds.Resource, out Track? resourceTrack)
        ? checked((long)resourceTrack.MaximumValue)
        : 0;
    internal bool IsLiving => Vitality > 0;
    internal long Power => stats.GetStat(RiflesStatIds.Power).ValueInt64;
    internal long Defense => stats.GetStat(RiflesStatIds.Defense).ValueInt64;
    internal EquipmentStatBonuses EquipmentBonuses => equipmentBonuses;

    internal long ApplyDamage(long requested)
    {
        if (requested < 0) throw new ArgumentOutOfRangeException(nameof(requested));
        return checked((long)stats.GetTrack(RiflesStatIds.Vitality).Spend(Math.Min(Vitality, requested)));
    }

    internal long Heal(long requested)
    {
        if (requested < 0) throw new ArgumentOutOfRangeException(nameof(requested));
        return checked((long)stats.GetTrack(RiflesStatIds.Vitality).Restore(requested));
    }

    internal long SpendResource(long requested)
    {
        if (requested < 0) throw new ArgumentOutOfRangeException(nameof(requested));
        if (!stats.TryGetTrack(RiflesStatIds.Resource, out Track? resource)) return 0;
        return checked((long)resource.Spend(Math.Min(Resource, requested)));
    }

    internal long RecoverResource(long requested)
    {
        if (requested < 0) throw new ArgumentOutOfRangeException(nameof(requested));
        return stats.TryGetTrack(RiflesStatIds.Resource, out Track? resource)
            ? checked((long)resource.Restore(requested))
            : 0;
    }

    /// <summary>Receives the current aggregate from authoritative equipped items.</summary>
    internal void SetEquipmentBonuses(long power, long defense)
    {
        ValidateDerivedBonus(power, nameof(power));
        ValidateDerivedBonus(defense, nameof(defense));
        ReplaceModifier(stats.GetStat(RiflesStatIds.Power), ref equipmentPowerModifier, power);
        ReplaceModifier(stats.GetStat(RiflesStatIds.Defense), ref equipmentDefenseModifier, defense);
        equipmentBonuses = new EquipmentStatBonuses(power, defense);
    }

    internal void SetDevelopmentBonuses(long power, long defense)
    {
        ValidateDerivedBonus(power, nameof(power));
        ValidateDerivedBonus(defense, nameof(defense));
        ReplaceModifier(stats.GetStat(RiflesStatIds.Power), ref developmentPowerModifier, power);
        ReplaceModifier(stats.GetStat(RiflesStatIds.Defense), ref developmentDefenseModifier, defense);
        developmentBonuses = new(power, defense);
    }

    internal void SetPosition(string position, int rank)
    {
        if (string.IsNullOrWhiteSpace(position)) throw new ArgumentOutOfRangeException(nameof(position));
        if (rank < 0) throw new ArgumentOutOfRangeException(nameof(rank));
        Position = position;
        Rank = rank;
    }

    private static void ReplaceModifier(Stat statistic, ref StatModifierHandle? existing, long value)
    {
        if (existing is not null) statistic.RemoveModifier(existing);
        existing = value == 0 ? null : statistic.AddModifier(value, StatModifierKind.Add);
    }

    private static void ValidateDerivedBonus(long value, string parameter)
    {
        if (value < -MaximumDerivedStatistic || value > MaximumDerivedStatistic)
            throw new ArgumentOutOfRangeException(parameter, value, "Derived statistic bonuses must remain within product bounds.");
    }
}
