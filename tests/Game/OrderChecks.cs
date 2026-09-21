using Rifles.Game.Combat;
using Rifles.Game.Content;
using Rifles.Procgen.Generation;

internal static class OrderChecks
{
    internal static void Run(GameDefinitions definitions)
    {
        VerifyForwardLaneChoice(definitions.Formation);
        VerifyBlockedAndOutOfReachCandidates(definitions.Formation);
        VerifyCommittedOriginSnapshot();
        Console.WriteLine("Order checks passed: forward lanes, blocked/empty orders, and committed origin state.");
    }

    private static void VerifyForwardLaneChoice(Rifles.Game.Party.FormationDefinition formation)
    {
        ForwardOrderCandidate[] targets =
        [
            new(12, 6, .8f, true), new(8, 5, 0, true), new(4, 4, -.8f, true),
        ];
        Require(ForwardOrderRules.Select(formation, "musket", "front-left", targets)?.Target == 12
            && ForwardOrderRules.Select(formation, "musket", "front-center", targets)?.Target == 8
            && ForwardOrderRules.Select(formation, "musket", "front-right", targets)?.Target == 4,
            "Three soldiers distribute across their own forward lanes.");
        Require(ForwardOrderRules.Select(formation, "musket", "front-left", [new ForwardOrderCandidate(4, 4, -.8f, true)])?.Target == 4,
            "A lone exposed target remains available through permitted lane fallback.");
        Require(ForwardOrderRules.Select(formation, "musket", "front-center",
            [new ForwardOrderCandidate(9, 5, 0, true), new ForwardOrderCandidate(3, 5, 0, true)])?.Target == 3,
            "Stable target identity resolves an otherwise equal order choice.");
    }

    private static void VerifyBlockedAndOutOfReachCandidates(Rifles.Game.Party.FormationDefinition formation)
    {
        Require(ForwardOrderRules.Select(formation, "musket", "front-center", [new ForwardOrderCandidate(1, 5, 0, false)]) is null,
            "A first-body or world-blocked candidate produces no participant and no queued action.");
        Require(ForwardOrderRules.Select(formation, "short-sword", "front-center", [new ForwardOrderCandidate(1, 3, 0, true)]) is null,
            "An own-lane preference cannot turn an out-of-reach weapon into a participant.");
        Require(ForwardOrderRules.Assess(formation, "front-center", false, false, false, true, "musket", true, true, []).Reason == "Fallen."
            && ForwardOrderRules.Assess(formation, "front-center", true, true, false, true, "musket", true, true, []).Reason == "Busy."
            && ForwardOrderRules.Assess(formation, "front-center", true, false, true, true, "musket", true, true, []).Reason == "Repositioning."
            && ForwardOrderRules.Assess(formation, "front-center", true, false, false, false, null, true, false, []).Reason == "No weapon."
            && ForwardOrderRules.Assess(formation, "front-center", true, false, false, true, "musket", true, false, []).Reason == "Dry musket.",
            "The shared live-readiness helper explains fallen, busy, repositioning, unarmed, and dry soldiers without queuing them.");
        Require(ForwardOrderRules.InOriginalForwardArea(formation, "musket", 5, 1)
            && ForwardOrderRules.Select(formation, "musket", "front-center", [new ForwardOrderCandidate(3, 4, 0, true)]) is not null
            && !ForwardOrderRules.InOriginalForwardArea(formation, "musket", -1, 0),
            "Replacement requires the original forward area while current position supplies the legal reach candidate.");
    }

    private static void VerifyCommittedOriginSnapshot()
    {
        ActionSnapshot committed = new(CombatActionKind.Fire, 7, null, null, 9, null, .2, ActionPhase.Windup, .6,
            new GridPoint(5, 5), OrderOrigin: new GridPoint(2, 3), OrderFacing: CardinalDirection.East);
        ActionSnapshot restored = ActionState.Restore(committed).Capture()!;
        Require(restored.OrderOrigin == new GridPoint(2, 3) && restored.OrderFacing == CardinalDirection.East,
            "Committed attack origin and facing survive action-state persistence.");
        RequireRejected(() => ActionState.Restore(committed with { OrderFacing = null }), "A partial order origin is rejected.");
    }

    private static void RequireRejected(Action action, string message)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidOperationException(message);
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
