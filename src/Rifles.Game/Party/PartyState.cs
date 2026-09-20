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
    /// position id, then member id, so frontal attacks keep row-major order
    /// deterministically.
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

/// <summary>
/// One authored character archetype: identity, base stats/resources, and the
/// key other content references by this id. Presets reference archetypes;
/// they never copy these stat blocks.
/// </summary>
internal sealed record CharacterArchetypeDefinition(
    string Id,
    string Name,
    long MaximumVitality,
    long BasePower = 0,
    long BaseDefense = 0,
    long MaximumResource = 0,
    long? StartingVitality = null,
    long? StartingResource = null)
{
    internal const long MaximumTrackValue = 1_000_000_000_000;

    internal long InitialVitality => StartingVitality ?? MaximumVitality;
    internal long InitialResource => StartingResource ?? MaximumResource;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name)
            || MaximumVitality <= 0 || MaximumResource < 0
            || MaximumVitality > MaximumTrackValue || MaximumResource > MaximumTrackValue
            || BasePower < 0 || BaseDefense < 0
            || BasePower > PartyMemberState.MaximumDerivedStatistic || BaseDefense > PartyMemberState.MaximumDerivedStatistic
            || StartingVitality is long startingVitality && (startingVitality < 0 || startingVitality > MaximumVitality)
            || StartingResource is long startingResource && (startingResource < 0 || startingResource > MaximumResource))
        {
            throw new InvalidDataException($"Invalid character archetype '{Id}'.");
        }
    }

    internal MemberDefinition Resolve(string instanceId, string displayName, string position)
    {
        if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(position))
            throw new InvalidDataException($"Invalid '{Id}' instance reference.");
        return new MemberDefinition(instanceId, Id, string.IsNullOrWhiteSpace(displayName) ? Name : displayName,
            position, MaximumVitality, BasePower, BaseDefense, MaximumResource, StartingVitality, StartingResource);
    }
}

/// <summary>
/// One authored preset slot: runtime instance identity, archetype reference,
/// display customization, and formation position. Stats come from the
/// archetype at resolution time.
/// </summary>
internal sealed record PresetMemberDefinition(string Id, string Archetype, string Name, string Position)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Archetype)
            || string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Position))
        {
            throw new InvalidDataException($"Invalid preset member '{Id}'.");
        }
    }
}

/// <summary>
/// Resolved per-instance construction input consumed by party runtime and
/// saves. Instances carry their archetype id; two instances may share one
/// archetype with distinct instance ids.
/// </summary>
internal sealed record MemberDefinition(
    string Id,
    string Archetype,
    string Name,
    string Position,
    long MaximumVitality,
    long BasePower = 0,
    long BaseDefense = 0,
    long MaximumResource = 0,
    long? StartingVitality = null,
    long? StartingResource = null)
{
    private const long MaximumTrackValue = 1_000_000_000_000;

    internal long InitialVitality => StartingVitality ?? MaximumVitality;
    internal long InitialResource => StartingResource ?? MaximumResource;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Archetype)
            || string.IsNullOrWhiteSpace(Name)
            || string.IsNullOrWhiteSpace(Position) || MaximumVitality <= 0 || MaximumResource < 0
            || MaximumVitality > MaximumTrackValue || MaximumResource > MaximumTrackValue
            || BasePower < 0 || BaseDefense < 0
            || BasePower > PartyMemberState.MaximumDerivedStatistic || BaseDefense > PartyMemberState.MaximumDerivedStatistic
            || StartingVitality is long startingVitality && (startingVitality < 0 || startingVitality > MaximumVitality)
            || StartingResource is long startingResource && (startingResource < 0 || startingResource > MaximumResource))
        {
            throw new InvalidDataException($"Invalid member definition '{Id}'.");
        }
    }
}

/// <summary>One authored starter party. Loadout ownership stays with Inventory.</summary>
internal sealed record StarterPartyPresetDefinition(string Id, string Name, PresetMemberDefinition[] Members)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name)
            || Members is not { Length: > 0 })
        {
            throw new InvalidDataException($"Invalid starter party preset '{Id}'.");
        }

        foreach (PresetMemberDefinition member in Members)
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

/// <summary>
/// Authored characters: the archetype catalogue plus starter presets that
/// reference it. A preset is chosen only when an expedition begins.
/// </summary>
internal sealed record CharacterOptionsDefinition(string DefaultPresetId, CharacterArchetypeDefinition[] Archetypes, StarterPartyPresetDefinition[] Presets)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(DefaultPresetId) || Presets is not { Length: > 0 })
        {
            throw new InvalidDataException("Character options need a default preset and at least one preset.");
        }

        if (Archetypes is not { Length: > 0 })
        {
            throw new InvalidDataException("Character options need at least one archetype.");
        }

        foreach (CharacterArchetypeDefinition archetype in Archetypes)
        {
            if (archetype is null) throw new InvalidDataException("Character options contain a null archetype.");
            archetype.Validate();
        }

        if (Archetypes.Select(archetype => archetype.Id).Distinct(StringComparer.Ordinal).Count() != Archetypes.Length)
        {
            throw new InvalidDataException("Character archetype ids must be unique.");
        }

        foreach (StarterPartyPresetDefinition preset in Presets)
        {
            if (preset is null) throw new InvalidDataException("Character options contain a null preset.");
            preset.Validate();
            foreach (PresetMemberDefinition slot in preset.Members)
            {
                if (Archetypes.All(archetype => archetype.Id != slot.Archetype))
                {
                    throw new InvalidDataException($"Starter party preset '{preset.Id}' member '{slot.Id}' references unknown archetype '{slot.Archetype}'.");
                }
            }
        }

        if (Presets.Select(preset => preset.Id).Distinct(StringComparer.Ordinal).Count() != Presets.Length
            || !Presets.Any(preset => preset.Id == DefaultPresetId))
        {
            throw new InvalidDataException("Character option preset ids must be unique and include the default.");
        }
    }

    internal StarterPartyPresetDefinition GetPreset(string id) => Presets.SingleOrDefault(preset => preset.Id == id)
        ?? throw new InvalidOperationException($"Unknown starter party preset '{id}'.");

    internal CharacterArchetypeDefinition Archetype(string id) => Archetypes.SingleOrDefault(archetype => archetype.Id == id)
        ?? throw new InvalidDataException($"Unknown character archetype '{id}'.");

    /// <summary>
    /// Resolves a preset against the admitted archetype catalogue into
    /// per-instance construction input consumed by party runtime and saves.
    /// </summary>
    internal MemberDefinition[] ResolvePreset(string id)
    {
        StarterPartyPresetDefinition preset = GetPreset(id);
        return preset.Members.Select(slot =>
        {
            CharacterArchetypeDefinition archetype;
            try
            {
                archetype = Archetype(slot.Archetype);
            }
            catch (InvalidDataException error)
            {
                throw new InvalidDataException($"Starter party preset '{preset.Id}' member '{slot.Id}': {error.Message}", error);
            }

            return archetype.Resolve(slot.Id, slot.Name, slot.Position);
        }).ToArray();
    }
}

/// <summary>
/// Saved party-member state. Nullable fields admit pre-formation/pre-resource
/// snapshots; all newly captured snapshots provide both values.
/// </summary>
internal sealed record MemberSnapshot(string Id, long Vitality, string? Position = null, long? Resource = null);

internal sealed record EquipmentStatBonuses(long Power, long Defense);

internal sealed class PartyMemberState
{
    internal const long MaximumDerivedStatistic = 1_000_000_000_000;
    private readonly Track vitality;
    private readonly Track? resource;
    private readonly Stat powerStatistic;
    private readonly Stat defenseStatistic;
    private EquipmentStatBonuses equipmentBonuses = new(0, 0);
    private EquipmentStatBonuses developmentBonuses = new(0, 0);
    private StatModifierHandle? equipmentPowerModifier;
    private StatModifierHandle? equipmentDefenseModifier;
    private StatModifierHandle? developmentPowerModifier;
    private StatModifierHandle? developmentDefenseModifier;

    internal PartyMemberState(MemberDefinition definition, int rank)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (rank < 0) throw new ArgumentOutOfRangeException(nameof(rank));
        Definition = definition;
        Position = definition.Position;
        Rank = rank;
        powerStatistic = CreateDerivedStatistic(definition.BasePower);
        defenseStatistic = CreateDerivedStatistic(definition.BaseDefense);
        vitality = new Track(definition.MaximumVitality, definition.InitialVitality,
            quantum: 1, rounding: MidpointRounding.ToZero, integerRounding: MidpointRounding.ToZero);
        if (definition.MaximumResource > 0)
        {
            resource = new Track(definition.MaximumResource, definition.InitialResource,
                quantum: 1, rounding: MidpointRounding.ToZero, integerRounding: MidpointRounding.ToZero);
        }
    }

    internal MemberDefinition Definition { get; }
    internal string Position { get; private set; }
    internal int Rank { get; private set; }
    internal long Vitality => vitality.ValueInt64;
    internal long MaximumVitality => checked((long)vitality.MaximumValue);
    internal long Resource => resource?.ValueInt64 ?? 0;
    internal long MaximumResource => resource is null ? 0 : checked((long)resource.MaximumValue);
    internal bool IsLiving => Vitality > 0;
    internal long Power => powerStatistic.ValueInt64;
    internal long Defense => defenseStatistic.ValueInt64;
    internal EquipmentStatBonuses EquipmentBonuses => equipmentBonuses;

    internal long ApplyDamage(long requested)
    {
        if (requested < 0) throw new ArgumentOutOfRangeException(nameof(requested));
        long applied = Math.Min(Vitality, requested);
        vitality.Spend(applied);
        return applied;
    }

    internal long Heal(long requested)
    {
        if (requested < 0) throw new ArgumentOutOfRangeException(nameof(requested));
        return checked((long)vitality.Restore(requested));
    }

    internal long SpendResource(long requested)
    {
        if (requested < 0) throw new ArgumentOutOfRangeException(nameof(requested));
        if (resource is null) return 0;
        long applied = Math.Min(Resource, requested);
        resource.Spend(applied);
        return applied;
    }

    internal long RecoverResource(long requested)
    {
        if (requested < 0) throw new ArgumentOutOfRangeException(nameof(requested));
        return resource is null ? 0 : checked((long)resource.Restore(requested));
    }

    /// <summary>Receives the current aggregate from authoritative equipped items.</summary>
    internal void SetEquipmentBonuses(long power, long defense)
    {
        ValidateDerivedBonus(power, nameof(power));
        ValidateDerivedBonus(defense, nameof(defense));
        ReplaceModifier(powerStatistic, ref equipmentPowerModifier, power);
        ReplaceModifier(defenseStatistic, ref equipmentDefenseModifier, defense);
        equipmentBonuses = new EquipmentStatBonuses(power, defense);
    }

    internal void SetDevelopmentBonuses(long power, long defense)
    {
        ValidateDerivedBonus(power, nameof(power));
        ValidateDerivedBonus(defense, nameof(defense));
        ReplaceModifier(powerStatistic, ref developmentPowerModifier, power);
        ReplaceModifier(defenseStatistic, ref developmentDefenseModifier, defense);
        developmentBonuses = new(power, defense);
    }

    internal void SetPosition(string position, int rank)
    {
        if (string.IsNullOrWhiteSpace(position)) throw new ArgumentOutOfRangeException(nameof(position));
        if (rank < 0) throw new ArgumentOutOfRangeException(nameof(rank));
        Position = position;
        Rank = rank;
    }

    private static Stat CreateDerivedStatistic(long baseValue) => new(
        baseValue,
        minimum: 0,
        maximum: MaximumDerivedStatistic,
        quantum: 1,
        integerRounding: MidpointRounding.ToZero);

    private static void ReplaceModifier(Stat statistic, ref StatModifierHandle? existing, long value)
    {
        if (existing is not null) statistic.RemoveModifier(existing);
        existing = value == 0 ? null : statistic.AddModifier(value);
    }

    private static void ValidateDerivedBonus(long value, string parameter)
    {
        if (value < -MaximumDerivedStatistic || value > MaximumDerivedStatistic)
            throw new ArgumentOutOfRangeException(parameter, value, "Derived statistic bonuses must remain within product bounds.");
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

    internal PartyState(IReadOnlyList<FormationPositionDefinition> positions, int maxSize, CharacterOptionsDefinition characters, string presetId)
        : this(GetPresetId(characters, presetId), positions, maxSize, characters.ResolvePreset(presetId))
    {
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

    private static string GetPresetId(CharacterOptionsDefinition? characters, string presetId)
    {
        ArgumentNullException.ThrowIfNull(characters);
        if (string.IsNullOrWhiteSpace(presetId)) throw new ArgumentNullException(nameof(presetId));
        return characters.GetPreset(presetId).Id;
    }

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
