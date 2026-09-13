using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Interaction;
using Rifles.Procgen.Generation;

namespace Rifles.Game.Dungeon;

/// <summary>Publishes the resolved grid through Engine voxels and navigation.</summary>
internal sealed class DungeonScene : IDisposable
{
    private readonly ExplorationTuning tuning;
    private float CellSize => tuning.CellSize;
    private const int FloorY = 0;
    private const int NavigationY = 1;
    private int CeilingY => tuning.CeilingCells;
    private const ulong GridId = 1;
    private const uint StoneSlot = 1, ExitSlot = 2;
    private readonly IEngineContext engine;
    private readonly SpatialSession spatial;
    private readonly List<Material> materials = [];
    private readonly List<Light> lights = [];
    private VoxelScenePresentation? scene;

    internal DungeonScene(IEngineContext engine, DungeonFloor floor, ExplorationTuning tuning, AppearanceDefinition appearance, Func<ulong> allocateLightId)
    {
        this.engine = engine;
        this.tuning = tuning;
        spatial = engine.Spatial.CreateSession(new SpatialSessionConfig(CellSize, tuning.ChunkSize, VoxelSurfaceMode.GreedyCubes));
        try
        {
            materials.Add(engine.Graphics.CreateMaterial(appearance.Stone.Request()));
            materials.Add(engine.Graphics.CreateMaterial(appearance.Exit.Request()));
            IReadOnlySet<GridPoint> cells = floor.Cells.ToHashSet();
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
                new PlanarNavConfig(GridId, CellSize, tuning.ChunkSize, 1),
                cells.Select(c => new PlanarNavCell(c.X, NavigationY, c.Y)).ToArray()));
            scene = engine.VoxelScenePresentation.ProjectScene(new ProjectVoxelSceneRequest(spatial,
                new VoxelSceneMaterialBinding[] { new(StoneSlot, materials[0]), new(ExitSlot, materials[1]) }));
            foreach (GridPoint room in floor.RoomCenters)
            {
                Vector3 position = Eye(room);
                lights.Add(engine.Graphics.CreateLight(new LightRequest(allocateLightId(), false, 0,
                    new LightDescriptor(LightKind.Point, new Vector3(appearance.LightColor[0], appearance.LightColor[1], appearance.LightColor[2]), appearance.LightIntensity,
                        true, position, Vector3.UnitY, true, appearance.LightRange, 1f, 0, 0, LightShadowIntent.Disabled))));
            }
        }
        catch { Dispose(); throw; }
    }

    internal bool AdmitStep(GridPoint from, GridPoint destination)
    {
        if (from.ManhattanDistance(destination) != 1) return false;
        NavigationStepReceipt step = engine.Spatial.EvaluateNavigationStep(new NavigationStepRequest(
            spatial, NavigationCenter(from), NavigationCenter(destination), CellSize, tuning.NavigationBudget));
        return step.Outcome == NavigationPathOutcome.Reached && step.Reached != 0
            && step.NextPathCell == new PlanarNavCell(destination.X, NavigationY, destination.Y);
    }

    private Vector3 NavigationCenter(GridPoint cell) => new((cell.X + .5f) * CellSize,
        (NavigationY + .5f) * CellSize, (cell.Y + .5f) * CellSize);
    internal Vector3 Eye(GridPoint cell) => Eye(new Vector2(cell.X, cell.Y));
    internal Vector3 Eye(Vector2 cell) => new((cell.X + .5f) * CellSize, CellSize + tuning.EyeHeight, (cell.Y + .5f) * CellSize);
    internal float GroundHeight => CellSize;
    internal InteractionVisibility Visibility(Vector3 origin, Vector3 target) => InteractionVisibilityQuery.Cast(engine.Spatial, spatial, origin, target, new SpatialQueryFilter(0, 0), ReadOnlyMemory<SpatialEntityCollider>.Empty, ReadOnlyMemory<ulong>.Empty);
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
