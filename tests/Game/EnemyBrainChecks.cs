using Rifles.Game.Combat;
using Rifles.Procgen.Generation;

internal static class EnemyBrainChecks
{
    internal static void Run()
    {
        EnemyBrainDefinition definition = new(5, 3, 2, 1, 3, 8, .5, 100, 12, 6, 18);
        definition.Validate();

        VerifySightLossSearchReturnAndPatrol(definition);
        VerifyPursuitMemoryExpiresWithoutADecision(definition);
        VerifySavedSearchDoesNotReplayNoise(definition);
        VerifyRetreatIsBounded(definition);
        VerifySnapshotValidation(definition);

        Console.WriteLine("Enemy brain checks passed: sight loss, bounded search, save resume, retreat budget.");
    }

    private static void VerifyPursuitMemoryExpiresWithoutADecision(EnemyBrainDefinition definition)
    {
        EnemyBrain brain = new(definition, new(0, 0), [new(0, 0), new(1, 0)]);
        GridPoint target = new(4, 0);
        brain.Observe(target, null);
        brain.Advance(definition.MemorySeconds);
        Require(brain.Mode == EnemyBrainMode.Return && brain.Reason == EnemyBrainReason.MemoryExpired && brain.LastKnownTarget is null,
            "A busy pursuer cannot retain old sight indefinitely without an admitted sensory decision.");

        brain = new EnemyBrain(definition, new(0, 0), [new(0, 0), new(1, 0)]);
        for (int index = 0; index < 3; index++)
        {
            brain.Advance(definition.MemorySeconds - 1);
            brain.Observe(target, null);
        }
        Require(brain.Mode == EnemyBrainMode.Pursue && brain.LastKnownTarget == target,
            "Each new visible observation refreshes pursuit memory at the decision boundary.");
    }

    private static void VerifySightLossSearchReturnAndPatrol(EnemyBrainDefinition definition)
    {
        GridPoint home = new(0, 0);
        GridPoint waypoint = new(1, 0);
        GridPoint target = new(4, 0);
        EnemyBrain brain = new(definition, home, [home, waypoint]);

        Require(brain.Goal(home) == waypoint && brain.Mode == EnemyBrainMode.Patrol, "Patrol advances from its arrived home route point.");
        brain.Observe(target, null);
        Require(brain.Mode == EnemyBrainMode.Pursue && brain.Reason == EnemyBrainReason.VisibleTarget && brain.Goal(home) == target,
            "Only a visible target begins pursuit.");
        brain.Observe(null, null);
        Require(brain.Mode == EnemyBrainMode.Search && brain.Reason == EnemyBrainReason.LostTarget && brain.Goal(home) == target,
            "Sight loss searches the last confirmed target cell.");
        Require(brain.Goal(target) == target, "Arrival starts a bounded local search instead of omniscient retargeting.");
        brain.Advance(definition.SearchSeconds);
        Require(brain.Mode == EnemyBrainMode.Return && brain.Reason == EnemyBrainReason.SearchExhausted && brain.Goal(target) == home,
            "Search expiry returns home.");
        Require(brain.Goal(home) == waypoint && brain.Mode == EnemyBrainMode.Patrol && brain.Reason == EnemyBrainReason.ReturnedHome,
            "Home arrival resumes the authored patrol route.");
    }

    private static void VerifySavedSearchDoesNotReplayNoise(EnemyBrainDefinition definition)
    {
        GridPoint home = new(0, 0);
        GridPoint noise = new(3, 1);
        HashSet<GridPoint> cells = [home, new(1, 0), noise];
        EnemyBrain original = new(definition, home, [home, new(1, 0)]);
        original.Observe(null, noise);
        original.Advance(1);
        EnemyBrain restored = EnemyBrain.FromSnapshot(definition, original.Capture(), cells);

        restored.Observe(null, null);
        Require(restored.Mode == EnemyBrainMode.Search && restored.LastKnownTarget == noise && restored.Reason == EnemyBrainReason.HeardNoise,
            "A saved search retains its prior attention without replaying a transient noise event.");
        restored.Advance(definition.MemorySeconds);
        Require(restored.Mode == EnemyBrainMode.Return && restored.LastKnownTarget is null,
            "Saved attention still decays using admitted resumed time.");
    }

    private static void VerifyRetreatIsBounded(EnemyBrainDefinition definition)
    {
        EnemyBrain brain = new(definition, new(0, 0), [new(0, 0), new(1, 0)]);
        brain.Observe(new(2, 0), null);
        Require(brain.NeedsRetreat(2) && brain.TrySpendRetreatStep(), "An engaged ranged enemy may take its first retreat step.");
        Require(!brain.TrySpendRetreatStep(), "Retreat cooldown prevents repeated movement in one instant.");
        brain.Advance(definition.RetreatCooldownSeconds);
        Require(brain.NeedsRetreat(2) && brain.TrySpendRetreatStep(), "Retreat resumes after its authored cooldown.");
        brain.Advance(definition.RetreatCooldownSeconds);
        Require(!brain.NeedsRetreat(2) && !brain.TrySpendRetreatStep(), "Retreat steps cannot exceed the engagement budget.");
    }

    private static void VerifySnapshotValidation(EnemyBrainDefinition definition)
    {
        GridPoint home = new(0, 0);
        EnemyBrain brain = new(definition, home, [home, new(1, 0)]);
        EnemyBrainSnapshot saved = brain.Capture();
        HashSet<GridPoint> cells = [home, new(1, 0)];
        RequireRejected(() => EnemyBrain.FromSnapshot(definition, saved with { PathRetryRemaining = double.NaN }, cells),
            "Non-finite persisted path timing is rejected.");
        RequireRejected(() => EnemyBrain.FromSnapshot(definition, saved with { PatrolIndex = 8 }, cells),
            "A saved patrol index must address the saved route.");
        RequireRejected(() => EnemyBrain.FromSnapshot(definition, saved with { MemoryRemaining = 1 }, cells),
            "Inactive patrol state cannot retain attention memory.");

        brain.Observe(null, new(1, 0));
        EnemyBrainSnapshot approachingSearch = brain.Capture();
        RequireRejected(() => EnemyBrain.FromSnapshot(definition, approachingSearch with { SearchRemaining = definition.SearchSeconds - 1 }, cells),
            "Search time cannot elapse before arrival at the last known cell.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireRejected(Action action, string message)
    {
        try { action(); }
        catch (Exception) { return; }
        throw new InvalidOperationException(message);
    }
}
