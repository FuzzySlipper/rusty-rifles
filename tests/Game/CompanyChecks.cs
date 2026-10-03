using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Party;

internal static class CompanyChecks
{
    internal static void Run(GameDefinitions definitions)
    {
        Check.Require(definitions.CompanyExperimentEnabled, "The company definitions are admitted when the experiment is enabled.");
        PartyState party = new(definitions.Party.Positions, definitions.Party.MaxPartySize,
            definitions.Characters, definitions.Characters.DefaultPresetId);
        Check.Require(party.Members.Count == 25 && party.Soldiers.Count == 24
            && definitions.Formation.Size == 5 && party.Commander!.Position == "commander",
            "A full 5x5 company has 24 soldiers around one fixed commander.");
        var forward = party.Soldiers.Where(member => definitions.Formation.Cell(member.Position).Forward > 0).ToArray();
        Check.Require(forward.Length == 10 && forward.All(member => definitions.Items.StartingItems.Any(item =>
            item.Owner == "member:" + member.InstanceId && item.Definition == "rifle" && item.Equipped
            && (item.Preset is null || item.Preset == definitions.Characters.DefaultPresetId))),
            "Both five-person forward ranks begin with equipped muskets.");
        Check.Require(definitions.Exploration.CellSize > 4 && definitions.FrontRank.HeightScale == 1.8f,
            "Room width grows while soldiers retain adult height.");
        FormationApproach approach = new(FormationAttackSector.Front, FormationScreeningLane.Center);
        FormationOccupant[] screens = [new("inner", "company-2-3", true), new("outer", "front-center", true)];
        Check.Require(FormationRules.SelectScreeningRecipient(definitions.Formation, approach, screens) == "outer"
            && FormationRules.SelectScreeningRecipient(definitions.Formation, approach,
                [screens[0], screens[1] with { Living = false }]) == "inner",
            "The outer rank screens first; a casualty exposes the inner rank.");
        Check.Require(FormationRules.DeriveOffensiveLane(1.3f, definitions.Formation.OffensiveLaneWidth, 2) == OffensiveLane.OuterLeft
            && FormationRules.DeriveOffensiveLane(-1.3f, definitions.Formation.OffensiveLaneWidth, 2) == OffensiveLane.OuterRight,
            "The two additional files have distinct offensive lanes.");
        Check.Rejected(() => (definitions.Formation with { Cells = definitions.Formation.Cells.Skip(1).ToArray() }).Validate(),
            "An incomplete company square is refused.");
        FormationPlannerChecks.Run(definitions);
        foreach (ulong seed in new ulong[] { 0, 1, 29, 83 })
        {
            DungeonFloor floor = DungeonFloor.Generate(seed, definitions.Generation, definitions.Rooms);
            Check.Require(floor.Cells.All(cell => floor.Level(cell) == 0), "Company floors use flat human-scale rooms.");
        }
        definitions.MartialDrills.ValidateAgainst(definitions.Characters, definitions.Formation, definitions.Combat, definitions.Crowd);
    }
}
