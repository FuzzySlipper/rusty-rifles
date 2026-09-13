namespace Rifles.Procgen.Generation;

/// <summary>Artifact admission only; does not plan runtime movement or return routes.</summary>
public static class ResolvedGridValidation
{
    public static bool IsConnected(IReadOnlySet<GridPoint> cells, GridPoint entrance)
    {
        if (!cells.Contains(entrance)) return false;
        HashSet<GridPoint> reached = [entrance];
        Queue<GridPoint> pending = new(); pending.Enqueue(entrance);
        while (pending.TryDequeue(out GridPoint cell))
            foreach (CardinalDirection direction in CardinalDirections.Ordered)
            {
                GridPoint next = cell + direction.Offset();
                if (cells.Contains(next) && reached.Add(next)) pending.Enqueue(next);
            }
        return reached.Count == cells.Count;
    }
}
