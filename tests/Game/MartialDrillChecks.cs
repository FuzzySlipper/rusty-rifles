using Rifles.Game.Debugging;
using Rifles.Game.Content;
using Rifles.Game.Expedition;
using Rifles.Game.Items;
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
        MartialDrillDefinition chargeClear = drills.Drill("charge-clear");
        Require(chargeClear.Enemies.Single() is { Forward: 2, Placement: "south" }
            && chargeClear.InitialEnemyDecisionDelaySeconds == definitions.Combat.Enemy("raider").DecisionSeconds,
            "The live charge fixture keeps its one-step contact target in bayonet reach until its normal first decision.");
        RequireRejected(() => new MartialDrillDefinitionSet([lanes, lanes]).Validate(), "Duplicate drill identities fail at content admission.");
        RequireRejected(() => (lanes with { Enemies = [lanes.Enemies[0] with { Forward = 0 }] }).Validate(),
            "Enemy offsets behind the party fail at content admission.");
        RequireRejected(() => (chargeClear with { InitialEnemyDecisionDelaySeconds = double.NaN }).Validate(),
            "Invalid drill decision delay fails at content admission.");
        Console.WriteLine("Martial drill checks passed: authored formation, lanes, casualty and charge fixtures.");
    }

    /// <summary>Exercises the same saved-floor admission path used by a live debug command, without an Engine host.</summary>
    internal static void VerifySnapshot(GameDefinitions definitions, ExpeditionSnapshot snapshot)
    {
        ExpeditionSnapshot? concentration = null;
        foreach (MartialDrillDefinition drill in definitions.MartialDrills.Drills)
        {
            ExpeditionSnapshot prepared = Rifles.Game.RiflesProduct.PrepareMartialDrill(definitions, snapshot, drill);
            _ = ExpeditionCodec.Validate(prepared, definitions);
            if (drill.Id == "concentration") concentration = prepared;
        }
        ExpeditionSnapshot preparedConcentration = concentration
            ?? throw new InvalidOperationException("The concentration drill is required.");

        Require(preparedConcentration.Combat.SelectedTarget == 0 && preparedConcentration.Paused,
            "A drill starts paused and has no selected-enemy dependency.");
        Require(preparedConcentration.Combat.Enemies.Select(enemy => enemy.Spawn).SequenceEqual(definitions.MartialDrills.Drill("concentration").Enemies.Select(enemy => enemy.SpawnId)),
            "A drill retains exactly its authored enemy identities.");
        HashSet<string> inventoryCombatOwners = preparedConcentration.Inventory.Packs.Where(pack => InventoryOwner.Parse(pack.Owner.Key) is CombatOwner)
            .Select(pack => pack.Owner.Key).ToHashSet(StringComparer.Ordinal);
        HashSet<string> expectedCombatOwners = preparedConcentration.Combat.Enemies.Select(enemy => enemy.Owner)
            .Concat(preparedConcentration.Combat.Drops.Select(drop => drop.Owner))
            .Concat(preparedConcentration.Combat.Flights.Where(flight => flight.Owner is not null).Select(flight => flight.Owner!))
            .ToHashSet(StringComparer.Ordinal);
        Require(inventoryCombatOwners.SetEquals(expectedCombatOwners),
            "The drill culls enemy packs and preserves every remaining combat owner in the saved ledger.");
        Console.WriteLine("Martial drill snapshot checks passed: prepared fixture restores through normal save admission.");
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
