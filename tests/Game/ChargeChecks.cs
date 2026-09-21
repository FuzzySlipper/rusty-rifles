using Rifles.Game.Combat;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Procgen.Generation;

internal static class ChargeChecks
{
    internal static void Run(GameDefinitions definitions)
    {
        VerifyStraightProbeHonorsWallsAndCrowds();
        VerifyManeuverTimeAndFailedCommit(definitions.Exploration);
        VerifyCommittedChargeStateAndRecovery();
        VerifyAuthoredChargeDefinition(definitions.Charge);
        Console.WriteLine("Charge checks passed: straight probes, dynamic stops, committed state, and authored recovery.");
    }

    private static void VerifyStraightProbeHonorsWallsAndCrowds()
    {
        HashSet<GridPoint> cells = [new(0, 0), new(1, 0), new(2, 0), new(3, 0)];
        MovementGrid clear = new(cells, (_, _) => true);
        clear.Add(1, new(0, 0));
        Require(clear.ProbeStraight(1, CardinalDirection.East, 3).SequenceEqual([new GridPoint(1, 0), new(2, 0), new(3, 0)]),
            "A charge preview exposes only consecutive legal forward cells.");
        clear.SetBlocked(new(1, 0), new(2, 0), true);
        Require(clear.ProbeStraight(1, CardinalDirection.East, 3).SequenceEqual([new GridPoint(1, 0)]),
            "A closed door or wall ends the forward-only probe without routing around it.");

        MovementGrid crowded = new(cells, (_, _) => true);
        crowded.Add(1, new(0, 0));
        crowded.Add(2, new(1, 0));
        Require(crowded.ProbeStraight(1, CardinalDirection.East, 3).Length == 0,
            "A dynamic body in the next cell prevents an initial charge reservation.");
    }

    private static void VerifyManeuverTimeAndFailedCommit(ExplorationTuning tuning)
    {
        bool admits = true;
        GridPoint origin = new(0, 0);
        GridPoint forward = origin + tuning.InitialFacing.Offset();
        GridPoint second = forward + tuning.InitialFacing.Offset();
        MovementGrid grid = new(new HashSet<GridPoint> { origin, forward, second }, (_, _) => admits);
        ExplorationState state = new(origin, tuning);
        state.Bind(grid, 1);
        Require(state.Act(ExplorationAction.Forward), "The initial forward reservation is admitted.");
        admits = false;
        state.AdvanceManeuver(tuning.StepSeconds / 2, tuning.StepSeconds / 2);
        Require(state.Position == origin && !state.Moving && !grid.Occupied(forward),
            "A changing admission failure leaves the party at its last legal cell and releases the reservation.");

        MovementGrid clear = new(new HashSet<GridPoint> { origin, forward }, (_, _) => true);
        ExplorationState fast = new(origin, tuning);
        fast.Bind(clear, 2);
        Require(fast.Act(ExplorationAction.Forward), "A clear maneuver reserves its one forward cell.");
        fast.AdvanceManeuver(tuning.StepSeconds / 2, tuning.StepSeconds / 2);
        Require(fast.Position == forward && fast.ElapsedSeconds == tuning.StepSeconds / 2,
            "Charge step timing scales only the movement countdown while elapsed simulation time stays admitted.");
    }

    private static void VerifyCommittedChargeStateAndRecovery()
    {
        ChargeSnapshot started = new(81, CardinalDirection.East, new GridPoint(3, 0), 3, 0,
            [new ChargeContributorSnapshot("warden", 22, 1)]);
        ChargeState state = new();
        state.Start(started);
        state.AdvanceStep();
        ChargeSnapshot saved = state.Capture()!;
        ChargeSnapshot restored = ChargeState.Restore(saved).Current;
        Require(restored.Target == saved.Target && restored.CompletedSteps == 1 && restored.Contributors.SequenceEqual(saved.Contributors),
            "A committed charge records its own target, contributors, and completed steps for a save.");

        ActionSnapshot recovery = new(CombatActionKind.Charge, 22, null, null, 0, null,
            1.8, ActionPhase.Recovery, 1.8);
        ActionState action = new();
        action.StartRecovery(recovery);
        action.Advance(.4, _ => throw new InvalidOperationException("Charge recovery has no second commit."));
        Require(action.Capture() is { Phase: ActionPhase.Recovery } partial && Math.Abs(partial.Remaining - 1.4) < .000001
            && !MusketActionRules.CanInterruptReload(recovery),
            "Committed charge recovery is durable and cannot be discarded as reload windup.");
    }

    private static void VerifyAuthoredChargeDefinition(ChargeDefinition charge)
    {
        Require(charge.MaximumCells > 0 && charge.StepSeconds > 0 && charge.RecoverySeconds > 0 && charge.BonusDamage >= 0,
            "Charge distance, timing, recovery, and bonus damage are authored positive values.");
        Reject(() => (charge with { MaximumCells = 0 }).Validate(), "Invalid charge distance is rejected at content admission.");
        Reject(() => (charge with { StepSeconds = 0 }).Validate(), "Invalid charge step timing is rejected at content admission.");
    }

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
