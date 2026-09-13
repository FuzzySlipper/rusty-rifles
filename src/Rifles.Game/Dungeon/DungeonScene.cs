using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Interaction;
using Rifles.Procgen.Generation;
using Rifles.Game.Items;

namespace Rifles.Game.Dungeon;

/// <summary>Publishes the resolved grid through Engine voxels and navigation.</summary>
internal sealed class DungeonScene : IDisposable
{
    private readonly ExplorationTuning tuning;
    private float CellSize => tuning.CellSize;
    internal float LogicalCellSize => CellSize;
    private int VoxelsPerCell => appearance.VoxelsPerCell;
    private float VoxelCellSize => CellSize / VoxelsPerCell;
    private const int FloorY = 0;
    private const int NavigationY = 1;
    private int CeilingY => tuning.CeilingCells;
    private const ulong GridId = 1;
    private const uint StoneSlot = 1, ExitSlot = 2, DoorSlot = 3;
    private const int EngineVoxelEditLimit = 4096;
    private readonly IEngineContext engine;
    private readonly SpatialSession spatial;
    private readonly DungeonFloor floor;
    private Material? doorMaterial;
    private bool doorVoxels;
    private readonly List<(Light Owner, LightRequest Request)> roomLights = [];
    private readonly AppearanceDefinition appearance;
    private DungeonMaterials? materials;
    private VoxelScenePresentation? scene;
    internal string Style { get; private set; }

    internal DungeonScene(IEngineContext engine, DungeonFloor floor, ExplorationTuning tuning, AppearanceDefinition appearance, Func<ulong> allocateLightId, ItemExplorationDefinition? itemDefinition = null)
    {
        this.engine = engine;
        this.floor = floor;
        this.tuning = tuning;
        this.appearance = appearance;
        Style = appearance.InitialStyle;
        spatial = engine.Spatial.CreateSession(new SpatialSessionConfig(VoxelCellSize,
            checked((uint)(tuning.ChunkSize * VoxelsPerCell)), VoxelSurfaceMode.GreedyCubes));
        try
        {
            if (itemDefinition is not null)
            {
                float[] color = itemDefinition.DoorColor;
                doorMaterial = engine.Graphics.CreateMaterial(new MaterialRequest(new Color(color[0], color[1], color[2], color[3]),
                    default, 1, new Color(1, 1, 1, 1), Vector3.Zero, 0, false));
            }
            materials = new DungeonMaterials(engine, appearance.Style(Style), VoxelCellSize);
            IReadOnlySet<GridPoint> cells = floor.Cells.ToHashSet();
            HashSet<GridPoint> walls = cells.SelectMany(cell => CardinalDirections.Ordered.Select(d => cell + d.Offset()))
                .Where(cell => !cells.Contains(cell)).ToHashSet();
            List<VoxelEdit> edits = [];
            foreach (GridPoint cell in cells)
            {
                AddLogicalVoxel(edits, cell, FloorY, cell == floor.Exit ? ExitSlot : StoneSlot);
                AddLogicalVoxel(edits, cell, CeilingY, StoneSlot);
            }
            foreach (GridPoint wall in walls)
                for (int y = FloorY; y <= CeilingY; y++) AddLogicalVoxel(edits, wall, y, StoneSlot);
            foreach (VoxelEdit[] batch in edits.Chunk(EngineVoxelEditLimit))
            {
                VoxelSceneReadout before = engine.Voxel.ReadScene(new VoxelSceneReadRequest(spatial));
                engine.Voxel.ApplyEdits(new VoxelEditTransaction(spatial, before.SourceRevision, batch));
            }
            engine.Spatial.ReplaceNavigation(new NavigationReplaceRequest(spatial,
                new PlanarNavConfig(GridId, CellSize, tuning.ChunkSize, 1),
                cells.Select(c => new PlanarNavCell(c.X, NavigationY, c.Y)).ToArray()));
            scene = engine.VoxelScenePresentation.ProjectSceneDirectional(new ProjectVoxelSceneDirectionalRequest(
                spatial, MaterialBindings(materials), FaceMaterialBindings(materials)));
            foreach (GridPoint room in floor.RoomCenters)
            {
                Vector3 position = Eye(room);
                LightRequest request = RoomLightRequest(allocateLightId(), position, true);
                roomLights.Add((engine.Graphics.CreateLight(request), request));
            }
        }
        catch { Dispose(); throw; }
    }

    internal void SetDoor(GridPoint door, bool open)
    {
        List<VoxelEdit> edits = [];
        for (int y = FloorY + 1; y < CeilingY; y++) AddLogicalVoxel(edits, door, y, doorMaterial is null ? StoneSlot : DoorSlot);
        if (open) edits = edits.Select(e => e with { Kind = VoxelEditKind.Clear }).ToList();
        VoxelSceneReadout before = engine.Voxel.ReadScene(new VoxelSceneReadRequest(spatial));
        engine.Voxel.ApplyEdits(new VoxelEditTransaction(spatial, before.SourceRevision, edits.ToArray()));
        engine.Spatial.ReplaceNavigation(new NavigationReplaceRequest(spatial,
            new PlanarNavConfig(GridId, CellSize, tuning.ChunkSize, 1),
            floor.Cells.Where(c => open || c != door).Select(c => new PlanarNavCell(c.X, NavigationY, c.Y)).ToArray()));
        doorVoxels = !open;
        engine.VoxelScenePresentation.UpdateSceneDirectional(new UpdateVoxelScenePresentationDirectionalRequest(
            scene!, MaterialBindings(materials!), FaceMaterialBindings(materials!)));
    }

    internal bool AdmitStep(GridPoint from, GridPoint destination)
    {
        if (from.ManhattanDistance(destination) != 1) return false;
        NavigationStepReceipt step = engine.Spatial.EvaluateNavigationStep(new NavigationStepRequest(
            spatial, NavigationCenter(from), NavigationCenter(destination), CellSize, tuning.NavigationBudget));
        return step.Outcome == NavigationPathOutcome.Reached && step.Reached != 0
            && step.NextPathCell == new PlanarNavCell(destination.X, NavigationY, destination.Y);
    }

    internal SpatialHit Trace(Vector3 start, Vector3 end, SpatialEntityCollider[] bodies, ulong ignore) =>
        engine.Spatial.CastSegment(new SpatialSegmentCastRequest(spatial, start, end, new SpatialQueryFilter(0, 0), bodies, new[] { ignore }, ReadOnlyMemory<SpatialEntityCollider>.Empty));
    internal GridPoint? NextStep(GridPoint from, IEnumerable<GridPoint> goals, IEnumerable<GridPoint> blocked)
    {
        NavigationTraversalCell[] overlay = blocked.Where(c => c != from).Distinct()
            .Select(c => new NavigationTraversalCell(new PlanarNavCell(c.X, NavigationY, c.Y), false, 1)).ToArray();
        engine.Spatial.ReplaceNavigationTraversal(new NavigationTraversalReplaceRequest(spatial, overlay));
        GridPoint? best = null;
        uint shortest = uint.MaxValue;
        foreach (GridPoint goal in goals)
        {
            NavigationWeightedPathReadout path = engine.Spatial.RequestWeightedNavigationPath(new NavigationWeightedPathRequest(
                spatial, new PlanarNavCell(from.X, NavigationY, from.Y), new PlanarNavCell(goal.X, NavigationY, goal.Y), tuning.NavigationBudget));
            if (path.Outcome != NavigationPathOutcome.Reached || path.PathLen <= 1 || path.PathLen >= shortest) continue;
            NavigationPathCellAtReceipt next = engine.Spatial.ReadNavigationPathCellAt(new NavigationPathCellAtRequest(spatial, 1));
            if (!next.Present) continue;
            best = new(checked((int)next.Cell.X), checked((int)next.Cell.Z)); shortest = path.PathLen;
        }
        return best;
    }
    private Vector3 NavigationCenter(GridPoint cell) => new((cell.X + .5f) * CellSize,
        (NavigationY + .5f) * CellSize, (cell.Y + .5f) * CellSize);
    internal Vector3 Eye(GridPoint cell) => Eye(new Vector2(cell.X, cell.Y));
    internal Vector3 Eye(Vector2 cell) => new((cell.X + .5f) * CellSize, CellSize + tuning.EyeHeight, (cell.Y + .5f) * CellSize);
    internal float GroundHeight => CellSize;
    internal InteractionVisibility Visibility(Vector3 origin, Vector3 target) => InteractionVisibilityQuery.Cast(engine.Spatial, spatial, origin, target, new SpatialQueryFilter(0, 0), ReadOnlyMemory<SpatialEntityCollider>.Empty, ReadOnlyMemory<ulong>.Empty);
    internal void Attach() => engine.VoxelScenePresentation.RefreshScene(scene!);

    /// <summary>Rebinds only the retained voxel presentation to another admitted art treatment.</summary>
    internal void SetStyle(string style)
    {
        DungeonMaterials replacement = new(engine, appearance.Style(style), VoxelCellSize);
        try
        {
            engine.VoxelScenePresentation.UpdateSceneDirectional(new UpdateVoxelScenePresentationDirectionalRequest(
                scene!, MaterialBindings(replacement), FaceMaterialBindings(replacement)));
        }
        catch
        {
            replacement.Dispose();
            throw;
        }

        DungeonMaterials previous = materials!;
        materials = replacement;
        Style = style;
        previous.Dispose();
    }

    /// <summary>Enables or disables the fixed room-fill lights without changing the lantern or simulation state.</summary>
    internal void SetRoomLights(bool enabled)
    {
        for (int index = 0; index < roomLights.Count; index++)
        {
            (Light owner, LightRequest request) = roomLights[index];
            LightRequest replacement = request with { Descriptor = request.Descriptor with { Enabled = enabled } };
            engine.Graphics.UpdateLight(new LightUpdateRequest(owner, replacement));
            roomLights[index] = (owner, replacement);
        }
    }

    private LightRequest RoomLightRequest(ulong id, Vector3 position, bool enabled) => new(id, false, 0,
        new LightDescriptor(LightKind.Point,
            new Vector3(appearance.LightColor[0], appearance.LightColor[1], appearance.LightColor[2]),
            appearance.LightIntensity, enabled, position, Vector3.UnitY, true, appearance.LightRange,
            1f, 0, 0, LightShadowIntent.Disabled));

    private void AddLogicalVoxel(List<VoxelEdit> edits, GridPoint cell, int y, uint material)
    {
        for (int offsetY = 0; offsetY < VoxelsPerCell; offsetY++)
            for (int offsetZ = 0; offsetZ < VoxelsPerCell; offsetZ++)
                for (int offsetX = 0; offsetX < VoxelsPerCell; offsetX++)
                    edits.Add(new VoxelEdit(VoxelEditKind.Set, new VoxelAddress(
                        Subcell(cell.X, offsetX), Subcell(y, offsetY), Subcell(cell.Y, offsetZ)), material));
    }

    private int Subcell(int logicalCoordinate, int offset) => checked(logicalCoordinate * VoxelsPerCell + offset);

    private VoxelSceneMaterialBinding[] MaterialBindings(DungeonMaterials value) => doorMaterial is null || !doorVoxels
        ? [new(StoneSlot, value.Wall), new(ExitSlot, value.Exit)]
        : [new(StoneSlot, value.Wall), new(ExitSlot, value.Exit), new(DoorSlot, doorMaterial)];

    private static VoxelSceneFaceMaterialBinding[] FaceMaterialBindings(DungeonMaterials value) =>
    [
        new VoxelSceneFaceMaterialBinding(StoneSlot, SpatialFace.PosY, value.Floor),
        new VoxelSceneFaceMaterialBinding(StoneSlot, SpatialFace.NegY, value.Ceiling),
        new VoxelSceneFaceMaterialBinding(ExitSlot, SpatialFace.PosY, value.Exit),
    ];

    public void Dispose()
    {
        foreach ((Light owner, _) in roomLights) owner.Dispose();
        roomLights.Clear();
        scene?.Dispose();
        scene = null;
        materials?.Dispose();
        materials = null;
        doorMaterial?.Dispose();
        spatial.Dispose();
    }
}
