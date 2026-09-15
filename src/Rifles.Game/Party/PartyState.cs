using Rusty.Engine.Mechanics;

namespace Rifles.Game.Party;

internal enum PartyReach { Melee, Ranged, Casting }

/// <summary>
/// One authored formation position. Rank 0 is the front rank; higher ranks
/// sit behind it. Reach and damage order derive from rank, never from roster
/// order or array index. Offsets are formation-local cell fractions —
/// forward toward the party facing, left from the party's point of view —
/// rotated into the world by the facing at use time, so the same authored
/// layout reads correctly no matter which way the party faces.
/// </summary>
internal sealed record FormationPositionDefinition(string Id, string Name, int Rank, float OffsetForward = 0, float OffsetLeft = 0)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) || Rank < 0
            || !float.IsFinite(OffsetForward) || !float.IsFinite(OffsetLeft)
            || Math.Abs(OffsetForward) > 0.5f || Math.Abs(OffsetLeft) > 0.5f)
        {
            throw new InvalidDataException($"Invalid formation position '{Id}'.");
        }
    }

    /// <summary>
    /// Picks the living member first encountered by a ray in formation-local
    /// direction, or null when the direction is degenerate. Ties break by
    /// position id, then member id, so frontal attacks keep the old
    /// front-left-first order deterministically.
    /// </summary>
    internal static string? FirstEncountered(float directionForward, float directionLeft,
        IEnumerable<(string Id, string Position, float Forward, float Left, bool Living)> members)
    {
        float length = MathF.Sqrt(directionForward * directionForward + directionLeft * directionLeft);
        if (!float.IsFinite(length) || !(length > 0)) return null;
        float forward = directionForward / length, left = directionLeft / length;
        string? best = null;
        float bestAlong = 0;
        string bestPosition = "", bestId = "";
        // When no living member's projection differs (e.g. content without
        // discriminating offsets), geometry says nothing: return null so the
        // caller keeps rank order instead of silently switching to id order.
        bool discriminates = false, haveLiving = false;
        float firstAlong = 0;
        foreach ((string id, string position, float memberForward, float memberLeft, bool living) in members)
        {
            if (!living) continue;
            float along = memberForward * forward + memberLeft * left;
            if (!haveLiving) { haveLiving = true; firstAlong = along; }
            else if (along != firstAlong) discriminates = true;
            if (best is not null && (along > bestAlong
                || (along == bestAlong && (string.Compare(position, bestPosition, StringComparison.Ordinal) > 0
                    || (position == bestPosition && string.Compare(id, bestId, StringComparison.Ordinal) > 0)))))
            {
                continue;
            }

            best = id;
            bestAlong = along;
            bestPosition = position;
            bestId = id;
        }

        return best is not null && discriminates ? best : null;
    }
}

internal sealed record MemberDefinition(
    string Id,
    string Name,
    string Position,
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
            || string.IsNullOrWhiteSpace(Position) || MaximumVitality <= 0 || MaximumResource < 0
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

/// <summary>One authored starter party. Loadout ownership stays with Inventory.</summary>
internal sealed record StarterPartyPresetDefinition(string Id, string Name, MemberDefinition[] Members)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name)
            || Members is not { Length: > 0 })
        {
            throw new InvalidDataException($"Invalid starter party preset '{Id}'.");
        }

        foreach (MemberDefinition member in Members)
        {
            if (member is null) throw new InvalidDataException($"Starter party preset '{Id}' has a null member.");
            member.Validate();
        }

        if (Members.Select(member => member.Id).Distinct(StringComparer.Ordinal).Count() != Members.Length
            || Members.Select(member => member.Position).Distinct(StringComparer.Ordinal).Count() != Members.Length)
        {
            throw new InvalidDataException($"Starter party preset '{Id}' must contain distinct members and formation positions.");
        }
    }
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
internal sealed record MemberSnapshot(string Id, long Vitality, string? Position = null, long? Resource = null);

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

    internal PartyMemberState(MemberDefinition definition, int rank)
    {
        definition.Validate();
        if (rank < 0) throw new ArgumentOutOfRangeException(nameof(rank));
        Definition = definition;
        Position = definition.Position;
        Rank = rank;
        vitality = new ExactTrack(new ExactTrackDefinition(VitalityId, ExactValue.Zero,
            new ExactTrackMaximum.Fixed(new ExactValue(definition.MaximumVitality))), new ExactValue(definition.InitialVitality));
        if (definition.MaximumResource > 0)
        {
            resource = new ExactTrack(new ExactTrackDefinition(ResourceId, ExactValue.Zero,
                new ExactTrackMaximum.Fixed(new ExactValue(definition.MaximumResource))), new ExactValue(definition.InitialResource));
        }
    }

    internal MemberDefinition Definition { get; }
    internal string Position { get; private set; }
    internal int Rank { get; private set; }
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

    internal void SetPosition(string position, int rank)
    {
        if (string.IsNullOrWhiteSpace(position)) throw new ArgumentOutOfRangeException(nameof(position));
        if (rank < 0) throw new ArgumentOutOfRangeException(nameof(rank));
        Position = position;
        Rank = rank;
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
    private const int FrontRank = 0;
    private readonly MemberDefinition[] roster;
    private readonly Dictionary<string, FormationPositionDefinition> positions;
    private readonly int maxSize;
    private PartyMemberState[] members;

    internal PartyState(IReadOnlyList<FormationPositionDefinition> positions, int maxSize, IReadOnlyList<MemberDefinition> definitions)
        : this(null, positions, maxSize, definitions)
    {
    }

    internal PartyState(IReadOnlyList<FormationPositionDefinition> positions, int maxSize, StarterPartyPresetDefinition preset)
        : this(GetPresetId(preset), positions, maxSize, GetPresetMembers(preset))
    {
        preset.Validate();
    }

    private PartyState(string? presetId, IReadOnlyList<FormationPositionDefinition> positions, int maxSize, IReadOnlyList<MemberDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(definitions);
        this.positions = ValidatePositions(positions);
        if (maxSize < 1) throw new InvalidDataException("Party capacity must be positive.");
        this.maxSize = maxSize;
        roster = definitions.ToArray();
        ValidateRoster(roster);
        PresetId = presetId;
        members = roster.Select(definition => new PartyMemberState(definition, RankOf(definition.Position))).ToArray();
    }

    private static string GetPresetId(StarterPartyPresetDefinition? preset) => preset?.Id
        ?? throw new ArgumentNullException(nameof(preset));

    private static IReadOnlyList<MemberDefinition> GetPresetMembers(StarterPartyPresetDefinition? preset) => preset?.Members
        ?? throw new ArgumentNullException(nameof(preset));

    /// <summary>Set for a starter-preset party and immutable for its expedition.</summary>
    internal string? PresetId { get; }
    internal IReadOnlyList<PartyMemberState> Members => Array.AsReadOnly(members);
    internal IReadOnlyList<FormationPositionDefinition> Positions => positions.Values.OrderBy(p => p.Rank).ThenBy(p => p.Id, StringComparer.Ordinal).ToArray();
    internal IReadOnlyList<MemberSnapshot> Capture() => members
        .Select(member => new MemberSnapshot(member.Definition.Id, member.Vitality, member.Position, member.Resource)).ToArray();

    internal bool SwapFormation(string memberId, string otherMemberId)
    {
        PartyMemberState? member = members.SingleOrDefault(candidate => candidate.Definition.Id == memberId);
        PartyMemberState? other = members.SingleOrDefault(candidate => candidate.Definition.Id == otherMemberId);
        if (member is null || other is null || ReferenceEquals(member, other) || !member.IsLiving || !other.IsLiving)
        {
            return false;
        }

        (string position, int rank) = (member.Position, member.Rank);
        member.SetPosition(other.Position, other.Rank);
        other.SetPosition(position, rank);
        return true;
    }

    internal bool MoveFormation(string memberId, string position)
    {
        PartyMemberState? member = members.SingleOrDefault(candidate => candidate.Definition.Id == memberId);
        if (member is null || !member.IsLiving || string.IsNullOrEmpty(position)
            || !positions.TryGetValue(position, out FormationPositionDefinition? target))
        {
            return false;
        }

        if (members.Any(candidate => !ReferenceEquals(candidate, member) && candidate.Position == position))
        {
            return false;
        }

        member.SetPosition(target.Id, target.Rank);
        return true;
    }

    internal bool CanUseReach(string memberId, PartyReach reach)
    {
        PartyMemberState? member = members.SingleOrDefault(candidate => candidate.Definition.Id == memberId);
        return member is not null && member.IsLiving && IsReachAllowed(member.Rank, reach);
    }

    internal FormationPositionDefinition PositionOf(string memberId)
    {
        PartyMemberState? member = members.SingleOrDefault(candidate => candidate.Definition.Id == memberId);
        return member is not null && positions.TryGetValue(member.Position, out FormationPositionDefinition? position)
            ? position
            : throw new InvalidDataException($"Unknown party member '{memberId}'.");
    }

    internal IReadOnlyList<PartyMemberState> EligibleMembers(PartyReach reach) => members
        .Where(member => member.IsLiving && IsReachAllowed(member.Rank, reach)).ToArray();

    internal void Restore(IReadOnlyList<MemberSnapshot> saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        // Roster membership is exact: a shorter snapshot would silently drop a
        // member while their inventory pack survives, corrupting the next save.
        // Size changes happen by rebuilding the roster, never by shrinking it
        // here. See F2.
        if (saved.Count != roster.Length || saved.Any(snapshot => snapshot is null)
            || saved.Select(snapshot => snapshot.Id).Distinct(StringComparer.Ordinal).Count() != saved.Count)
        {
            throw new InvalidOperationException("Party snapshot roster mismatch.");
        }

        PartyMemberState[] restored = saved.Select(value =>
        {
            MemberDefinition definition = roster.SingleOrDefault(candidate => candidate.Id == value.Id)
                ?? throw new InvalidOperationException("Party snapshot member missing.");
            string position = value.Position ?? definition.Position;
            if (!positions.TryGetValue(position, out FormationPositionDefinition? target))
            {
                throw new InvalidOperationException("Party snapshot formation position is unknown.");
            }

            long resourceValue = value.Resource ?? definition.MaximumResource;
            if (value.Vitality < 0 || value.Vitality > definition.MaximumVitality
                || resourceValue < 0 || resourceValue > definition.MaximumResource)
            {
                throw new InvalidOperationException("Party snapshot member state is out of range.");
            }

            PartyMemberState member = new(definition, target.Rank);
            member.Heal(definition.MaximumVitality);
            member.ApplyDamage(definition.MaximumVitality - value.Vitality);
            member.RecoverResource(definition.MaximumResource);
            member.SpendResource(definition.MaximumResource - resourceValue);
            member.SetPosition(target.Id, target.Rank);
            return member;
        }).ToArray();
        if (restored.Length < 1 || restored.Length > maxSize
            || restored.Select(member => member.Position).Distinct(StringComparer.Ordinal).Count() != restored.Length)
        {
            throw new InvalidOperationException("Party snapshot formation positions must be distinct and within capacity.");
        }

        members = restored;
    }

    private int RankOf(string position) => positions.TryGetValue(position, out FormationPositionDefinition? target)
        ? target.Rank
        : throw new InvalidDataException($"Unknown formation position '{position}'.");

    private static bool IsReachAllowed(int rank, PartyReach reach) => reach switch
    {
        PartyReach.Melee => rank == FrontRank,
        PartyReach.Ranged or PartyReach.Casting => true,
        _ => throw new ArgumentOutOfRangeException(nameof(reach)),
    };

    private static Dictionary<string, FormationPositionDefinition> ValidatePositions(IReadOnlyList<FormationPositionDefinition> positions)
    {
        Dictionary<string, FormationPositionDefinition> map = new(StringComparer.Ordinal);
        foreach (FormationPositionDefinition position in positions)
        {
            if (position is null) throw new InvalidDataException("A formation position cannot be null.");
            position.Validate();
            if (!map.TryAdd(position.Id, position)) throw new InvalidDataException($"Duplicate formation position '{position.Id}'.");
        }

        if (!map.Values.Any(position => position.Rank == FrontRank))
        {
            throw new InvalidDataException("Formation positions need a front rank.");
        }

        return map;
    }

    private void ValidateRoster(IReadOnlyList<MemberDefinition> definitions)
    {
        if (definitions.Count < 1 || definitions.Count > maxSize)
        {
            throw new InvalidDataException($"A party needs between one and {maxSize} members.");
        }

        foreach (MemberDefinition definition in definitions)
        {
            if (definition is null) throw new InvalidDataException("A party cannot contain a null member.");
            definition.Validate();
            _ = RankOf(definition.Position);
        }

        if (definitions.Select(definition => definition.Id).Distinct(StringComparer.Ordinal).Count() != definitions.Count
            || definitions.Select(definition => definition.Position).Distinct(StringComparer.Ordinal).Count() != definitions.Count)
        {
            throw new InvalidDataException("Party members and formation positions must be distinct.");
        }
    }
}
