using System.Numerics;
using Rusty.Engine;
using Rifles.Procgen.Generation;

namespace Rifles.Game.Dungeon;

/// <summary>Publishes the resolved grid through Engine voxels and navigation.</summary>
internal sealed class DungeonScene : IDisposable
{
    internal const float CellSize = 2f;
    private const int ChunkSize = 16;
    private const int FloorY = 0;
    private const int NavigationY = 1;
    private const int CeilingY = 3;
    private const ulong GridId = 1;
    private const uint StoneSlot = 1, ExitSlot = 2;
    private readonly IEngineContext engine;
    private readonly SpatialSession spatial;
    private readonly List<Material> materials = [];
    private readonly List<Light> lights = [];
    private VoxelScenePresentation? scene;

    internal DungeonScene(IEngineContext engine, DungeonFloor floor)
    {
        this.engine = engine;
        spatial = engine.Spatial.CreateSession(new SpatialSessionConfig(CellSize, ChunkSize, VoxelSurfaceMode.GreedyCubes));
        try
        {
            materials.Add(engine.Graphics.CreateMaterial(new MaterialRequest(new Color(.35f, .32f, .27f, 1),
                new RenderResourceHandle(0), .95f, new Color(1, 1, 1, 1), Vector3.Zero, 0, false)));
            materials.Add(engine.Graphics.CreateMaterial(new MaterialRequest(new Color(.25f, .52f, .43f, 1),
                new RenderResourceHandle(0), .8f, new Color(1, 1, 1, 1), new Vector3(.08f, .2f, .12f), 0, false)));
            IReadOnlySet<GridPoint> cells = floor.Geometry.WalkableCells;
            HashSet<GridPoint> walls = cells.SelectMany(cell => CardinalDirections.Ordered.Select(d => cell + d.Offset()))
                .Where(cell => !cells.Contains(cell)).ToHashSet();
            List<VoxelEdit> edits = [];
            foreach (GridPoint cell in cells)
            {
                edits.Add(new(VoxelEditKind.Set, new VoxelAddress(cell.X, FloorY, cell.Y), cell == floor.Exit ? ExitSlot : StoneSlot));
                edits.Add(new(VoxelEditKind.Set, new VoxelAddress(cell.X, CeilingY, cell.Y), StoneSlot));
            }
            foreach (GridPoint wall in walls)
                for (int y = FloorY; y <= CeilingY; y++)
                    edits.Add(new(VoxelEditKind.Set, new VoxelAddress(wall.X, y, wall.Y), StoneSlot));
            VoxelSceneReadout before = engine.Voxel.ReadScene(new VoxelSceneReadRequest(spatial));
            engine.Voxel.ApplyEdits(new VoxelEditTransaction(spatial, before.SourceRevision, edits.ToArray()));
            engine.Spatial.ReplaceNavigation(new NavigationReplaceRequest(spatial,
                new PlanarNavConfig(GridId, CellSize, ChunkSize, 1),
                cells.Select(c => new PlanarNavCell(c.X, NavigationY, c.Y)).ToArray()));
            scene = engine.VoxelScenePresentation.ProjectScene(new ProjectVoxelSceneRequest(spatial,
                new VoxelSceneMaterialBinding[] { new(StoneSlot, materials[0]), new(ExitSlot, materials[1]) }));
            ulong lightId = 1;
            foreach (PlacedPiece room in floor.Geometry.Pieces)
            {
                Vector3 position = Eye(room.Origin + new GridPoint(2, 2));
                lights.Add(engine.Graphics.CreateLight(new LightRequest(lightId++, false, 0,
                    new LightDescriptor(LightKind.Point, new Vector3(1f, .78f, .5f), 5f,
                        true, position, Vector3.UnitY, true, 24f, 1f, 0, 0, LightShadowIntent.Disabled))));
            }
        }
        catch { Dispose(); throw; }
    }

    internal bool AdmitStep(GridPoint from, GridPoint destination)
    {
        if (from.ManhattanDistance(destination) != 1) return false;
        NavigationStepReceipt step = engine.Spatial.EvaluateNavigationStep(new NavigationStepRequest(
            spatial, NavigationCenter(from), NavigationCenter(destination), CellSize, 128));
        return step.Outcome == NavigationPathOutcome.Reached && step.Reached != 0
            && step.NextPathCell == new PlanarNavCell(destination.X, NavigationY, destination.Y);
    }

    private static Vector3 NavigationCenter(GridPoint cell) => new((cell.X + .5f) * CellSize,
        (NavigationY + .5f) * CellSize, (cell.Y + .5f) * CellSize);
    internal static Vector3 Eye(GridPoint cell) => NavigationCenter(cell) with { Y = CellSize + 1.6f };
    internal void Attach() => engine.VoxelScenePresentation.RefreshScene(scene!);

    public void Dispose()
    {
        foreach (Light light in lights) light.Dispose();
        lights.Clear();
        scene?.Dispose();
        scene = null;
        foreach (Material material in materials) material.Dispose();
        materials.Clear();
        spatial.Dispose();
    }
}
