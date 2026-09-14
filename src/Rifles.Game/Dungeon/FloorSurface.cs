using System.Numerics;
using Rifles.Procgen.Generation;

namespace Rifles.Game.Dungeon;

/// <summary>Presentation support height for the same authored voxel stair treads.</summary>
internal static class FloorSurface
{
    internal static int TreadHeight(int x, int z, int dx, int dz, int resolution) =>
        1 + (dx > 0 ? x : dx < 0 ? resolution - 1 - x : dz > 0 ? z : resolution - 1 - z);

    internal static float Height(DungeonFloor floor, Vector2 point, float cellSize, int resolution)
    {
        float Base(GridPoint cell) => (1 + floor.Level(cell)) * cellSize;
        int x = (int)MathF.Floor(point.X), z = (int)MathF.Floor(point.Y);
        float north = float.Lerp(Base(new(x, z)), Base(new(x + 1, z)), point.X - x);
        float south = float.Lerp(Base(new(x, z + 1)), Base(new(x + 1, z + 1)), point.X - x);
        float interpolated = float.Lerp(north, south, point.Y - z);
        // Logical points are cell centers; voxel columns are measured from cell corners.
        var under = new GridPoint((int)MathF.Floor(point.X + .5f), (int)MathF.Floor(point.Y + .5f));
        float support = Base(under);
        if (floor.Level(under) >= 0)
        {
            int vx = Math.Clamp((int)((point.X + .5f - under.X) * resolution), 0, resolution - 1);
            int vz = Math.Clamp((int)((point.Y + .5f - under.Y) * resolution), 0, resolution - 1);
            foreach (var edge in floor.Connectors.Where(c => c.From == under && floor.Level(c.To) == floor.Level(under) + 1))
                support = Math.Max(support, Base(under) + TreadHeight(vx, vz,
                    edge.To.X - under.X, edge.To.Y - under.Y, resolution) * cellSize / resolution);
        }
        return Math.Max(interpolated, support);
    }
}
