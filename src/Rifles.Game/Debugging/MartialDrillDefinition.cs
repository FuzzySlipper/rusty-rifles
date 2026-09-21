using Rifles.Game.Characters;
using Rifles.Game.Combat;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Party;
using Rifles.Procgen.Generation;

namespace Rifles.Game.Debugging;

/// <summary>Small, authored starting states used to demonstrate the martial controls in a live floor.</summary>
internal sealed record MartialDrillDefinitionSet(MartialDrillDefinition[] Drills)
{
    internal void Validate()
    {
        GameDefinitions.Require(Drills is { Length: > 0 and <= 8 }
            && Drills.All(drill => drill is not null)
            && Drills.Select(drill => drill.Id).Distinct(StringComparer.Ordinal).Count() == Drills.Length,
            "martial drill definitions");
        foreach (MartialDrillDefinition drill in Drills) drill.Validate();
    }

    internal MartialDrillDefinition Drill(string id) => Drills.SingleOrDefault(drill => drill.Id == id)
        ?? throw new InvalidDataException("Unknown martial drill '" + id + "'. Use rifles.drill.list.");

    internal void ValidateAgainst(CharacterOptionsDefinition characters, FormationDefinition formation, CombatDefinition combat,
        CrowdDefinition crowd)
    {
        foreach (MartialDrillDefinition drill in Drills)
        {
            MemberDefinition[] roster = characters.ResolvePreset(characters.DefaultPresetId);
            GameDefinitions.Require(drill.Members.Select(member => member.Member).ToHashSet(StringComparer.Ordinal)
                .SetEquals(roster.Select(member => member.Id)), "martial drill roster coverage");
            foreach (MartialDrillMemberDefinition member in drill.Members)
            {
                MemberDefinition resolved = roster.SingleOrDefault(candidate => candidate.Id == member.Member)
                    ?? throw new InvalidDataException("Martial drill '" + drill.Id + "' names unknown member '" + member.Member + "'.");
                FormationCellDefinition cell = formation.Cell(member.Position);
                GameDefinitions.Require(cell.Commander == resolved.Commander, "martial drill commander formation");
            }
            foreach (string member in drill.BayonetMembers)
            {
                MemberDefinition resolved = roster.SingleOrDefault(candidate => candidate.Id == member)
                    ?? throw new InvalidDataException("Martial drill '" + drill.Id + "' names unknown bayonet member '" + member + "'.");
                GameDefinitions.Require(!resolved.Commander, "martial drill bayonet commander");
            }
            foreach (MartialDrillEnemyDefinition enemy in drill.Enemies)
            {
                EnemySpawnDefinition spawn = combat.Encounter.SingleOrDefault(candidate => candidate.Id == enemy.SpawnId)
                    ?? throw new InvalidDataException("Martial drill '" + drill.Id + "' names unknown spawn '" + enemy.SpawnId + "'.");
                EnemyDefinition archetype = combat.Enemy(spawn.Enemy);
                _ = crowd.Footprint(archetype.Footprint).Placement(enemy.Placement);
            }
        }
    }
}

internal sealed record MartialDrillDefinition(string Id, string Name, string Description, CardinalDirection Facing,
    MartialDrillMemberDefinition[] Members, MartialDrillEnemyDefinition[] Enemies, string[] BayonetMembers,
    bool ForwardBlocked, string Expected)
{
    internal void Validate()
    {
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(Id) && Id.All(value => char.IsLower(value) || char.IsDigit(value) || value is '-' or '_')
            && !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Description) && Enum.IsDefined(Facing)
            && Members is { Length: > 0 } && Members.All(member => member is not null)
            && Members.Select(member => member.Member).Distinct(StringComparer.Ordinal).Count() == Members.Length
            && Enemies is { Length: > 0 and <= 3 } && Enemies.All(enemy => enemy is not null)
            && Enemies.Select(enemy => enemy.SpawnId).Distinct(StringComparer.Ordinal).Count() == Enemies.Length
            && BayonetMembers is not null && BayonetMembers.Distinct(StringComparer.Ordinal).Count() == BayonetMembers.Length
            && (!ForwardBlocked || Enemies.Any(enemy => enemy.Forward == 1 && enemy.Left == 0))
            && !string.IsNullOrWhiteSpace(Expected), "martial drill '" + Id + "'");
        foreach (MartialDrillMemberDefinition member in Members) member.Validate(Id);
        foreach (MartialDrillEnemyDefinition enemy in Enemies) enemy.Validate(Id);
    }
}

internal sealed record MartialDrillMemberDefinition(string Member, string Position, bool Fallen = false)
{
    internal void Validate(string drill)
    {
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(Member) && !string.IsNullOrWhiteSpace(Position),
            "martial drill member '" + drill + "'");
    }
}

/// <summary>Offsets are formation-relative cells: forward follows the authored facing and left is the party's left.</summary>
internal sealed record MartialDrillEnemyDefinition(string SpawnId, int Forward, int Left, string Placement)
{
    internal void Validate(string drill)
    {
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(SpawnId) && Forward is > 0 and <= 12 && Left is >= -4 and <= 4
            && !string.IsNullOrWhiteSpace(Placement), "martial drill enemy '" + drill + "'");
    }
}
