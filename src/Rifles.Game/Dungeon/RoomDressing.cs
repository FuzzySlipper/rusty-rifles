using Rifles.Game.Content;
using Rifles.Procgen.Generation;

namespace Rifles.Game.Dungeon;

internal sealed record RoomDressing(ulong BenchId, GridPoint Bench, ulong CrateId, GridPoint Crate,
    ulong ObserverId, GridPoint Observer)
{
    internal static RoomDressing Create(DungeonFloor floor, PatrolActor actor, WorldArtDefinition art, Func<ulong> allocate)
    {
        HashSet<GridPoint> occupied = [actor.Capture().Start, actor.Capture().End];
        GridPoint Place(int[] offset)
        {
            GridPoint desired = floor.Entrance + new GridPoint(offset[0], offset[1]);
            GridPoint cell = floor.Cells.Where(c => !occupied.Contains(c))
                .OrderBy(c => c.ManhattanDistance(desired)).ThenBy(c => c.Y).ThenBy(c => c.X)
                .Where(c => DressingPlacement.CanBlock(floor, occupied.Append(c))).Select(c => (GridPoint?)c).FirstOrDefault()
                ?? throw new InvalidDataException("No route-preserving room dressing placement is available.");
            occupied.Add(cell);
            return cell;
        }
        return new(allocate(), Place(art.BenchOffset), allocate(), Place(art.CrateOffset), allocate(), Place(art.ObserverOffset));
    }
    internal void Validate(DungeonFloor floor)
    {
        GridPoint[] cells = [Bench, Crate, Observer];
        GameDefinitions.Require(cells.Distinct().Count() == cells.Length && cells.All(floor.Cells.Contains)
            && !cells.Contains(floor.Exit), "Saved room dressing");
    }
    internal void Bind(MovementGrid grid, bool observerAlive = true)
    {
        // These objects occupy authored grid cells. Alpha silhouettes never define collision.
        grid.Add(BenchId, Bench);
        grid.Add(CrateId, Crate);
        if (observerAlive) grid.Add(ObserverId, Observer);
    }
}
