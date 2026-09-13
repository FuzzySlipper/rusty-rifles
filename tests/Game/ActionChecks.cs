using Rifles.Game.Combat;

internal static class ActionChecks
{
    internal static void Run()
    {
        VerifyWindupAndAdmittedTime();
        VerifyCoarseUpdateCommitsOnce();
        VerifyCommitExceptionCannotReplay();
        VerifySaveRestore();
        VerifyInvalidSnapshotsAreRejected();

        Console.WriteLine("Action checks passed: windup, commit, recovery, save state, and interruption safety.");
    }

    private static void VerifyWindupAndAdmittedTime()
    {
        ActionState state = new();
        state.Start(Action(remaining: 0.4, recovery: 0.3));
        int commits = 0;

        state.Advance(0, _ => commits++);
        Require(state.Busy && state.Current!.Phase == ActionPhase.Windup && Same(state.Current.Remaining, 0.4) && commits == 0,
            "Paused simulation time leaves an action in windup.");

        state.Advance(0.25, _ => commits++);
        Require(state.Current!.Phase == ActionPhase.Windup && Same(state.Current.Remaining, 0.15) && commits == 0,
            "Only admitted time advances the windup.");
    }

    private static void VerifyCoarseUpdateCommitsOnce()
    {
        ActionState state = new();
        state.Start(Action(remaining: 0.2, recovery: 0.3));
        ActionSnapshot? committed = null;

        state.Advance(0.6, action => committed = action);
        Require(committed is not null && committed.Phase == ActionPhase.Recovery && Same(committed.Remaining, 0.3),
            "Windup completion enters recovery before the action effect commits.");
        Require(!state.Busy, "One coarse update can consume both windup and recovery.");
    }

    private static void VerifyCommitExceptionCannotReplay()
    {
        ActionState state = new();
        state.Start(Action(remaining: 0.2, recovery: 0.3));
        int commits = 0;

        RequireRejected(() => state.Advance(0.2, _ =>
        {
            commits++;
            throw new InvalidOperationException("Effect failed after becoming durable.");
        }), "A resolving effect can report its failure.");
        Require(state.Busy && state.Current!.Phase == ActionPhase.Recovery && commits == 1,
            "A failed effect remains in recovery after its windup has resolved.");

        state.Advance(0.3, _ => commits++);
        Require(!state.Busy && commits == 1, "Later recovery does not replay a previously attempted action effect.");
    }

    private static void VerifySaveRestore()
    {
        ActionState state = new();
        state.Start(Action(remaining: 0.5, recovery: 0.4));
        state.Advance(0.2, _ => throw new InvalidOperationException("Windup should not commit yet."));
        ActionSnapshot windup = state.Capture() ?? throw new InvalidOperationException("Windup snapshot missing.");

        ActionState restored = ActionState.Restore(windup);
        ActionSnapshot? committed = null;
        restored.Advance(0.3, action => committed = action);
        Require(committed is not null && committed.Phase == ActionPhase.Recovery && restored.Busy,
            "Restoring a windup action preserves its remaining delay and effect meaning.");

        ActionSnapshot recovery = restored.Capture() ?? throw new InvalidOperationException("Recovery snapshot missing.");
        ActionState resumedRecovery = ActionState.Restore(recovery);
        resumedRecovery.Advance(0.4, _ => throw new InvalidOperationException("Recovery must not commit again."));
        Require(!resumedRecovery.Busy, "Restoring recovery completes without replaying the action effect.");
    }

    private static void VerifyInvalidSnapshotsAreRejected()
    {
        ActionSnapshot action = Action();
        RequireRejected(() => new ActionState().Start(action with { Phase = ActionPhase.Recovery }),
            "New actions cannot skip directly to recovery.");
        RequireRejected(() => ActionState.Restore(action with { Remaining = double.NaN }),
            "Saves reject non-finite remaining action time.");
        RequireRejected(() => ActionState.Restore(action with { RecoverySeconds = 0 }),
            "Saves reject missing recovery duration.");
        RequireRejected(() => ActionState.Restore(action with { Kind = (CombatActionKind)99 }),
            "Saves reject unknown action kinds.");
        RequireRejected(() => ActionState.Restore(action with { Phase = (ActionPhase)99 }),
            "Saves reject unknown action phases.");
    }

    private static ActionSnapshot Action(double remaining = 0.5, double recovery = 0.25) => new(
        CombatActionKind.Fire, 7, "i:7", "member:warden", 8, null, remaining, ActionPhase.Windup, recovery);

    private static bool Same(double actual, double expected) => Math.Abs(actual - expected) < 0.000001;

    private static void RequireRejected(Action action, string message)
    {
        try
        {
            action();
        }
        catch (Exception) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
