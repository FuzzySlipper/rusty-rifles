using Rifles.Procgen.Generation;

namespace Rifles.Game.Combat;

internal enum CombatActionKind
{
    Melee,
    Fire,
    Reload,
    FixBayonet,
    UnfixBayonet,
    Charge,
    Throw,
    Consume,
    Cast,
}

internal enum ActionPhase
{
    Windup,
    Recovery,
}

internal sealed record ActionSnapshot(
    CombatActionKind Kind,
    ulong Weapon,
    string? ItemToken,
    string? SourceOwner,
    ulong Target,
    string? TargetMember,
    double Remaining,
    ActionPhase Phase,
    double RecoverySeconds,
    GridPoint? AimCell = null, float AimOffsetX = 0, float AimOffsetY = 0, string? Spell = null, long Cost = 0, ulong FeatureRevision = 0,
    GridPoint? OrderOrigin = null, CardinalDirection? OrderFacing = null, bool SuppressSharedEffect = false);

/// <summary>
/// The current party action. The caller supplies only admitted simulation time;
/// this state has no clock or scheduler of its own.
/// </summary>
internal sealed class ActionState
{
    private ActionSnapshot? current;

    internal ActionSnapshot? Current => current;
    internal bool Busy => current is not null;

    internal void Start(ActionSnapshot action)
    {
        if (Busy) throw new InvalidOperationException("An action is already in progress.");
        Validate(action);
        if (action.Phase != ActionPhase.Windup) throw new InvalidDataException("New actions must begin in windup.");
        current = action;
    }

    /// <summary>Applies a committed maneuver's recovery without a second effect windup.</summary>
    internal void StartRecovery(ActionSnapshot action)
    {
        if (Busy) throw new InvalidOperationException("An action is already in progress.");
        Validate(action);
        if (action.Phase != ActionPhase.Recovery || action.Remaining != action.RecoverySeconds)
            throw new InvalidDataException("Recovery must begin at its authored duration.");
        current = action;
    }

    internal void Advance(double seconds, Action<ActionSnapshot> commit)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        ArgumentNullException.ThrowIfNull(commit);

        if (current is null || seconds == 0) return;

        ActionSnapshot action = current;
        if (action.Phase == ActionPhase.Windup)
        {
            if (seconds < action.Remaining)
            {
                current = action with { Remaining = action.Remaining - seconds };
                return;
            }

            seconds -= action.Remaining;
            ActionSnapshot recovery = action with
            {
                Remaining = action.RecoverySeconds,
                Phase = ActionPhase.Recovery,
            };

            // Make the transition durable before resolving the effect. If the effect
            // reports an error, a subsequent admitted update cannot resolve it again.
            current = recovery;
            commit(recovery);
        }

        if (current is null || current.Phase != ActionPhase.Recovery) return;

        ActionSnapshot activeRecovery = current;
        if (seconds >= activeRecovery.Remaining) current = null;
        else current = activeRecovery with { Remaining = activeRecovery.Remaining - seconds };
    }

    internal void Cancel() => current = null;

    internal ActionSnapshot? Capture()
    {
        if (current is not null) Validate(current);
        return current;
    }

    internal static ActionState Restore(ActionSnapshot? snapshot)
    {
        ActionState state = new();
        if (snapshot is not null)
        {
            Validate(snapshot);
            state.current = snapshot;
        }
        return state;
    }

    private static void Validate(ActionSnapshot action)
    {
        if (!Enum.IsDefined(action.Kind)) throw new InvalidDataException("Action kind is invalid.");
        if (!Enum.IsDefined(action.Phase)) throw new InvalidDataException("Action phase is invalid.");
        if (!double.IsFinite(action.Remaining) || action.Remaining <= 0)
            throw new InvalidDataException("Action remaining time must be finite and positive.");
        if (!double.IsFinite(action.RecoverySeconds) || action.RecoverySeconds <= 0)
            throw new InvalidDataException("Action recovery time must be finite and positive.");
        if (action.OrderOrigin.HasValue != action.OrderFacing.HasValue
            || action.OrderFacing is { } facing && !Enum.IsDefined(facing))
            throw new InvalidDataException("Order actions need a valid origin pose.");
    }
}
