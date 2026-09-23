using Rifles.Game.Dungeon;
using Rifles.Procgen.Generation;

internal static class CrowdChecks
{
    internal static void Run()
    {
        GeometryAndCapacityAreBothRequired();
        SharingRequiresAnOptedInFaction();
        PartyCellStaysExclusiveDuringSteps();
        MixedFootprintCostsAreBounded();
        EdgeClearanceUsesTheMovingProfile();
        WaitingIntentIsFairAndCancellable();
        RestoreKeepsAuthoredAnchors();
    }

    private static void GeometryAndCapacityAreBothRequired()
    {
        MovementGrid grid = Grid(capacity: 2);
        grid.Add(1, new GridPoint(0, 0), "left-only", "rats", share: true);
        Require(!grid.CanFit(new GridPoint(0, 0), "left-only", "rats", share: true), "A matching quadrant must reject even below capacity.");
        grid.Add(2, new GridPoint(0, 0), "right-only", "rats", share: true);
        Require(!grid.CanFit(new GridPoint(0, 0), "left-only", "rats", share: true), "Capacity must reject even with an unused mask.");
    }

    private static void SharingRequiresAnOptedInFaction()
    {
        MovementGrid grid = Grid(capacity: 3);
        grid.Add(1, new GridPoint(0, 0), "small", "rats", share: true, "left");
        Require(!grid.CanFit(new GridPoint(0, 0), "small", "rats", share: false), "Both actors must opt into sharing.");
        Require(!grid.CanFit(new GridPoint(0, 0), "small", "guards", share: true), "Different factions remain exclusive.");
        Require(grid.CanFit(new GridPoint(0, 0), "small", "rats", share: true), "Same faction may use a disjoint authored placement.");
    }

    private static void PartyCellStaysExclusiveDuringSteps()
    {
        MovementGrid grid = Grid(capacity: 3);
        grid.Add(1, new GridPoint(1, 0));
        grid.Add(2, new GridPoint(0, 0), "small", "rats", share: true);
        Require(!grid.CanFit(2, new GridPoint(1, 0)) && !grid.TryReserve(2, new GridPoint(1, 0)),
            "A crowd enemy cannot enter the party's occupied cell.");
        Require(grid.TryReserve(1, new GridPoint(2, 0)), "The party can reserve its next cell.");
        Require(!grid.CanFit(2, new GridPoint(1, 0)) && !grid.TryReserve(2, new GridPoint(1, 0)),
            "The party's source cell stays exclusive while it moves.");
        Require(!grid.CanFit(2, new GridPoint(2, 0)), "The party's reserved destination stays exclusive.");
    }

    private static void MixedFootprintCostsAreBounded()
    {
        MovementGrid grid = Grid(capacity: 2);
        grid.Add(1, new GridPoint(0, 0), "large", "rats", share: true, "top");
        Require(!grid.CanFit(new GridPoint(0, 0), "right-only", "rats", share: true), "A large footprint cost consumes the whole bounded capacity.");
    }

    private static void EdgeClearanceUsesTheMovingProfile()
    {
        MovementGrid grid = Grid(capacity: 3);
        grid.Add(1, new GridPoint(0, 0), "small", "rats", share: true, "left");
        grid.SetClearance(new GridPoint(0, 0), new GridPoint(1, 0), .24f);
        Require(!grid.TryReserve(1, new GridPoint(1, 0)), "A profile must not cross an authored edge narrower than its clearance.");
        grid.SetClearance(new GridPoint(0, 0), new GridPoint(1, 0), .25f);
        Require(grid.TryReserve(1, new GridPoint(1, 0)), "An edge exactly at the profile clearance is admitted.");
        grid.Cancel(1);
    }

    private static void WaitingIntentIsFairAndCancellable()
    {
        MovementGrid grid = Grid(capacity: 3, waitingAttemptLimit: 3);
        grid.Add(1, new GridPoint(0, 0), "small", "rats", share: true, "left");
        grid.Add(2, new GridPoint(2, 0), "small", "rats", share: true, "right");
        grid.Add(3, new GridPoint(1, 0), "left-only", "rats", share: true);

        Require(!grid.TryReserve(1, new GridPoint(1, 0), "left"), "Occupied destination creates the first waiting intent.");
        grid.Remove(3);
        Require(!grid.TryReserve(2, new GridPoint(1, 0)), "A newer contender yields to the older waiting intent.");
        Require(grid.Blocked(1)?.Attempts == 2, "Yielding advances the older intent toward a bounded expiry.");
        Require(grid.TryReserve(1, new GridPoint(1, 0), "left") && grid.Commit(1), "The oldest actor can take the released destination.");

        grid.Add(3, new GridPoint(0, 0), "left-only", "rats", share: true);
        Require(!grid.TryReserve(3, new GridPoint(1, 0)), "A blocked move exposes a waiting state.");
        grid.Cancel(3);
        Require(grid.Blocked(3) is null, "Cancellation removes the pending fairness intent and its stale blocked presentation state.");
    }

    private static void RestoreKeepsAuthoredAnchors()
    {
        MovementGrid grid = Grid(capacity: 3);
        grid.Add(1, new GridPoint(0, 0), "small", "rats", share: true, "left");
        Require(grid.RestoreReservation(1, new GridPoint(1, 0), "left", "right"), "Saved source and destination placement ids restore an in-transit actor.");
        Require(grid.Placement(1).Id == "left" && grid.DestinationPlacement(1)?.Id == "right", "Source stays stable while the reservation exposes its exact destination anchor.");
        Require(grid.Commit(1) && grid.Placement(1).Id == "right", "Commit adopts the saved destination anchor.");

        grid.Add(9, new GridPoint(2, 0));
        Require(grid.Placement(9).Id == "exclusive", "Legacy actors retain a stable exclusive placement identity.");
    }

    private static MovementGrid Grid(int capacity, int waitingAttemptLimit = 4)
    {
        CrowdDefinition crowd = new(capacity, waitingAttemptLimit, new Dictionary<string, FootprintDefinition>
        {
            ["small"] = new(1, 1, .25f,
            [
                new PlacementDefinition("left", 0b0001, -.25f, .25f, .5f, .5f),
                new PlacementDefinition("right", 0b0010, .25f, .25f, .5f, .5f),
            ]),
            ["left-only"] = new(1, 1, .25f,
            [
                new PlacementDefinition("left", 0b0001, -.25f, .25f, .5f, .5f),
            ]),
            ["right-only"] = new(1, 1, .25f,
            [
                new PlacementDefinition("right", 0b0010, .25f, .25f, .5f, .5f),
            ]),
            ["large"] = new(2, 2, .5f,
            [
                new PlacementDefinition("top", 0b0011, 0, .25f, 1, .5f),
                new PlacementDefinition("bottom", 0b1100, 0, -.25f, 1, .5f),
            ]),
        });
        HashSet<GridPoint> cells = [new(0, 0), new(1, 0), new(2, 0)];
        return new MovementGrid(cells, (_, _) => true, crowd);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
