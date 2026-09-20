using Rifles.Game.Presentation;
using System.Text;

internal static class CommandChecks
{
    internal static void Run()
    {
        // Two independent commands from one UI projection parse to independent
        // dispatchable shapes: no shared freshness gate couples or discards
        // the later command. Genuine rules (reach, capacity, busy, revisioned
        // ledger atomicity, feature impact rechecks) still enforce at act time.
        SessionCommand first = Parse(new { action = "attack", member = "warden" });
        SessionCommand second = Parse(new { action = "attack", member = "sentry" });
        Require(first.Action == "attack" && first.Member == "warden"
            && second.Action == "attack" && second.Member == "sentry",
            "Sequential UI commands parse independently with their own identity.");

        SessionCommand cased = Parse(new { Action = "Rest", Member = "Warden" });
        Require(cased.Action == "Rest" && cased.Member == "Warden",
            "Boundary parsing accepts Engine-delivered casing without per-call options.");

        SessionCommand feature = Parse(new { action = "use", target = 7, targetRevision = 3 });
        Require(feature.Target == 7 && feature.TargetRevision == 3,
            "Feature commands retain their impact revision for delayed rechecks.");

        Console.WriteLine("Command checks passed: independent queued commands, shared parsing, and feature impact revisions.");
    }

    private static SessionCommand Parse(object payload) =>
        SessionCommand.Parse(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(payload)));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
