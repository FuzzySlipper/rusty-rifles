using Rifles.Game.Debugging;
using Rifles.Game.Content;
using Rifles.Game.Party;

internal static class MartialDrillChecks
{
    internal static void Run(GameDefinitions definitions)
    {
        MartialDrillDefinitionSet drills = definitions.MartialDrills;
        drills.ValidateAgainst(definitions.Characters, definitions.Formation, definitions.Combat, definitions.Crowd);
        Require(drills.Drills.Select(drill => drill.Id).ToHashSet(StringComparer.Ordinal)
            .SetEquals(["three-lanes", "concentration", "melee-reach", "commander-gap", "charge-clear", "charge-blocked"]),
            "The authored drills cover the visible martial-command matrix.");
        MartialDrillDefinition lanes = drills.Drill("three-lanes");
        Require(lanes.Enemies.Length == 3 && lanes.Enemies.Select(enemy => enemy.Left).Order().SequenceEqual([-2, 0, 2]),
            "Three lanes use three authored, distinct forward offsets.");
        Require(lanes.Members.Single(member => member.Member == "musketeer-b").Position == "front-center"
            && lanes.Members.Single(member => member.Member == "warden").Position == "front-left"
            && lanes.Members.Single(member => member.Member == "musketeer-a").Position == "front-right",
            "Three rifles occupy authored left, center and right formation lanes.");
        Require(drills.Drill("commander-gap").Members.Single(member => member.Member == "blade").Fallen,
            "The commander drill preserves an authored front-center casualty.");
        Require(drills.Drill("charge-blocked").ForwardBlocked && drills.Drill("charge-blocked").Enemies.Single().Forward == 1,
            "The blocked charge begins with the target in the first forward cell.");
        RequireRejected(() => new MartialDrillDefinitionSet([lanes, lanes]).Validate(), "Duplicate drill identities fail at content admission.");
        RequireRejected(() => (lanes with { Enemies = [lanes.Enemies[0] with { Forward = 0 }] }).Validate(),
            "Enemy offsets behind the party fail at content admission.");
        Console.WriteLine("Martial drill checks passed: authored formation, lanes, casualty and charge fixtures.");
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
