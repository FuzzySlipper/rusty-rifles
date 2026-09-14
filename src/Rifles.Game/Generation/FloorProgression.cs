using Rifles.Game.Dungeon;
using Rifles.Procgen.Generation;

namespace Rifles.Game.Generation;

internal sealed record FloorProgressionReport(bool Accepted, string[] Diagnostics, GridPoint[] Reached, string[] AcquiredKeys);

/// <summary>Offline bounded physical item-state validation, never an actor navigation implementation.</summary>
internal static class FloorProgression
{
    internal static FloorProgressionReport Inspect(DungeonFloor floor, GeneratedGate[] gates, GeneratedPlate[]? plates = null)
    {
        plates ??= [];
        HashSet<string> keys = [];
        HashSet<string> opened = [];
        HashSet<GridPoint> reached = [];
        var cells = floor.Cells.ToHashSet();
        // Every successful pass acquires a key or opens a gate; the bound is the finite artifact size.
        int passes = gates.Length + floor.Grants.Length + 1;
        for (int pass = 0; pass <= passes; pass++)
        {
            var blocked = gates.Where(g => !opened.Contains(g.RouteId)).Select(g => g.Cell).ToHashSet();
            reached = [];
            Queue<GridPoint> queue = new();
            if (!blocked.Contains(floor.Entrance)) { reached.Add(floor.Entrance); queue.Enqueue(floor.Entrance); }
            while (queue.TryDequeue(out var cell))
                foreach (var direction in CardinalDirections.Ordered)
                {
                    var next = cell + direction.Offset();
                    if (cells.Contains(next) && !blocked.Contains(next) && reached.Add(next)) queue.Enqueue(next);
                }
            bool changed = false;
            foreach (var grant in floor.Grants)
                if (reached.Contains(grant.Cell) && keys.Add(grant.Item)) changed = true;
            foreach (var gate in gates)
                if (reached.Contains(gate.Approach) && (gate.RequiredItem is null || keys.Contains(gate.RequiredItem))
                    && plates.Where(p => p.GateId == gate.Id).All(p => reached.Contains(p.Cell) && reached.Contains(p.WeightSource))
                    && opened.Add(gate.RouteId)) changed = true;
            if (!changed) break;
        }
        List<string> diagnostics = [];
        if (!reached.Contains(floor.Exit)) diagnostics.Add("physical_objective_unreachable");
        foreach (var grant in floor.Grants.Where(g => !keys.Contains(g.Item))) diagnostics.Add("inaccessible_key:" + grant.Item);
        foreach (var gate in gates.Where(g => !opened.Contains(g.RouteId))) diagnostics.Add("inaccessible_gate_handle:" + gate.RouteId);
        return new(diagnostics.Count == 0, diagnostics.ToArray(), reached.OrderBy(c => c.Y).ThenBy(c => c.X).ToArray(), keys.Order().ToArray());
    }
}
