using System.Numerics;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Procgen.Generation;
using Rusty.Engine.Interaction;

namespace Rifles.Game.Items;

internal enum AnchorPlacement { Floor, Bench, Crate, Plate }
internal sealed record AnchorDefinition(string Key, string Name, AnchorPlacement Placement, float[] Offset);
internal sealed record ItemExplorationDefinition(AnchorDefinition[] Anchors, int[] PlateOffset, ulong PlateWeight,
    ulong PartyWeight, ulong ActorWeight, float Reach, float[] DoorColor, float[] PlateColor, float PlateHeight)
{
    internal void Validate()
    {
        GameDefinitions.Require(Anchors.Length > 0 && Anchors.Select(a => a.Key).Distinct().Count() == Anchors.Length
            && Anchors.Count(a => a.Placement == AnchorPlacement.Plate) == 1
            && Anchors.Count(a => a.Placement == AnchorPlacement.Crate) == 1, "item anchors");
        foreach (AnchorDefinition a in Anchors)
            GameDefinitions.Require(!string.IsNullOrWhiteSpace(a.Key) && !string.IsNullOrWhiteSpace(a.Name)
                && Enum.IsDefined(a.Placement) && a.Offset.Length == 3 && a.Offset.All(float.IsFinite), "anchor " + a.Key);
        GameDefinitions.Require(PlateOffset.Length == 2 && PlateWeight > 0 && PartyWeight > 0 && ActorWeight > 0
            && float.IsFinite(Reach) && Reach > 0 && float.IsFinite(PlateHeight) && PlateHeight > 0, "item interaction tuning");
        foreach (float[] color in new[] { DoorColor, PlateColor })
            GameDefinitions.Require(color.Length == 4 && color.All(v => float.IsFinite(v) && v >= 0 && v <= 1), "puzzle colors");
    }
}
internal sealed record WorldAnchor(ulong Id, string Key, GridPoint Cell);
internal sealed record ItemExplorationSnapshot(WorldAnchor[] Anchors, ulong DoorId, GridPoint Door,
    ulong LeverId, GridPoint Lever, ulong PlateId, bool Unlocked, bool LeverOn, bool DoorOpen, bool CrateOpened, ulong Revision, string? OpenContainer = null);

/// <summary>Resolved interaction anchors and puzzle meaning, independent of inventory contents and Engine presentation.</summary>
internal sealed class ExplorationItems
{
    private readonly ItemExplorationDefinition definition;
    private ItemExplorationSnapshot state;
    internal string? OpenContainer => state.OpenContainer;
    internal ItemExplorationSnapshot Capture() => state;
    internal GridPoint Door => state.Door;
    internal ulong LeverId => state.LeverId;
    internal ulong DoorId => state.DoorId;
    internal ItemExplorationDefinition Definition => definition;
    internal IReadOnlyList<WorldAnchor> Anchors => state.Anchors;
    internal ulong Revision => state.Revision;
    internal bool DoorOpen => state.DoorOpen;
    internal bool Unlocked => state.Unlocked;
    internal bool LeverOn => state.LeverOn;
    internal string Status(ulong weight) => $"Gate: {(state.DoorOpen ? "open" : "closed")} · {(state.Unlocked ? "unlocked" : "locked")} · lever {(state.LeverOn ? "on" : "off")} · plate {weight}/{definition.PlateWeight}";
    internal ExplorationItems(ItemExplorationDefinition definition, ItemExplorationSnapshot state)
    { this.definition = definition; this.state = state; }
    internal static ExplorationItems Create(ItemExplorationDefinition definition, DungeonFloor floor,
        RoomDressing dressing, PatrolActor actor, Func<ulong> allocate)
    {
        HashSet<GridPoint> floorCells = floor.Cells.ToHashSet();
        HashSet<GridPoint> unavailable = [floor.Entrance, floor.Exit, dressing.Bench, dressing.Crate, dressing.Observer, actor.Capture().Start, actor.Capture().End];
        unavailable.UnionWith(floor.Routes.Where(r => r.Traversal != Rifles.Procgen.TraversalKind.Open).Select(r => r.Cells[r.Cells.Count / 2]));
        unavailable.UnionWith(floor.Routes.Where(r => r.Traversal == Rifles.Procgen.TraversalKind.Locked).Select(r => r.Cells[0]));
        unavailable.UnionWith(floor.Grants.Select(g => g.Cell));
        unavailable.UnionWith(floor.Connectors.SelectMany(c => new[] { c.From, c.To }));
        GridPoint desired = floor.Entrance + new GridPoint(definition.PlateOffset[0], definition.PlateOffset[1]);
        GridPoint plate = floor.Cells.Where(c => !unavailable.Contains(c)).OrderBy(c => c.ManhattanDistance(desired)).ThenBy(c => c.Y).ThenBy(c => c.X).First();
        unavailable.Add(plate);
        // A straight one-cell passage blocks this corridor; generated loops may offer other routes.
        GridPoint door = floor.Cells.Where(c => !unavailable.Contains(c)
            && CardinalDirections.Ordered.Count(d => floorCells.Contains(c + d.Offset())) == 2
            && ((floorCells.Contains(c + CardinalDirection.North.Offset()) && floorCells.Contains(c + CardinalDirection.South.Offset()))
                || (floorCells.Contains(c + CardinalDirection.East.Offset()) && floorCells.Contains(c + CardinalDirection.West.Offset()))))
            .OrderBy(c => c.ManhattanDistance(floor.Entrance)).ThenBy(c => c.Y).ThenBy(c => c.X).First();
        GridPoint lever = CardinalDirections.Ordered.Select(d => door + d.Offset()).Where(floorCells.Contains)
            .OrderBy(c => c.ManhattanDistance(floor.Entrance)).First();
        WorldAnchor[] anchors = definition.Anchors.Select(a => new WorldAnchor(allocate(), a.Key, a.Placement switch
        { AnchorPlacement.Bench => dressing.Bench, AnchorPlacement.Crate => dressing.Crate, AnchorPlacement.Plate => plate, _ => floor.Entrance })).ToArray();
        return new(definition, new(anchors, allocate(), door, allocate(), lever, allocate(), false, false, false, false, 1));
    }
    internal void Validate(DungeonFloor floor)
    {
        GameDefinitions.Require(state.OpenContainer is null || state.OpenContainer == "crate" && state.CrateOpened, "saved open container");
        GameDefinitions.Require(state.Revision > 0 && state.Revision <= uint.MaxValue && state.Anchors.Select(a => a.Key).ToHashSet().SetEquals(definition.Anchors.Select(a => a.Key))
            && state.Anchors.Length == definition.Anchors.Length && state.Anchors.All(a => floor.Cells.Contains(a.Cell))
            && floor.Cells.Contains(state.Door) && floor.Cells.Contains(state.Lever) && state.Door.ManhattanDistance(state.Lever) == 1, "saved item anchors and gate");
    }
    internal AnchorDefinition AnchorDefinition(string key) => definition.Anchors.Single(a => a.Key == key);
    internal WorldAnchor Anchor(string key) => state.Anchors.Single(a => a.Key == key);
    internal Vector3 Point(string key, DungeonScene scene)
    {
        WorldAnchor anchor = Anchor(key); float[] offset = AnchorDefinition(key).Offset;
        return (scene.Eye(anchor.Cell) with { Y = scene.GroundHeight(anchor.Cell) }) + new Vector3(offset[0], offset[1], offset[2]);
    }
    internal Vector3 LeverPoint(DungeonScene scene) => scene.Eye(state.Lever);
    internal Vector3 DoorPoint(DungeonScene scene) => scene.Eye(state.Door);
    internal bool Reachable(Vector3 target, ExplorationState party, DungeonScene scene) => !party.Moving
        && Vector3.Distance(scene.Eye(party.Position), target) <= definition.Reach
        && scene.Visibility(scene.Eye(party.Position), target) == InteractionVisibility.Visible;
    internal bool Reachable(string key, ExplorationState party, DungeonScene scene) => Reachable(Point(key, scene), party, scene);
    internal void RequireAccess(string key, ExplorationState party, DungeonScene scene)
    {
        if (InventoryOwner.Parse(key) is MemberOwner or PartyOwner) return;
        if (!Reachable(key, party, scene)) throw new InvalidDataException("Move within reach of that anchor.");
        if (key == "crate" && OpenContainer != key) throw new InvalidDataException("Open the crate first.");
    }
    internal void Open(string key, ExplorationState party, DungeonScene scene)
    {
        if (AnchorDefinition(key).Placement != AnchorPlacement.Crate || !Reachable(key, party, scene))
            throw new InvalidDataException("The container is out of reach.");
        state = state with { OpenContainer = key, CrateOpened = true, Revision = checked(state.Revision + 1) };
    }
    internal void Close() => state = state with { OpenContainer = null };
    internal void CheckOpen(ExplorationState party, DungeonScene scene)
    { if (OpenContainer is { } key && !Reachable(key, party, scene)) Close(); }
    internal void ToggleLever(ExplorationState party, DungeonScene scene, ulong revision)
    {
        if (revision != state.Revision || !Reachable(LeverPoint(scene), party, scene)) throw new InvalidDataException("Lever target changed or is out of reach.");
        state = state with { LeverOn = !state.LeverOn, Revision = checked(state.Revision + 1) };
    }
    internal void Unlock(ExplorationState party, DungeonScene scene, ulong revision)
    {
        // The handle is on the approach side, not inside the closed voxel volume.
        if (revision != state.Revision || !Reachable(LeverPoint(scene), party, scene)) throw new InvalidDataException("Gate lock changed or is out of reach.");
        if (state.Unlocked) throw new InvalidDataException("The gate is already unlocked; keep the key.");
        state = state with { Unlocked = true, Revision = checked(state.Revision + 1) };
    }
    internal ulong Weight(ItemInventory items, ExplorationState party, PatrolActor actor)
    {
        WorldAnchor plate = state.Anchors.Single(a => AnchorDefinition(a.Key).Placement == AnchorPlacement.Plate);
        return checked(items.Mass(plate.Key) + (party.Position == plate.Cell ? definition.PartyWeight : 0)
            + (actor.Motion.Position == plate.Cell ? definition.ActorWeight : 0));
    }
    internal void UpdateDoor(ItemInventory items, ExplorationState party, PatrolActor actor, MovementGrid grid, DungeonScene scene)
    {
        bool desired = state.Unlocked && state.LeverOn && Weight(items, party, actor) >= definition.PlateWeight;
        if (!desired && grid.Occupied(state.Door)) desired = true;
        if (desired == state.DoorOpen) return;
        scene.SetDoor(state.Door, desired);
        state = state with { DoorOpen = desired, Revision = checked(state.Revision + 1) };
    }
}
