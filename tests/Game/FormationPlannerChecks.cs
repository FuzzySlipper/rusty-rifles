using Rifles.Game.Content;
using Rifles.Game.Party;

internal static class FormationPlannerChecks
{
    internal static void Run(GameDefinitions definitions)
    {
        PartyState Fresh() => new(definitions.Party.Positions, definitions.Party.MaxPartySize,
            definitions.Characters, definitions.Characters.DefaultPresetId);
        const double duration = 2;
        PartyState party = Fresh();
        FormationPlanner planner = new(party);
        string first = party.Soldiers[0].InstanceId, second = party.Soldiers[1].InstanceId;
        string firstPosition = party.Soldiers[0].Position, secondPosition = party.Soldiers[1].Position;
        planner.Begin(true);
        planner.Place(first, secondPosition);
        Require(party.Soldiers[0].Position == firstPosition && planner.PlannedMoves().Length == 2,
            "Draft swaps never change live defensive positions.");
        planner.Cancel();
        Require(!planner.Open && planner.PreviouslyPaused && !planner.Executing && party.Soldiers[0].Position == firstPosition,
            "Cancellation preserves arrangement and prior pause state.");
        planner.Begin(false);
        Require(!planner.Execute(duration) && !planner.Executing, "No-op draft incurs no transition.");
        planner.Begin(false);
        Reject(() => planner.Place(first, "commander"), "Commander center stays reserved.");
        planner.Place(first, secondPosition);
        Require(planner.Execute(duration) && planner.Affects(first) && planner.Affects(second)
            && !planner.Affects(party.Soldiers[2].InstanceId), "Only changed soldiers participate.");
        Reject(() => planner.Begin(false), "Active execution cannot be replaced by another draft.");
        planner.Advance(duration / 2);
        Require(party.Soldiers[0].Position == firstPosition && planner.Executing, "Old screening remains during execution.");
        FormationExecutionSnapshot saved = planner.Capture()!;
        PartyState restoredParty = Fresh();
        FormationPlanner restored = new(restoredParty);
        restored.Restore(saved, duration);
        Require(!restored.Open && restored.Execution!.Remaining == duration / 2, "Only execution resumes from saves.");
        restoredParty.Soldiers[0].ApplyDamage(long.MaxValue);
        restored.Advance(duration / 2);
        Require(!restored.Executing && restoredParty.Soldiers[0].Position == secondPosition
            && restoredParty.Soldiers[1].Position == firstPosition && !restoredParty.Soldiers[0].IsLiving,
            "Final swap is simultaneous and preserves casualties incurred during execution.");
        Reject(() => new FormationPlanner(Fresh()).Restore(saved with { Moves = [saved.Moves[0]] }, duration),
            "A saved one-sided swap cannot duplicate an occupied destination.");
        Console.WriteLine("Formation planner checks passed: draft, swap, cancellation, timed commit, casualties and restoration.");
    }

    private static void Reject(Action action, string message)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidOperationException(message);
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
