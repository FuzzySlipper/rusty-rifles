using Rifles.Game.Content;
using Rifles.Game.Party;
using Rifles.Game.Characters;

internal static class CommanderChecks
{
    internal static void Run(GameDefinitions definitions)
    {
        PartyState Fresh() => new(definitions.Party.Positions, definitions.Party.MaxPartySize,
            definitions.Characters, definitions.Characters.DefaultPresetId);
        PartyState party = Fresh();
        RiflesCharacter commander = party.Commander ?? throw new Exception("Commander missing.");
        Require(party.Members.Count == 7 && party.Soldiers.Count == 6 && commander.Position == "commander", "Six soldiers surround one commander.");
        Require(!party.MoveFormation(commander.InstanceId, "left-guard")
            && !party.MoveFormation("warden", commander.Position)
            && !party.SwapFormation("warden", commander.InstanceId), "Commander center is fixed at authoritative mutation boundary.");
        foreach (PartyReach reach in Enum.GetValues<PartyReach>())
            Require(!party.CanUseReach(commander.InstanceId, reach), "Commander has no direct attack or cast reach.");
        FormationApproach front = new(FormationAttackSector.Front, FormationScreeningLane.Center);
        Require(party.ScreenedRecipient(definitions.Formation, front)?.InstanceId == "blade", "Front-center soldier screens commander.");
        party.Members.Single(member => member.InstanceId == "blade").ApplyDamage(long.MaxValue);
        Require(party.ScreenedRecipient(definitions.Formation, front) == commander && party.Soldiers.Count(member => member.IsLiving) == 5,
            "A central casualty exposes commander while other soldiers live.");
        var saved = party.Capture();
        commander.ApplyDamage(long.MaxValue);
        Require(party.Defeated && party.Soldiers.Any(member => member.IsLiving), "Commander death ends run independently of soldiers.");
        var defeated = party.Capture();
        PartyState restored = Fresh(); restored.Restore(defeated);
        Require(restored.Defeated && restored.Soldiers.Any(member => member.IsLiving), "Defeat survives restored stats and roles.");
        restored.Restore(saved);
        foreach (var soldier in restored.Soldiers) soldier.ApplyDamage(long.MaxValue);
        Require(!restored.Defeated && restored.Commander!.IsLiving, "Soldier loss alone permits commander retreat.");
        var displaced = saved.Select(member => member.Id == commander.InstanceId ? member with { Position = "left-guard" } : member).ToArray();
        try { Fresh().Restore(displaced); throw new Exception("Moved saved commander accepted."); }
        catch (InvalidOperationException) { }
        Console.WriteLine("Commander checks passed: reserved center, screening, casualty gaps, defeat and restore.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
