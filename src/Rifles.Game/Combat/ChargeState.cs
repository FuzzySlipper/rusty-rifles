using Rifles.Game.Characters;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Items;
using Rifles.Game.Party;
using Rifles.Procgen.Generation;

namespace Rifles.Game.Combat;

/// <summary>Durable coordination state for one committed, straight party charge.</summary>
internal sealed class ChargeState
{
    private ChargeSnapshot? current;

    internal bool Executing => current is not null;
    internal ChargeSnapshot Current => current ?? throw new InvalidOperationException("No charge is active.");

    internal void Start(ChargeSnapshot snapshot)
    {
        if (current is not null) throw new InvalidOperationException("A charge is already active.");
        current = Copy(snapshot);
    }

    internal void AdvanceStep()
    {
        ChargeSnapshot active = Current;
        if (active.CompletedSteps >= active.PlannedSteps) throw new InvalidOperationException("Charge has no remaining steps.");
        current = active with { CompletedSteps = active.CompletedSteps + 1 };
    }

    internal ChargeSnapshot Clear()
    {
        ChargeSnapshot active = Current;
        current = null;
        return active;
    }

    internal ChargeSnapshot? Capture() => current is null ? null : Copy(current);

    internal static ChargeState Restore(ChargeSnapshot? snapshot)
    {
        ChargeState state = new();
        if (snapshot is not null) state.current = Copy(snapshot);
        return state;
    }

    /// <summary>Cross-validates the durable maneuver against the saved forward reservation.</summary>
    internal static void ValidateSaved(ChargeSnapshot? snapshot, ExplorationSnapshot exploration, GameDefinitions definitions,
        DungeonFloor floor, PartyState party, ItemInventory inventory)
    {
        if (snapshot is null) return;
        ValidateShape(snapshot, definitions.Charge, floor, party, inventory);
        GridPoint direction = snapshot.Facing.Offset();
        int remaining = snapshot.PlannedSteps - snapshot.CompletedSteps;
        GameDefinitions.Require(exploration.Facing == snapshot.Facing && exploration.Action == ExplorationAction.Forward
            && exploration.Destination == exploration.Position + direction && exploration.RemainingSeconds > 0
            && snapshot.PlannedStop == exploration.Position + new GridPoint(direction.X * remaining, direction.Y * remaining),
            "saved charge exploration reservation");
    }

    /// <summary>
    /// Validates stable charge identities after combat restore. A reserved step
    /// may outlive its target, a contributor, or their equipped weapon; those
    /// live changes stop or skip the eventual contact instead of invalidating a
    /// legitimate save.
    /// </summary>
    internal static void ValidateCombat(ChargeSnapshot? snapshot, IEnumerable<EnemyState> enemies, PartyState party,
        ItemInventory inventory, GameDefinitions definitions)
    {
        if (snapshot is null) return;
        GameDefinitions.Require(enemies.Any(enemy => enemy.Id == snapshot.Target), "saved charge target");
        double maximumWeaponRecovery = definitions.Items.Items.Where(item => item.Weapon is not null)
            .Max(item => item.Weapon!.RecoverySeconds);
        foreach (ChargeContributorSnapshot contributor in snapshot.Contributors)
        {
            GameDefinitions.Require(party.Members.Any(member => member.Definition.Id == contributor.Member && !member.Definition.Commander)
                && inventory.UniqueItem(contributor.Weapon).Entity == contributor.Weapon
                && contributor.WeaponRecoverySeconds <= maximumWeaponRecovery, "saved charge contributor");
        }
    }

    private static void ValidateShape(ChargeSnapshot snapshot, ChargeDefinition tuning, DungeonFloor floor,
        PartyState party, ItemInventory inventory)
    {
        GameDefinitions.Require(snapshot.Target != 0 && Enum.IsDefined(snapshot.Facing) && floor.Cells.Contains(snapshot.PlannedStop)
            && snapshot.PlannedSteps is > 0 and <= 8 && snapshot.PlannedSteps <= tuning.MaximumCells
            && snapshot.CompletedSteps is >= 0 && snapshot.CompletedSteps < snapshot.PlannedSteps
            && snapshot.Contributors is { Length: > 0 }
            && snapshot.Contributors.Select(contributor => contributor.Member).Distinct(StringComparer.Ordinal).Count() == snapshot.Contributors.Length,
            "saved charge state");
        foreach (ChargeContributorSnapshot contributor in snapshot.Contributors)
        {
            GameDefinitions.Require(contributor.Weapon != 0 && double.IsFinite(contributor.WeaponRecoverySeconds)
                && contributor.WeaponRecoverySeconds > 0
                && party.Members.Any(member => member.Definition.Id == contributor.Member && !member.Definition.Commander)
                && inventory.UniqueItem(contributor.Weapon).Entity == contributor.Weapon, "saved charge contributor");
        }
    }

    private static ChargeSnapshot Copy(ChargeSnapshot snapshot) => snapshot with { Contributors = [.. snapshot.Contributors] };
}

/// <summary>Current charge admission and progress for the existing HUD projection.</summary>
internal sealed record ChargeReadout(bool Executing, int Eligible, int Total, string Reason, int CompletedSteps,
    int PlannedSteps, double RemainingSeconds, IReadOnlyList<MemberOrderReadout> Contributors);
