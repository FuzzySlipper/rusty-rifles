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
    private readonly Dictionary<string, (EquipmentStatBonuses Bonuses, StatModifierHandle? Power, StatModifierHandle? Defense)> equipmentSources = new(StringComparer.Ordinal);
    private EquipmentStatBonuses developmentBonuses = new(0, 0);
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
    internal EquipmentStatBonuses EquipmentBonuses => equipmentSources.Values
        .Aggregate(new EquipmentStatBonuses(0, 0),
            (total, source) => new EquipmentStatBonuses(total.Power + source.Bonuses.Power, total.Defense + source.Bonuses.Defense));

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

    /// <summary>
    /// Attaches one equipped item's contribution, preserving per-source
    /// identity so unequipping removes only its own contribution. Replaces
    /// any previous contribution from the same item.
    /// </summary>
    internal void EquipContribution(string item, long power, long defense)
    {
        ValidateDerivedBonus(power, nameof(power));
        ValidateDerivedBonus(defense, nameof(defense));
        UnequipContribution(item);
        StatModifierHandle? powerHandle = power == 0 ? null : stats.GetStat(RiflesStatIds.Power).AddModifier(power, StatModifierKind.Add);
        StatModifierHandle? defenseHandle = defense == 0 ? null : stats.GetStat(RiflesStatIds.Defense).AddModifier(defense, StatModifierKind.Add);
        equipmentSources.Add(item, (new EquipmentStatBonuses(power, defense), powerHandle, defenseHandle));
    }

    /// <summary>Removes one item's contribution; unknown items are ignored.</summary>
    internal void UnequipContribution(string item)
    {
        if (!equipmentSources.Remove(item, out var source)) return;
        if (source.Power is not null) stats.GetStat(RiflesStatIds.Power).RemoveModifier(source.Power);
        if (source.Defense is not null) stats.GetStat(RiflesStatIds.Defense).RemoveModifier(source.Defense);
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
