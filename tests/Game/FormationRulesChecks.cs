using System.Numerics;
using Rifles.Game.Content;
using Rifles.Game.Party;
using Rifles.Procgen.Generation;

internal static class FormationRulesChecks
{
    internal static void Run(GameDefinitions definitions)
    {
        FormationDefinition formation = definitions.Formation;
        VerifyApproachRotationAndCrowdOffsets(formation);
        VerifyScreeningGapsAndCommanderFallback(formation);
        VerifyWeaponReachAndPreferences(formation);
        VerifyInvalidDefinitions(formation);
        Console.WriteLine("Formation checks passed: rotated approach lanes, explicit gaps, reach, and stable preference.");
    }

    private static void VerifyApproachRotationAndCrowdOffsets(FormationDefinition formation)
    {
        FormationApproach north = FormationRules.DeriveApproach(CardinalDirection.North, new(0, -1), formation.LaneBoundaryRatio);
        FormationApproach east = FormationRules.DeriveApproach(CardinalDirection.East, new(1, 0), formation.LaneBoundaryRatio);
        FormationApproach south = FormationRules.DeriveApproach(CardinalDirection.South, new(0, 1), formation.LaneBoundaryRatio);
        FormationApproach west = FormationRules.DeriveApproach(CardinalDirection.West, new(-1, 0), formation.LaneBoundaryRatio);
        Require(north == east && east == south && south == west && north == new FormationApproach(FormationAttackSector.Front, FormationScreeningLane.Center),
            "Quarter turns preserve a front-center approach.");
        Require(FormationRules.DeriveApproach(CardinalDirection.North, new(-.5f, -.8f), formation.LaneBoundaryRatio)
                == new FormationApproach(FormationAttackSector.Front, FormationScreeningLane.Left),
            "A nearby hostile crowd offset selects the real front-left band instead of a cell center.");
        Require(FormationRules.DeriveApproach(CardinalDirection.North, new(.05f, -1f), formation.LaneBoundaryRatio).Lane == FormationScreeningLane.Center,
            "The relative angle band keeps small lateral spread in the center at distance.");
        RequireRejected(() => FormationRules.DeriveApproach(CardinalDirection.North, Vector2.Zero, formation.LaneBoundaryRatio),
            "Directionless sources require their own caller policy.");
    }

    private static void VerifyScreeningGapsAndCommanderFallback(FormationDefinition formation)
    {
        FormationOccupant[] intact =
        [
            new("alpha", "front-left", true), new("bravo", "front-center", true), new("charlie", "front-right", true),
            new("left", "left-guard", true), new("right", "right-guard", true), new("rear", "rear-center", true),
        ];
        FormationApproach frontLeft = new(FormationAttackSector.Front, FormationScreeningLane.Left);
        FormationApproach frontCenter = new(FormationAttackSector.Front, FormationScreeningLane.Center);
        Require(FormationRules.SelectScreeningRecipient(formation, frontLeft, intact) == "alpha"
            && FormationRules.SelectScreeningRecipient(formation, frontCenter, intact) == "bravo", "Intact front screens its own lanes.");
        FormationOccupant[] cornersOnly = [new("alpha", "front-left", true), new("charlie", "front-right", true)];
        Require(FormationRules.SelectScreeningRecipient(formation, frontCenter, cornersOnly) is null,
            "Live corners do not silently cover a missing front-center lane; commander is exposed.");
        Require(FormationRules.SelectScreeningRecipient(formation, new(FormationAttackSector.Left, FormationScreeningLane.Center),
            [new FormationOccupant("left", "left-guard", true)]) == "left", "One surviving flank guard covers only its flank center.");
        Require(FormationRules.SelectScreeningRecipient(formation, new(FormationAttackSector.Left, FormationScreeningLane.Front),
            [new FormationOccupant("left", "left-guard", true)]) is null, "A flank guard is not a universal edge shield.");
        Require(FormationRules.SelectScreeningRecipient(formation, new(FormationAttackSector.Rear, FormationScreeningLane.Center), intact) == "rear",
            "Rear attacks use the independently authored rear screening entry.");
        Require(FormationRules.SelectScreeningRecipient(formation, frontLeft,
            [new FormationOccupant("fallen", "front-left", false)]) is null, "Dead residents do not screen and damage does not spill to a second target.");
    }

    private static void VerifyWeaponReachAndPreferences(FormationDefinition formation)
    {
        FormationCellDefinition frontCenter = formation.Cell("front-center");
        FormationCellDefinition rearCenter = formation.Cell("rear-center");
        Require(FormationRules.CanReach(formation, formation.Weapon("short-sword"), formation.Cell("front-left"), new("left", 2, .6f, true))
            && !FormationRules.CanReach(formation, formation.Weapon("short-sword"), formation.Cell("front-left"), new("center", 2, 0, true)),
            "A left short sword can strike its own lane, not the party center lane.");
        FormationCellDefinition frontLeft = formation.Cell("front-left");
        Require(FormationRules.CanReach(formation, formation.Weapon("short-sword"), frontCenter, new("direct", 2, 0, true))
            && FormationRules.CanReach(formation, formation.Weapon("short-sword"), frontLeft, new("own-left", 2, .8f, true))
            && !FormationRules.CanReach(formation, formation.Weapon("short-sword"), frontCenter, new("side", 2, .8f, true)),
            "Short swords reach directly ahead from the front rank only.");
        Require(FormationRules.CanReach(formation, formation.Weapon("sword"), frontCenter, new("adjacent", 2, .8f, true))
            && !FormationRules.CanReach(formation, formation.Weapon("sword"), rearCenter, new("rear", 2, 0, true)),
            "Swords cover adjacent frontage from the front rank only.");
        Require(FormationRules.CanReach(formation, formation.Weapon("pike"), rearCenter, new("far", 3, 0, true))
            && FormationRules.CanReach(formation, formation.Weapon("fixed-bayonet"), rearCenter, new("near", 2, 0, true)),
            "Pikes and fixed bayonets extend forward past friendly positions.");
        FormationTarget[] volley =
        [
            new("left", 6, .8f, true), new("center", 5, 0, true), new("right", 4, -.8f, true),
            new("too-far", 20, 0, true),
        ];
        Require(FormationRules.SelectPreferredTarget(formation, "musket", "front-left", volley) == "left"
            && FormationRules.SelectPreferredTarget(formation, "musket", "front-center", volley) == "center"
            && FormationRules.SelectPreferredTarget(formation, "musket", "front-right", volley) == "right",
            "Three lanes prefer their own exposed targets.");
        Require(FormationRules.SelectPreferredTarget(formation, "musket", "front-left", [new FormationTarget("only", 5, -.8f, true)]) == "only",
            "Permitted lane fallbacks let a volley concentrate on one valid enemy.");
        Require(FormationRules.SelectPreferredTarget(formation, "musket", "front-center",
            [new FormationTarget("zeta", 5, 0, true), new FormationTarget("alpha", 5, 0, true)]) == "alpha",
            "Stable identity breaks a remaining targeting tie.");
        Require(FormationRules.SelectPreferredTarget(formation, "short-sword", "front-center",
            [new FormationTarget("preferred-but-sideways", 2, .8f, true)]) is null,
            "Lane preference cannot expand a weapon's physical reach.");
        Require(FormationRules.DeriveOffensiveLane(.8f, formation.OffensiveLaneWidth) == OffensiveLane.Left
            && FormationRules.DeriveOffensiveLane(0, formation.OffensiveLaneWidth) == OffensiveLane.Center,
            "Offensive lanes derive once from actual party-relative crowd offsets.");
    }

    private static void VerifyInvalidDefinitions(FormationDefinition formation)
    {
        RequireRejected(() => (formation with { LaneBoundaryRatio = 1 }).Validate(), "Invalid lane ratio is rejected at content admission.");
        RequireRejected(() => (formation with { Cells = formation.Cells.Where(cell => !cell.Commander).ToArray() }).Validate(), "Missing commander center is rejected.");
        RequireRejected(() => (formation with { Weapons = [.. formation.Weapons, formation.Weapons[0]] }).Validate(), "Duplicate weapon ids are rejected.");
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
