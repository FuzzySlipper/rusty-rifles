using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Procgen.Generation;

internal static class CrowdLaneChecks
{
    internal static void Run(GameDefinitions definitions)
    {
        GridPoint source = new(0, 0), east = new(1, 0);
        MovementGrid grid = new(new HashSet<GridPoint> { source, east }, (_, _) => true, definitions.Crowd);
        grid.Add(1, source, "small", "garrison", true, "nw");
        grid.Add(2, source, "small", "garrison", true, "ne");
        if (grid.TryReserve(1, east)) throw new InvalidOperationException("Rear actor crossed its stationary neighbor's departure lane.");
        if (!grid.TryReserve(2, east) || !grid.Commit(2)) throw new InvalidOperationException("Frontmost actor could not clear the lane.");
        if (!grid.TryReserve(1, east)) throw new InvalidOperationException("Cleared departure lane remained blocked.");
        try
        {
            grid.Add(3, source, "small", "garrison", true, "ne");
            throw new InvalidOperationException("New anchor entered an active departure lane.");
        }
        catch (InvalidDataException) { }
        grid.Cancel(1);
        grid.Add(3, source, "small", "garrison", true, "ne");
    }
}
