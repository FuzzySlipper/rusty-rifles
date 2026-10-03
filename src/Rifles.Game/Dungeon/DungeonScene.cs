using System.Numerics;
using Rifles.Game.Generation;
using Rusty.Engine;
using Rusty.Engine.Interaction;
using Rifles.Procgen.Generation;
using Rifles.Game.Content;
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
    private int CeilingBase => tuning.CeilingBaseVoxels(VoxelsPerCell);
    private const ulong GridId = 1;
    private const uint StoneSlot = 1, ExitSlot = 2, DoorSlot = 3, LimewashSlot = 4, FloorDetailSlot = 5;
    private readonly IEngineContext engine;
    private readonly SpatialSession spatial;
    private readonly DungeonFloor floor;
    private Material? doorMaterial;
    private readonly HashSet<GridPoint> closedDoors = [];
    private readonly HashSet<uint> baseSlots = [];
    private bool doorVoxels => closedDoors.Count > 0;
    private readonly List<(Light Owner, LightRequest Request)> roomLights = [];
    private readonly AppearanceDefinition appearance;
    private readonly DungeonMaterialCache materialCache;
    private DungeonMaterials? materials;
    private VoxelScenePresentation? scene;
    internal string Style { get; private set; }

    internal DungeonScene(IEngineContext engine, DungeonMaterialCache materialCache, DungeonFloor floor, ExplorationTuning tuning, AppearanceDefinition appearance, Func<ulong> allocateLightId, ItemExplorationDefinition? itemDefinition = null)
    {
        this.engine = engine;
        this.floor = floor;
        this.tuning = tuning;
        this.appearance = appearance;
        this.materialCache = materialCache ?? throw new ArgumentNullException(nameof(materialCache));
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
            materials = materialCache.Get(appearance.Style(Style), VoxelCellSize);
            IReadOnlySet<GridPoint> cells = floor.Cells.ToHashSet();
            HashSet<GridPoint> walls = cells.SelectMany(cell => CardinalDirections.Ordered.Select(d => cell + d.Offset()))
                .Where(cell => !cells.Contains(cell)).ToHashSet();
            List<VoxelEdit> edits = [];
            foreach (GridPoint cell in cells)
            {
                AddLogicalVoxel(edits, cell, FloorY + floor.Level(cell), cell == floor.Exit ? ExitSlot : StoneSlot);
                AddVoxelSlab(edits, cell, Subcell(floor.Level(cell), CeilingBase), VoxelsPerCell, StoneSlot);
            }
            foreach (GridPoint wall in walls)
            {
                int[] neighbors = CardinalDirections.Ordered.Select(d => wall + d.Offset()).Where(cells.Contains).Select(floor.Level).ToArray();
                int bottom = Subcell(neighbors.Min(), 0);
                int top = Subcell(neighbors.Max(), CeilingBase + VoxelsPerCell);
                AddVoxelSlab(edits, wall, bottom, top - bottom, StoneSlot);
            }
            AddArchitecture(edits);
            // Stair treads are explicit traversal geometry, not decorative occluders.
            foreach (var connector in floor.Connectors.Where(c => floor.Level(c.From) >= 0 && floor.Level(c.To) == floor.Level(c.From) + 1))
            {
                int dx = connector.To.X - connector.From.X, dz = connector.To.Y - connector.From.Y;
                for (int x = 0; x < VoxelsPerCell; x++)
                    for (int z = 0; z < VoxelsPerCell; z++)
                    {
                        int treadHeight = FloorSurface.TreadHeight(x, z, dx, dz, VoxelsPerCell);
                        for (int y = 0; y < treadHeight; y++)
                            edits.Add(new VoxelEdit(VoxelEditKind.Set, new VoxelAddress(Subcell(connector.From.X, x),
                                Subcell(NavigationY + floor.Level(connector.From), y), Subcell(connector.From.Y, z)), StoneSlot));
                    }
            }
            var resolvedEdits = edits.GroupBy(edit => edit.Address).Select(group => group.Last()).ToArray();
            baseSlots.UnionWith(resolvedEdits.Where(edit => edit.Kind == VoxelEditKind.Set).Select(edit => edit.MaterialSlot));
            engine.Voxel.ApplyEdits(new VoxelEditTransaction(spatial, resolvedEdits));
            engine.Spatial.ReplaceNavigation(new NavigationReplaceRequest(spatial,
                new PlanarNavConfig(GridId, CellSize, tuning.ChunkSize, 1),
                cells.Select(c => NavigationCell(c)).ToArray()));
            scene = engine.VoxelScenePresentation.ProjectSceneDirectional(new ProjectVoxelSceneDirectionalRequest(
                spatial, MaterialBindings(materials), FaceMaterialBindings(materials)));
            LightRequest ambient = new(allocateLightId(), false, 0,
                new LightDescriptor(LightKind.Ambient,
                    new Vector3(appearance.AmbientColor[0], appearance.AmbientColor[1], appearance.AmbientColor[2]),
                    appearance.AmbientIntensity, true, Vector3.Zero, Vector3.UnitY, true, 0,
                    1, 0, 0, LightShadowIntent.Disabled));
            roomLights.Add((engine.Graphics.CreateLight(ambient), ambient));
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
        AddVoxelSlab(edits, door, Subcell(floor.Level(door) + 1, 0), CeilingBase - VoxelsPerCell,
            doorMaterial is null ? StoneSlot : DoorSlot);
        if (open) edits = edits.Select(e => e with { Kind = VoxelEditKind.Clear }).ToList();
        engine.Voxel.ApplyEdits(new VoxelEditTransaction(spatial, edits.ToArray()));
        if (open) closedDoors.Remove(door); else closedDoors.Add(door);
        engine.Spatial.ReplaceNavigation(new NavigationReplaceRequest(spatial,
            new PlanarNavConfig(GridId, CellSize, tuning.ChunkSize, 1),
            floor.Cells.Where(c => !closedDoors.Contains(c)).Select(c => NavigationCell(c)).ToArray()));
        engine.VoxelScenePresentation.UpdateSceneDirectional(new UpdateVoxelScenePresentationDirectionalRequest(
            scene!, MaterialBindings(materials!), FaceMaterialBindings(materials!)));
    }

    internal bool AdmitStep(GridPoint from, GridPoint destination)
    {
        if (from.ManhattanDistance(destination) != 1) return false;
        NavigationStepResult step = engine.Spatial.EvaluateNavigationStep(new NavigationStepRequest(
            spatial, NavigationCenter(from), NavigationCenter(destination), Vector3.Distance(NavigationCenter(from), NavigationCenter(destination)), tuning.NavigationBudget));
        return step.Outcome == NavigationPathOutcome.Reached && step.Reached != 0
            && step.NextPathCell == NavigationCell(destination);
    }

    internal (int Checked, bool Complete, string[] Mismatches) InspectNavigation(int maximumEdges)
    {
        if (maximumEdges <= 0) throw new ArgumentOutOfRangeException(nameof(maximumEdges));
        int examined = 0;
        List<string> mismatches = [];
        var cells = floor.Cells.ToHashSet();
        foreach (var from in floor.Cells.Where(c => !closedDoors.Contains(c)))
            foreach (var direction in CardinalDirections.Ordered)
            {
                if (examined == maximumEdges) return (examined, false, mismatches.ToArray());
                var to = from + direction.Offset();
                bool expected = cells.Contains(to) && !closedDoors.Contains(to) && Math.Abs(floor.Level(from) - floor.Level(to)) <= 1;
                if (AdmitStep(from, to) != expected) mismatches.Add($"navigation mismatch {from} -> {to}");
                examined++;
            }
        return (examined, true, mismatches.ToArray());
    }

    internal SpatialHit Trace(Vector3 start, Vector3 end, SpatialEntityCollider[] bodies, ulong ignore) =>
        engine.Spatial.CastSegment(new SpatialSegmentCastRequest(spatial, start, end, new SpatialQueryFilter(0, 0), bodies, new[] { ignore }, ReadOnlyMemory<SpatialEntityCollider>.Empty));
    internal GridPoint? NextStep(GridPoint from, IEnumerable<GridPoint> goals, IEnumerable<GridPoint> blocked)
    {
        // Closed doors are absent from the Engine projection, so they cannot be overlay cells.
        NavigationTraversalCell[] overlay = blocked.Where(c => c != from && !closedDoors.Contains(c)).Distinct()
            .Select(c => new NavigationTraversalCell(NavigationCell(c), false, 1)).ToArray();
        engine.Spatial.ReplaceNavigationTraversal(new NavigationTraversalReplaceRequest(spatial, overlay));
        try
        {
            GridPoint? best = null;
            uint shortest = uint.MaxValue;
            foreach (GridPoint goal in goals)
            {
                NavigationWeightedPathResult path = engine.Spatial.RequestWeightedNavigationPath(new NavigationWeightedPathRequest(
                    spatial, NavigationCell(from), NavigationCell(goal), tuning.NavigationBudget));
                uint length = checked((uint)path.Path.Length);
                if (path.Outcome != NavigationPathOutcome.Reached || length <= 1 || length >= shortest) continue;
                PlanarNavCell next = path.Path.Span[1];
                best = new(checked((int)next.X), checked((int)next.Z)); shortest = length;
            }
            return best;
        }
        finally
        {
            // Occupancy exclusions belong to this mover's path query. Retaining
            // them would also block another actor's ordinary step admission.
            engine.Spatial.ReplaceNavigationTraversal(new NavigationTraversalReplaceRequest(spatial,
                ReadOnlyMemory<NavigationTraversalCell>.Empty));
        }
    }

    private PlanarNavCell NavigationCell(GridPoint cell) => new(cell.X, NavigationY + floor.Level(cell), cell.Y);
    private Vector3 NavigationCenter(GridPoint cell) => new((cell.X + .5f) * CellSize,
        (NavigationY + floor.Level(cell) + .5f) * CellSize, (cell.Y + .5f) * CellSize);
    internal Vector3 Eye(GridPoint cell) => Eye(new Vector2(cell.X, cell.Y));
    internal Vector3 Eye(Vector2 cell) => new((cell.X + .5f) * CellSize, GroundHeight(cell) + tuning.EyeHeight, (cell.Y + .5f) * CellSize);
    internal float GroundHeight(GridPoint cell) => GroundHeight(new Vector2(cell.X, cell.Y));
    internal float GroundHeight(Vector2 cell) => FloorSurface.Height(floor, cell, CellSize, VoxelsPerCell);
    internal InteractionVisibility Visibility(Vector3 origin, Vector3 target) => InteractionVisibilityQuery.Cast(engine.Spatial, spatial, origin, target, new SpatialQueryFilter(0, 0), ReadOnlyMemory<SpatialEntityCollider>.Empty, ReadOnlyMemory<ulong>.Empty);

    /// <summary>Rebinds only the retained voxel presentation to another cached art treatment.</summary>
    internal void SetStyle(string style)
    {
        DungeonMaterials replacement = materialCache.Get(appearance.Style(style), VoxelCellSize);
        engine.VoxelScenePresentation.UpdateSceneDirectional(new UpdateVoxelScenePresentationDirectionalRequest(
            scene!, MaterialBindings(replacement), FaceMaterialBindings(replacement)));
        materials = replacement;
        Style = style;
    }

    /// <summary>Enables or disables authored ambient and room lights without changing the lantern or simulation state.</summary>
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
            1f, 0, 0, appearance.RoomLightShadows ? LightShadowIntent.Requested : LightShadowIntent.Disabled));

    private void AddLogicalVoxel(List<VoxelEdit> edits, GridPoint cell, int y, uint material)
    {
        AddVoxelSlab(edits, cell, Subcell(y, 0), VoxelsPerCell, material);
    }

    private void AddVoxelSlab(List<VoxelEdit> edits, GridPoint cell, int bottom, int height, uint material)
    {
        for (int offsetY = 0; offsetY < height; offsetY++)
            for (int offsetZ = 0; offsetZ < VoxelsPerCell; offsetZ++)
                for (int offsetX = 0; offsetX < VoxelsPerCell; offsetX++)
                    edits.Add(new VoxelEdit(VoxelEditKind.Set, new VoxelAddress(
                        Subcell(cell.X, offsetX), bottom + offsetY, Subcell(cell.Y, offsetZ)), material));
    }

    private void AddArchitecture(List<VoxelEdit> edits)
    {
        if (floor.Architecture is not { } detail) return;
        ArchitectureDetail.ValidateVoxelResolution(detail, floor, VoxelsPerCell, CeilingBase + VoxelsPerCell);
        foreach (var fact in detail.Facts)
        {
            uint material = fact.Material switch { ArchitectureDetailMaterial.Limewash => LimewashSlot,
                ArchitectureDetailMaterial.Floor => FloorDetailSlot, _ => StoneSlot };
            if (fact.Kind == ArchitectureDetailKind.MaterialRegion)
            {
                AddVoxelSlab(edits, fact.Cell, Subcell(floor.Level(fact.Cell),
                    fact.Surface == ArchitectureDetailSurface.Floor ? 0 : CeilingBase), VoxelsPerCell, material);
                continue;
            }
            var side = fact.Side!.Value;
            var wall = fact.Cell + side.Offset();
            for (int depth = 0; depth < Math.Max(1, fact.Depth); depth++)
                for (int across = 0; across < VoxelsPerCell; across++)
                    for (int y = fact.Layer; y < fact.Layer + fact.Height; y++)
                    {
                        int x = side == CardinalDirection.East ? depth : side == CardinalDirection.West ? VoxelsPerCell - 1 - depth : across;
                        int z = side == CardinalDirection.South ? depth : side == CardinalDirection.North ? VoxelsPerCell - 1 - depth : across;
                        var kind = fact.Kind is ArchitectureDetailKind.Recess or ArchitectureDetailKind.Damage ? VoxelEditKind.Clear : VoxelEditKind.Set;
                        edits.Add(new VoxelEdit(kind, new VoxelAddress(Subcell(wall.X, x), Subcell(floor.Level(fact.Cell), y), Subcell(wall.Y, z)), material));
                    }
        }
    }

    private int Subcell(int logicalCoordinate, int offset) => checked(logicalCoordinate * VoxelsPerCell + offset);

    private VoxelSceneMaterialBinding[] MaterialBindings(DungeonMaterials value)
    {
        var bindings = new List<VoxelSceneMaterialBinding>
        {
            new(StoneSlot, value.Wall), new(ExitSlot, value.Exit),
            new(LimewashSlot, value.Ceiling), new(FloorDetailSlot, value.Floor),
        };
        bindings.RemoveAll(binding => !baseSlots.Contains(binding.MaterialSlot));
        if (doorMaterial is not null && doorVoxels) bindings.Add(new(DoorSlot, doorMaterial));
        return bindings.ToArray();
    }

    private VoxelSceneFaceMaterialBinding[] FaceMaterialBindings(DungeonMaterials value) =>
        new VoxelSceneFaceMaterialBinding[]
        {
            new(StoneSlot, SpatialFace.PosY, value.Floor),
            new(StoneSlot, SpatialFace.NegY, value.Ceiling),
            new(ExitSlot, SpatialFace.PosY, value.Exit),
        }.Where(binding => baseSlots.Contains(binding.MaterialSlot)).ToArray();

    public void Dispose()
    {
        foreach ((Light owner, _) in roomLights) owner.Dispose();
        roomLights.Clear();
        scene?.Dispose();
        scene = null;
        materials = null;
        doorMaterial?.Dispose();
        spatial.Dispose();
    }
}
