using System.Numerics;
using Rifles.Game.Magic;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Items;
using Rifles.Game.Party;

namespace Rifles.Game.Combat;

internal sealed record RestoredCombat(
    EnemyState[] Enemies,
    Dictionary<string, ActionState> Actions,
    ulong[] LoadedWeapons,
    FlightSnapshot[] Flights,
    DropSnapshot[] Drops,
    AllySnapshot[] Allies,
    ulong SelectedTarget, MagicState Magic);

/// <summary>Validates saved combat facts before the product binds them to live movement or rendering resources.</summary>
internal static class CombatRestore
{
    internal static RestoredCombat Validate(CombatSnapshot saved, GameDefinitions definitions, DungeonFloor floor,
        ItemInventory inventory, PartyState party, ulong partyId, ulong[] allyIds, IEnumerable<string>? expeditionRewards = null, long completionExperience = 0)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(allyIds);

        EnemyState[] enemies = RestoreEnemies(saved.Enemies, definitions, floor);
        Dictionary<string, ActionState> actions = RestoreMemberActions(saved.Members, definitions, floor, party);
        ValidateLoadedWeapons(saved.LoadedWeapons, definitions, inventory);
        FlightSnapshot[] flights = RestoreFlights(saved.Flights, definitions, floor, inventory, party, partyId, enemies);
        ValidateDropsAndCombatOwners(saved.Drops, flights, enemies, floor, inventory);
        ValidateAllies(saved.Allies, definitions.Combat, allyIds);
        GameDefinitions.Require(saved.SelectedTarget == 0 || enemies.Any(enemy => enemy.Id == saved.SelectedTarget), "saved combat target");

        MagicState magic = MagicState.Restore(saved.Magic ?? throw new InvalidDataException("Saved spell state missing."), definitions.Magic,
            party.Members.Select(m => (m.Definition.Id, m.Definition.Archetype)), party.Members.Where(m => m.IsLiving).Select(m => "member:" + m.Definition.Id)
                .Concat(enemies.Where(e => e.Alive).Select(e => "enemy:" + e.Id)).Append("party"), expeditionRewards ?? enemies.Where(e => !e.Alive).Select(e => e.Spawn), completionExperience);
        foreach (var entry in actions)
            if (entry.Value.Current is { Kind: CombatActionKind.Cast } cast)
                GameDefinitions.Require(magic.For(entry.Key).Known.Contains(cast.Spell!)
                    && cast.Cost == Math.Max(0, definitions.Magic.Spell(cast.Spell!).Cost - magic.CostDiscount(entry.Key))
                    && (cast.Phase == ActionPhase.Recovery || party.Members.Single(m => m.Definition.Id == entry.Key).Resource >= cast.Cost)
                    && cast.TargetMember is not null && party.Members.Any(m => m.Definition.Id == cast.TargetMember), "saved caster spell and target");
        GameDefinitions.Require(magic.RestRemaining == 0 || party.Members.Any(m => m.Definition.Id == magic.RestOwner && m.IsLiving)
            && actions.Values.All(a => !a.Busy), "saved rest eligibility");
        foreach (MagicConditionSnapshot condition in saved.Magic!.Conditions)
        {
            SpellDefinition spell = definitions.Magic.Spell(condition.Spell);
            GameDefinitions.Require(condition.Target == "party" ? spell.Target == SpellTarget.Party
                : condition.Target.StartsWith("member:", StringComparison.Ordinal) ? spell.Target == SpellTarget.Ally || spell.Harmful
                : spell.Harmful, "saved condition target kind");
        }
        return new RestoredCombat(enemies, actions, saved.LoadedWeapons, flights, saved.Drops, saved.Allies, saved.SelectedTarget, magic);
    }

    private static EnemyState[] RestoreEnemies(EnemySnapshot[] snapshots, GameDefinitions definitions, DungeonFloor floor)
    {
        GameDefinitions.Require(snapshots is { Length: > 0 } && snapshots.Length <= definitions.Combat.Encounter.Length
            && snapshots.Select(snapshot => snapshot.Spawn).Distinct(StringComparer.Ordinal).Count() == snapshots.Length
            && snapshots.Select(snapshot => snapshot.Spawn).ToHashSet(StringComparer.Ordinal)
                .IsSubsetOf(definitions.Combat.Encounter.Select(spawn => spawn.Id).ToHashSet(StringComparer.Ordinal)), "saved combat enemy definitions");

        EnemyState[] enemies = new EnemyState[snapshots.Length];
        for (int index = 0; index < snapshots.Length; index++)
        {
            EnemySnapshot snapshot = snapshots[index];
            EnemySpawnDefinition spawn = definitions.Combat.Encounter.Single(s => s.Id == snapshot.Spawn);
            GameDefinitions.Require(spawn.Enemy == snapshot.Definition, "saved enemy archetype");
            EnemyDefinition definition = definitions.Combat.Enemy(spawn.Enemy);
            GameDefinitions.Require(snapshot.Owner == EnemyOwner(snapshot.Id), "saved combat enemy owner");
            ValidateAction(snapshot.Action, definitions, floor);
            if (snapshot.Action is { Kind: CombatActionKind.Cast } cast)
                GameDefinitions.Require(definitions.Magic.EnemySpells.GetValueOrDefault(definition.Id) == cast.Spell
                    && cast.Cost == definitions.Magic.Spell(cast.Spell!).Cost && cast.TargetMember is null
                    && (cast.Phase == ActionPhase.Recovery || snapshot.Resource >= cast.Cost), "saved enemy cast");
            enemies[index] = new EnemyState(snapshot, definition, floor, definitions.Exploration, definitions.Magic.EnemyResource);
            var placement = definitions.Crowd.Footprint(definition.Footprint).Placement(snapshot.Motion.Placement);
            enemies[index].Motion.RestoreVisualOffset(new(placement.OffsetX, placement.OffsetY));
        }
        return enemies;
    }

    private static Dictionary<string, ActionState> RestoreMemberActions(MemberActionSnapshot[] snapshots,
        GameDefinitions definitions, DungeonFloor floor, PartyState party)
    {
        if (snapshots is null) throw new InvalidDataException("Invalid saved combat party actions.");
        string[] expected = party.Members.Select(member => member.Definition.Id).ToArray();
        GameDefinitions.Require(snapshots.Length == expected.Length
            && snapshots.Select(snapshot => snapshot.Member).Distinct(StringComparer.Ordinal).Count() == snapshots.Length
            && snapshots.Select(snapshot => snapshot.Member).ToHashSet(StringComparer.Ordinal).SetEquals(expected), "saved combat party actions");

        Dictionary<string, ActionState> actions = new(StringComparer.Ordinal);
        foreach (MemberActionSnapshot snapshot in snapshots)
        {
            PartyMemberState member = party.Members.Single(member => member.Definition.Id == snapshot.Member);
            ValidateAction(snapshot.Action, definitions, floor);
            ActionState action = ActionState.Restore(snapshot.Action);
            GameDefinitions.Require(member.IsLiving || !action.Busy, "dead member action");
            actions.Add(snapshot.Member, action);
        }
        return actions;
    }

    private static void ValidateAction(ActionSnapshot? action, GameDefinitions definitions, DungeonFloor floor)
    {
        if (action is null) return;
        GameDefinitions.Require(float.IsFinite(action.AimOffsetX) && Math.Abs(action.AimOffsetX) <= .5f
            && float.IsFinite(action.AimOffsetY) && Math.Abs(action.AimOffsetY) <= .5f, "saved aim offset");
        _ = ActionState.Restore(action);
        if (action.Kind == CombatActionKind.Cast)
        {
            SpellDefinition spell = definitions.Magic.Spell(action.Spell ?? "");
            GameDefinitions.Require(action.Cost >= 0 && action.Cost <= spell.Cost
                && action.Remaining <= (action.Phase == ActionPhase.Windup ? spell.Windup : spell.Recovery)
                && action.RecoverySeconds == spell.Recovery
                && (spell.Target == SpellTarget.Enemy) == action.AimCell.HasValue
                && (!action.AimCell.HasValue || floor.Cells.Contains(action.AimCell.Value)), "saved spell action");
            return;
        }
        GameDefinitions.Require(action.Spell is null && action.Cost == 0, "nonspell action payload");
        ActionDefinition profile = definitions.Combat.Action(action.Kind);
        double maximumRemaining = action.Phase == ActionPhase.Windup ? profile.Windup : profile.Recovery;
        GameDefinitions.Require(action.Remaining <= maximumRemaining && action.RecoverySeconds <= profile.Recovery
            && (action.Phase != ActionPhase.Recovery || action.Remaining <= action.RecoverySeconds), "saved combat action duration");

        bool attacks = action.Kind is CombatActionKind.Melee or CombatActionKind.Fire or CombatActionKind.Throw;
        GameDefinitions.Require(attacks == action.AimCell.HasValue && (!action.AimCell.HasValue || floor.Cells.Contains(action.AimCell.Value)),
            "saved combat action aim");
    }

    private static void ValidateLoadedWeapons(ulong[] loadedWeapons, GameDefinitions definitions, ItemInventory inventory)
    {
        if (loadedWeapons is null) throw new InvalidDataException("Invalid saved loaded weapons.");
        GameDefinitions.Require(loadedWeapons.Distinct().Count() == loadedWeapons.Length, "saved loaded weapons");
        HashSet<ulong> rifles = inventory.Owners.SelectMany(owner => inventory.Items(owner.Key))
            .Where(item => item.Entity != 0 && definitions.Items.Item(item.Definition).Ammunition.Length > 0)
            .Select(item => item.Entity).ToHashSet();
        GameDefinitions.Require(loadedWeapons.All(rifles.Contains), "saved loaded weapon item");
    }

    private static FlightSnapshot[] RestoreFlights(FlightSnapshot[] snapshots, GameDefinitions definitions, DungeonFloor floor,
        ItemInventory inventory, PartyState party, ulong partyId, EnemyState[] enemies)
    {
        if (snapshots is null) throw new InvalidDataException("Invalid saved combat flights.");
        HashSet<string> members = party.Members.Select(member => member.Definition.Id).ToHashSet(StringComparer.Ordinal);
        HashSet<string> enemyOwners = enemies.Select(enemy => enemy.Owner).ToHashSet(StringComparer.Ordinal);
        FlightSnapshot[] flights = new FlightSnapshot[snapshots.Length];
        for (int index = 0; index < snapshots.Length; index++)
        {
            FlightSnapshot flight = snapshots[index];
            SpellDefinition? spell = flight.Kind == CombatActionKind.Cast ? definitions.Magic.Spell(flight.Spell ?? "") : null;
            GameDefinitions.Require(spell is null ? flight.Spell is null : spell.Target == SpellTarget.Enemy && flight.Owner is null && flight.Destination is null, "saved spell flight payload");
            bool validShooter = flight.Shooter == partyId && flight.Member is not null && members.Contains(flight.Member)
                || spell is not null && flight.Member is null && enemies.Any(e => e.Id == flight.Shooter
                    && definitions.Magic.EnemySpells.GetValueOrDefault(e.Definition.Id) == spell.Id);
            GameDefinitions.Require(flight.Kind is CombatActionKind.Throw or CombatActionKind.Cast
                && float.IsFinite(flight.X) && float.IsFinite(flight.Y) && float.IsFinite(flight.Z)
                && float.IsFinite(flight.DirectionX) && float.IsFinite(flight.DirectionY) && float.IsFinite(flight.DirectionZ)
                && float.IsFinite(flight.Remaining) && flight.Remaining > 0 && flight.Remaining <= (spell?.Range ?? definitions.Combat.Action(flight.Kind).Range)
                && floor.Cells.Contains(flight.LastCell) && validShooter,
                "saved combat flight");

            Vector3 direction = new(flight.DirectionX, flight.DirectionY, flight.DirectionZ);
            GameDefinitions.Require(direction.LengthSquared() > 0 && float.IsFinite(direction.LengthSquared()), "saved combat flight direction");
            direction = Vector3.Normalize(direction);
            if (flight.Kind == CombatActionKind.Cast)
            {
                GameDefinitions.Require(flight.Owner is null, "saved spell owner");
            }
            else
            {
                string owner = flight.Owner ?? throw new InvalidDataException("Invalid saved combat throw owner.");
                PackOwner pack = inventory.Owner(owner);
                IReadOnlyList<CarriedItem> items = inventory.Items(owner);
                GameDefinitions.Require(!enemyOwners.Contains(owner) && owner == "combat:flight:" + pack.Id
                    && items.Count == 1 && items[0].Quantity == 1, "saved combat throw owner");
            }
            flights[index] = flight with { DirectionX = direction.X, DirectionY = direction.Y, DirectionZ = direction.Z };
        }
        return flights;
    }

    private static void ValidateDropsAndCombatOwners(DropSnapshot[] drops, FlightSnapshot[] flights, EnemyState[] enemies,
        DungeonFloor floor, ItemInventory inventory)
    {
        if (drops is null) throw new InvalidDataException("Invalid saved combat drops.");
        GameDefinitions.Require(drops.All(drop => !string.IsNullOrWhiteSpace(drop.Owner) && floor.Cells.Contains(drop.Cell))
            && drops.Select(drop => drop.Owner).Distinct(StringComparer.Ordinal).Count() == drops.Length, "saved combat drops");
        HashSet<string> dropOwners = drops.Select(drop => drop.Owner).ToHashSet(StringComparer.Ordinal);
        HashSet<string> enemyOwners = enemies.Select(enemy => enemy.Owner).ToHashSet(StringComparer.Ordinal);
        HashSet<string> flightOwners = flights.Where(flight => flight.Owner is not null).Select(flight => flight.Owner!).ToHashSet(StringComparer.Ordinal);
        GameDefinitions.Require(flightOwners.Count == flights.Count(flight => flight.Owner is not null)
            && !flightOwners.Overlaps(dropOwners), "saved combat flight drops");

        foreach (EnemyState enemy in enemies)
            GameDefinitions.Require(enemy.Alive ? !dropOwners.Contains(enemy.Owner) : dropOwners.Contains(enemy.Owner), "saved enemy drop");

        HashSet<string> inventoryCombatOwners = inventory.Owners.Where(owner => owner.Key.StartsWith("combat:", StringComparison.Ordinal))
            .Select(owner => owner.Key).ToHashSet(StringComparer.Ordinal);
        HashSet<string> knownOwners = new(enemyOwners, StringComparer.Ordinal);
        knownOwners.UnionWith(flightOwners);
        knownOwners.UnionWith(dropOwners);
        GameDefinitions.Require(inventoryCombatOwners.SetEquals(knownOwners), "saved combat inventory owners");

        foreach (string owner in knownOwners)
        {
            _ = inventory.Owner(owner);
            if (enemyOwners.Contains(owner)) continue;
            PackOwner pack = inventory.Owner(owner);
            GameDefinitions.Require(owner == "combat:flight:" + pack.Id, "saved combat flight inventory owner");
        }
    }

    private static void ValidateAllies(AllySnapshot[] allies, CombatDefinition combat, ulong[] allyIds)
    {
        GameDefinitions.Require(allies is not null && allyIds.Length == allies.Length
            && allies.Select(ally => ally.Id).Distinct().Count() == allies.Length
            && allies.Select(ally => ally.Id).ToHashSet().SetEquals(allyIds)
            && allies.All(ally => ally.Vitality is >= 0 && ally.Vitality <= combat.AllyVitality), "saved combat allies");
    }

    private static string EnemyOwner(ulong id) => "combat:enemy:" + id;
}
