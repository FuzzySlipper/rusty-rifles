using Rusty.Engine.Mechanics;

namespace Rifles.Game.Party;

internal enum FormationSlot { FrontLeft, FrontRight, RearLeft, RearRight }
internal enum PartyReach { Melee, Ranged, Casting }

internal sealed record MemberDefinition(
    string Id,
    string Name,
    FormationSlot Slot,
    long MaximumVitality,
    long BasePower = 0,
    long BaseDefense = 0,
    long MaximumResource = 0,
    long? StartingVitality = null,
    long? StartingResource = null)
{
    internal long InitialVitality => StartingVitality ?? MaximumVitality;
    internal long InitialResource => StartingResource ?? MaximumResource;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name)
            || !Enum.IsDefined(Slot) || MaximumVitality <= 0 || MaximumResource < 0
            || MaximumVitality > ExactValue.MaximumAbsolute || MaximumResource > ExactValue.MaximumAbsolute
            || BasePower < 0 || BaseDefense < 0
            || BasePower > ExactValue.MaximumAbsolute || BaseDefense > ExactValue.MaximumAbsolute
            || StartingVitality is long startingVitality && (startingVitality < 0 || startingVitality > MaximumVitality)
            || StartingResource is long startingResource && (startingResource < 0 || startingResource > MaximumResource))
        {
            throw new InvalidDataException($"Invalid member definition '{Id}'.");
        }
    }
}

/// <summary>One authored, complete starter party. Loadout ownership stays with Inventory.</summary>
internal sealed record StarterPartyPresetDefinition(string Id, string Name, MemberDefinition[] Members)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name)
            || Members is not { Length: PartySize })
        {
            throw new InvalidDataException($"Invalid starter party preset '{Id}'.");
        }

        foreach (MemberDefinition member in Members)
        {
            if (member is null) throw new InvalidDataException($"Starter party preset '{Id}' has a null member.");
            member.Validate();
        }

        if (Members.Select(member => member.Id).Distinct(StringComparer.Ordinal).Count() != PartySize
            || Members.Select(member => member.Slot).Distinct().Count() != PartySize)
        {
            throw new InvalidDataException($"Starter party preset '{Id}' must contain four distinct members and formation slots.");
        }
    }

    private const int PartySize = 4;
}

/// <summary>Authored starter choices; a preset is chosen only when an expedition begins.</summary>
internal sealed record CharacterOptionsDefinition(string DefaultPresetId, StarterPartyPresetDefinition[] Presets)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(DefaultPresetId) || Presets is not { Length: > 0 })
        {
            throw new InvalidDataException("Character options need a default preset and at least one preset.");
        }

        foreach (StarterPartyPresetDefinition preset in Presets)
        {
            if (preset is null) throw new InvalidDataException("Character options contain a null preset.");
            preset.Validate();
        }

        if (Presets.Select(preset => preset.Id).Distinct(StringComparer.Ordinal).Count() != Presets.Length
            || !Presets.Any(preset => preset.Id == DefaultPresetId))
        {
            throw new InvalidDataException("Character option preset ids must be unique and include the default.");
        }

        string[] expectedMemberIds = Presets[0].Members.Select(member => member.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (Presets.Skip(1).Any(preset => !preset.Members.Select(member => member.Id)
            .OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(expectedMemberIds, StringComparer.Ordinal)))
        {
            throw new InvalidDataException("Each starter preset must retain the same stable member ids.");
        }
    }

    internal StarterPartyPresetDefinition GetPreset(string id) => Presets.SingleOrDefault(preset => preset.Id == id)
        ?? throw new InvalidOperationException($"Unknown starter party preset '{id}'.");
}

/// <summary>
/// Saved party-member state. Nullable fields admit pre-formation/pre-resource
/// snapshots; all newly captured snapshots provide both values.
/// </summary>
internal sealed record MemberSnapshot(string Id, long Vitality, FormationSlot? Slot = null, long? Resource = null);

internal sealed record EquipmentStatBonuses(long Power, long Defense);

internal sealed class PartyMemberState
{
    private const long MaximumDerivedStatistic = ExactValue.MaximumAbsolute;
    private static readonly StatId PowerId = StatId.Parse("rifles.power");
    private static readonly StatId DefenseId = StatId.Parse("rifles.defense");
    private static readonly TrackId VitalityId = TrackId.Parse("rifles.vitality");
    private static readonly TrackId ResourceId = TrackId.Parse("rifles.resource");
    private static readonly ExactStatDefinition PowerDefinition = CreateStatDefinition(PowerId);
    private static readonly ExactStatDefinition DefenseDefinition = CreateStatDefinition(DefenseId);
    private readonly ExactTrack vitality;
    private readonly ExactTrack? resource;
    private EquipmentStatBonuses equipmentBonuses = new(0, 0);
    private EquipmentStatBonuses developmentBonuses = new(0, 0);

    internal PartyMemberState(MemberDefinition definition)
    {
        definition.Validate();
        Definition = definition;
        Slot = definition.Slot;
        vitality = new ExactTrack(new ExactTrackDefinition(VitalityId, ExactValue.Zero,
            new ExactTrackMaximum.Fixed(new ExactValue(definition.MaximumVitality))), new ExactValue(definition.InitialVitality));
        if (definition.MaximumResource > 0)
        {
            resource = new ExactTrack(new ExactTrackDefinition(ResourceId, ExactValue.Zero,
                new ExactTrackMaximum.Fixed(new ExactValue(definition.MaximumResource))), new ExactValue(definition.InitialResource));
        }
    }

    internal MemberDefinition Definition { get; }
    internal FormationSlot Slot { get; private set; }
    internal long Vitality => vitality.Current.Raw;
    internal long MaximumVitality => vitality.Bounds.Maximum.Raw;
    internal long Resource => resource?.Current.Raw ?? 0;
    internal long MaximumResource => resource?.Bounds.Maximum.Raw ?? 0;
    internal bool IsLiving => Vitality > 0;
    internal long Power => Evaluate(PowerDefinition, Definition.BasePower, equipmentBonuses.Power, developmentBonuses.Power).Value.Raw;
    internal long Defense => Evaluate(DefenseDefinition, Definition.BaseDefense, equipmentBonuses.Defense, developmentBonuses.Defense).Value.Raw;
    internal EquipmentStatBonuses EquipmentBonuses => equipmentBonuses;

    internal long ApplyDamage(long requested)
    {
        if (requested < 0) throw new ArgumentOutOfRangeException(nameof(requested));
        long applied = Math.Min(Vitality, requested);
        vitality.Spend(new ExactValue(applied));
        return applied;
    }

    internal long Heal(long requested)
    {
        if (requested < 0) throw new ArgumentOutOfRangeException(nameof(requested));
        return vitality.Restore(new ExactValue(requested)).AppliedAmount.Raw;
    }

    internal long SpendResource(long requested)
    {
        if (requested < 0) throw new ArgumentOutOfRangeException(nameof(requested));
        if (resource is null) return 0;
        long applied = Math.Min(Resource, requested);
        resource.Spend(new ExactValue(applied));
        return applied;
    }

    internal long RecoverResource(long requested)
    {
        if (requested < 0) throw new ArgumentOutOfRangeException(nameof(requested));
        return resource?.Restore(new ExactValue(requested)).AppliedAmount.Raw ?? 0;
    }

    /// <summary>Receives the current aggregate from authoritative equipped items.</summary>
    internal void SetEquipmentBonuses(long power, long defense)
    {
        // Evaluate both prospective values before replacing the aggregate so an
        // invalid aggregate cannot leave one displayed statistic updated.
        _ = Evaluate(PowerDefinition, Definition.BasePower, power);
        _ = Evaluate(DefenseDefinition, Definition.BaseDefense, defense);
        equipmentBonuses = new EquipmentStatBonuses(power, defense);
    }

    internal void SetDevelopmentBonuses(long power, long defense)
    {
        _ = Evaluate(PowerDefinition, Definition.BasePower, equipmentBonuses.Power, power);
        _ = Evaluate(DefenseDefinition, Definition.BaseDefense, equipmentBonuses.Defense, defense);
        developmentBonuses = new(power, defense);
    }

    internal void SetSlot(FormationSlot slot)
    {
        if (!Enum.IsDefined(slot)) throw new ArgumentOutOfRangeException(nameof(slot));
        Slot = slot;
    }

    private static ExactStatDefinition CreateStatDefinition(StatId id) => new(
        id, ExactValue.Zero, new ExactValue(MaximumDerivedStatistic));

    private static ExactStatEvaluation Evaluate(ExactStatDefinition definition, long baseValue, long equipmentBonus, long developmentBonus = 0)
    {
        List<ExactSource> sources = [];
        void Add(string name, long bonus)
        {
            if (bonus == 0) return;
            sources.Add(new ExactSource(new IntrinsicSourceIdentity(null, SourceInstanceId.Parse(name)),
                SourceDefinitionId.Parse(name), 0,
                [new ExactStatContributionDefinition(definition.Id, StackingGroupId.Parse(name + "." + definition.Id.Value),
                    MechanicsStackingPolicy.Sum, new ExactStatContribution.Add(new ExactValue(bonus)))]));
        }
        Add("rifles.equipment", equipmentBonus);
        Add("rifles.development", developmentBonus);
        return ExactStatEvaluator.Evaluate(definition, new ExactValue(baseValue), sources);
    }
}

internal sealed class PartyState
{
    private readonly MemberDefinition[] roster;
    private PartyMemberState[] members;

    internal PartyState(IReadOnlyList<MemberDefinition> definitions)
        : this(null, definitions)
    {
    }

    internal PartyState(StarterPartyPresetDefinition preset)
        : this(GetPresetId(preset), GetPresetMembers(preset))
    {
        preset.Validate();
    }

    private PartyState(string? presetId, IReadOnlyList<MemberDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        roster = definitions.ToArray();
        ValidateRoster(roster);
        PresetId = presetId;
        members = roster.Select(definition => new PartyMemberState(definition)).ToArray();
    }

    private static string GetPresetId(StarterPartyPresetDefinition? preset) => preset?.Id
        ?? throw new ArgumentNullException(nameof(preset));

    private static IReadOnlyList<MemberDefinition> GetPresetMembers(StarterPartyPresetDefinition? preset) => preset?.Members
        ?? throw new ArgumentNullException(nameof(preset));

    /// <summary>Set for a starter-preset party and immutable for its expedition.</summary>
    internal string? PresetId { get; }
    internal IReadOnlyList<PartyMemberState> Members => Array.AsReadOnly(members);
    internal IReadOnlyList<MemberSnapshot> Capture() => members
        .Select(member => new MemberSnapshot(member.Definition.Id, member.Vitality, member.Slot, member.Resource)).ToArray();

    internal bool SwapFormation(string memberId, string otherMemberId)
    {
        PartyMemberState? member = members.SingleOrDefault(candidate => candidate.Definition.Id == memberId);
        PartyMemberState? other = members.SingleOrDefault(candidate => candidate.Definition.Id == otherMemberId);
        if (member is null || other is null || ReferenceEquals(member, other) || !member.IsLiving || !other.IsLiving)
        {
            return false;
        }

        FormationSlot memberSlot = member.Slot;
        member.SetSlot(other.Slot);
        other.SetSlot(memberSlot);
        return true;
    }

    internal bool CanUseReach(string memberId, PartyReach reach)
    {
        PartyMemberState? member = members.SingleOrDefault(candidate => candidate.Definition.Id == memberId);
        return member is not null && member.IsLiving && IsReachAllowed(member.Slot, reach);
    }

    internal IReadOnlyList<PartyMemberState> EligibleMembers(PartyReach reach) => members
        .Where(member => member.IsLiving && IsReachAllowed(member.Slot, reach)).ToArray();

    internal void Restore(IReadOnlyList<MemberSnapshot> saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (saved.Count != roster.Length || saved.Any(snapshot => snapshot is null)
            || saved.Select(snapshot => snapshot.Id).Distinct(StringComparer.Ordinal).Count() != saved.Count)
        {
            throw new InvalidOperationException("Party snapshot roster mismatch.");
        }

        PartyMemberState[] restored = roster.Select(definition =>
        {
            MemberSnapshot value = saved.SingleOrDefault(snapshot => snapshot.Id == definition.Id)
                ?? throw new InvalidOperationException("Party snapshot member missing.");
            FormationSlot slot = value.Slot ?? definition.Slot;
            long resourceValue = value.Resource ?? definition.MaximumResource;
            if (value.Vitality < 0 || value.Vitality > definition.MaximumVitality
                || resourceValue < 0 || resourceValue > definition.MaximumResource || !Enum.IsDefined(slot))
            {
                throw new InvalidOperationException("Party snapshot member state is out of range.");
            }

            PartyMemberState member = new(definition);
            member.Heal(definition.MaximumVitality);
            member.ApplyDamage(definition.MaximumVitality - value.Vitality);
            member.RecoverResource(definition.MaximumResource);
            member.SpendResource(definition.MaximumResource - resourceValue);
            member.SetSlot(slot);
            return member;
        }).ToArray();
        if (restored.Select(member => member.Slot).Distinct().Count() != restored.Length)
        {
            throw new InvalidOperationException("Party snapshot formation slots must be distinct.");
        }

        members = restored;
    }

    private static bool IsReachAllowed(FormationSlot slot, PartyReach reach) => reach switch
    {
        PartyReach.Melee => slot is FormationSlot.FrontLeft or FormationSlot.FrontRight,
        PartyReach.Ranged or PartyReach.Casting => true,
        _ => throw new ArgumentOutOfRangeException(nameof(reach)),
    };

    private static void ValidateRoster(IReadOnlyList<MemberDefinition> definitions)
    {
        if (definitions.Count != 4)
        {
            throw new InvalidDataException("A party needs exactly four members.");
        }

        foreach (MemberDefinition definition in definitions)
        {
            if (definition is null) throw new InvalidDataException("A party cannot contain a null member.");
            definition.Validate();
        }

        if (definitions.Select(definition => definition.Id).Distinct(StringComparer.Ordinal).Count() != definitions.Count
            || definitions.Select(definition => definition.Slot).Distinct().Count() != definitions.Count)
        {
            throw new InvalidDataException("Party members and formation slots must be distinct.");
        }
    }
}
