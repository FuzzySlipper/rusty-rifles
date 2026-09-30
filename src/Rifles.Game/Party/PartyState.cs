using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using Rifles.Game.Characters;

namespace Rifles.Game.Party;

/// <summary>
/// One authored formation position. Rank 0 is the front rank; higher ranks
/// sit behind it. Reach and damage order derive from rank, never from roster
/// order or array index. Offsets are formation-local cell fractions —
/// forward toward the party facing, left from the party's point of view —
/// rotated into the world by the facing at use time, so the same authored
/// layout reads correctly no matter which way the party faces.
/// </summary>
internal sealed record FormationPositionDefinition(string Id, string Name, int Rank, float OffsetForward = 0, float OffsetLeft = 0, bool Commander = false)
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
    [property: System.Text.Json.Serialization.JsonRequired] long BasePower = 0,
    [property: System.Text.Json.Serialization.JsonRequired] long BaseDefense = 0,
    [property: System.Text.Json.Serialization.JsonRequired] long MaximumResource = 0,
    [property: System.Text.Json.Serialization.JsonRequired] long? StartingVitality = null,
    [property: System.Text.Json.Serialization.JsonRequired] long? StartingResource = null, [property: System.Text.Json.Serialization.JsonRequired] bool Commander = false)
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
            || BasePower > RiflesCharacter.MaximumDerivedStatistic || BaseDefense > RiflesCharacter.MaximumDerivedStatistic
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
            position, MaximumVitality, BasePower, BaseDefense, MaximumResource, StartingVitality, StartingResource, Commander);
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
    long? StartingResource = null, bool Commander = false)
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
            || BasePower > RiflesCharacter.MaximumDerivedStatistic || BaseDefense > RiflesCharacter.MaximumDerivedStatistic
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
internal sealed record MemberSnapshot(string Id, string Position, StatsComponentSnapshot Stats);

internal sealed class PartyState
{
    private readonly CharacterEntities entities = new();
    private readonly MemberDefinition[] roster;
    private readonly Dictionary<string, FormationPositionDefinition> positions;
    private readonly int maxSize;
    private RiflesCharacter[] members;

    internal PartyState(IReadOnlyList<FormationPositionDefinition> positions, int maxSize, IReadOnlyList<MemberDefinition> definitions)
        : this(null, positions, maxSize, definitions)
    {
    }

    internal PartyState(IReadOnlyList<FormationPositionDefinition> positions, int maxSize, CharacterOptionsDefinition characters, string presetId)
        : this(GetPresetId(characters, presetId), positions, maxSize, characters.ResolvePreset(presetId))
    {
    }

    private PartyState(string? presetId, IReadOnlyList<FormationPositionDefinition> positions, int maxSize, IReadOnlyList<MemberDefinition> definitions, IReadOnlyList<MemberSnapshot>? saved = null)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(definitions);
        this.positions = ValidatePositions(positions);
        if (maxSize < 1) throw new InvalidDataException("Party capacity must be positive.");
        this.maxSize = maxSize;
        roster = definitions.ToArray();
        ValidateRoster(roster);
        PresetId = presetId;
        members = saved is null ? roster.Select(definition => CreateMember(definition, RankOf(definition.Position))).ToArray() : [];
        Formation = new(this);
        if (saved is not null) Restore(saved);
    }

    internal static PartyState FromSnapshot(IReadOnlyList<FormationPositionDefinition> positions, int maxSize,
        IReadOnlyList<MemberDefinition> roster, IReadOnlyList<MemberSnapshot> saved) => new(null, positions, maxSize, roster, saved);

    /// <summary>
    /// The entity owner behind this party's characters. Enemy creation for the
    /// same floor attaches here too, so one store holds the floor's characters
    /// until #8363 consolidates floor lifetime.
    /// </summary>
    internal CharacterEntities Entities => entities;
    internal FormationPlanner Formation { get; }

    private RiflesCharacter CreateMember(MemberDefinition definition, int rank, Func<StatsComponent>? build = null)
    {
        (EntityId entity, StatsComponent stats) = entities.AttachStats(
            definition.Id, "rifles:member:" + definition.Archetype, build ?? (() => RiflesStats.ForMember(definition)));
        return new RiflesCharacter(new Actor(entities.Store, entity), definition, stats, definition.Position, rank);
    }

    private static string GetPresetId(CharacterOptionsDefinition? characters, string presetId)
    {
        ArgumentNullException.ThrowIfNull(characters);
        if (string.IsNullOrWhiteSpace(presetId)) throw new ArgumentNullException(nameof(presetId));
        return characters.GetPreset(presetId).Id;
    }

    /// <summary>Set for a starter-preset party and immutable for its expedition.</summary>
    internal string? PresetId { get; }

    /// <summary>
    /// Party-wide rest state. Rest belongs to the party/session owner, not the
    /// magic books: it travels with the party and resets when floors join idle.
    /// </summary>
    internal double RestRemaining { get; private set; }

    internal string RestOwner { get; private set; } = "";
    internal void BeginRest(string owner, double seconds)
    {
        if (RestRemaining > 0 || !members.Any(member => member.InstanceId == owner && member.IsLiving)
            || !double.IsFinite(seconds) || seconds <= 0) throw new InvalidDataException("Invalid rest request.");
        RestOwner = owner;
        RestRemaining = seconds;
    }
    internal void RestoreRest(double remaining, string owner)
    {
        if (!double.IsFinite(remaining) || remaining < 0
            || (remaining > 0 ? !members.Any(member => member.InstanceId == owner && member.IsLiving) : owner.Length != 0))
            throw new InvalidDataException("Invalid saved rest owner or time.");
        RestRemaining = remaining;
        RestOwner = owner;
    }
    internal bool CancelRest()
    {
        bool wasResting = RestRemaining > 0;
        RestRemaining = 0;
        RestOwner = "";
        return wasResting;
    }
    internal string? AdvanceRest(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (RestRemaining <= 0) return null;
        RestRemaining = Math.Max(0, RestRemaining - seconds);
        if (RestRemaining > 0) return null;
        string owner = RestOwner;
        RestOwner = "";
        return owner;
    }

    internal RiflesCharacter? Commander => members.SingleOrDefault(member => member.Definition.Commander);
    internal bool Defeated => Commander is { } commander ? !commander.IsLiving : members.All(member => !member.IsLiving);
    internal IReadOnlyList<RiflesCharacter> Soldiers => members.Where(member => !member.Definition.Commander).ToArray();

    internal RiflesCharacter? ScreenedRecipient(FormationDefinition formation, FormationApproach approach)
    {
        string? id = FormationRules.SelectScreeningRecipient(formation, approach,
            members.Select(member => new FormationOccupant(member.InstanceId, member.Position, member.IsLiving)));
        return id is null ? Commander : members.Single(member => member.InstanceId == id);
    }

    internal IReadOnlyList<RiflesCharacter> Members => Array.AsReadOnly(members);
    internal IReadOnlyList<FormationPositionDefinition> Positions => positions.Values.OrderBy(p => p.Rank).ThenBy(p => p.Id, StringComparer.Ordinal).ToArray();
    internal IReadOnlyList<MemberSnapshot> Capture() => members
        .Select(member => new MemberSnapshot(member.Definition.Id, member.Position, RiflesStats.SnapshotForPersistence(member.Stats))).ToArray();

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

        RiflesCharacter[] restored = saved.Select(value =>
        {
            MemberDefinition definition = roster.SingleOrDefault(candidate => candidate.Id == value.Id)
                ?? throw new InvalidOperationException("Party snapshot member missing.");
            if (!positions.TryGetValue(value.Position, out FormationPositionDefinition? target) || target.Commander != definition.Commander)
            {
                throw new InvalidOperationException("Party snapshot formation position is unknown.");
            }

            // Stats rebuild modifier-free from the admitted snapshot with
            // authored bases intact; callers re-apply equipment afterwards.
            RiflesStats.AdmitStats(value.Stats, RiflesStats.ForMember(definition));
            entities.Detach(value.Id);
            RiflesCharacter member = CreateMember(definition, target.Rank, () => RiflesStats.RebuildForRestore(value.Stats));
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

    private static Dictionary<string, FormationPositionDefinition> ValidatePositions(IReadOnlyList<FormationPositionDefinition> positions)
    {
        Dictionary<string, FormationPositionDefinition> map = new(StringComparer.Ordinal);
        foreach (FormationPositionDefinition position in positions)
        {
            if (position is null) throw new InvalidDataException("A formation position cannot be null.");
            position.Validate();
            if (!map.TryAdd(position.Id, position)) throw new InvalidDataException($"Duplicate formation position '{position.Id}'.");
        }

        if (!map.Values.Any(position => position.Rank == 0))
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
            if (positions[definition.Position].Commander != definition.Commander)
                throw new InvalidDataException("Commander must occupy the reserved center; soldiers cannot occupy it.");
        }

        if (definitions.Count(definition => definition.Commander) > 1) throw new InvalidDataException("Only one commander is allowed.");

        if (definitions.Select(definition => definition.Id).Distinct(StringComparer.Ordinal).Count() != definitions.Count
            || definitions.Select(definition => definition.Position).Distinct(StringComparer.Ordinal).Count() != definitions.Count)
        {
            throw new InvalidDataException("Party members and formation positions must be distinct.");
        }
    }
}
