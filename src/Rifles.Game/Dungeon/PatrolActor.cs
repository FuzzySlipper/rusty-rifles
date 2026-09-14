using Rifles.Procgen.Generation;
namespace Rifles.Game.Dungeon;
internal sealed record PatrolSnapshot(ulong Id, GridPoint Start, GridPoint End, ExplorationSnapshot Motion);
internal sealed class PatrolActor(ulong id, GridPoint start, GridPoint end, ExplorationState motion)
{
    internal ulong Id => id;
    internal ExplorationState Motion => motion;
    internal PatrolSnapshot Capture() => new(id, start, end, motion.Capture());
    internal void Bind(MovementGrid grid) => motion.Bind(grid, id);
    internal void Advance(double seconds)
    {
        motion.Advance(seconds);
        if (!motion.Moving) motion.Act(motion.Position == start ? ExplorationAction.Forward : ExplorationAction.Backward);
    }
    internal static PatrolActor Create(ulong id, DungeonFloor floor, ExplorationTuning tuning, FeatureDefinition features)
    {
        HashSet<GridPoint> cells = floor.Cells.ToHashSet();
        foreach (GridPoint start in cells.OrderBy(p => Math.Abs(p.ManhattanDistance(floor.Entrance) - features.ActorOffsetCells)).ThenBy(p => p.Y).ThenBy(p => p.X))
        foreach (CardinalDirection direction in CardinalDirections.Ordered)
        {
            GridPoint end = start + direction.Offset();
            if (!cells.Contains(end) || !DressingPlacement.CanBlock(floor, [start, end])) continue;
            return new(id, start, end, new ExplorationState(start, tuning with { InitialFacing = direction }));
        }
        throw new InvalidDataException("Floor has no adjacent porter patrol cells away from party entrance.");
    }
    internal static PatrolActor Restore(PatrolSnapshot saved, DungeonFloor floor, ExplorationTuning tuning)
    {
        if (saved.Id == 0 || saved.Start.ManhattanDistance(saved.End) != 1 || !floor.Cells.Contains(saved.Start) || !floor.Cells.Contains(saved.End)
            || saved.Motion.Position != saved.Start && saved.Motion.Position != saved.End)
            throw new InvalidDataException("Invalid saved porter route.");
        if (saved.Motion.Action == ExplorationAction.Forward && saved.Motion.Position != saved.Start
            || saved.Motion.Action == ExplorationAction.Backward && saved.Motion.Position != saved.End)
            throw new InvalidDataException("Saved porter action leaves its patrol route.");
        CardinalDirection direction = CardinalDirections.Ordered.Single(d => saved.Start + d.Offset() == saved.End);
        if (saved.Motion.Facing != direction || saved.Motion.Action is not (null or ExplorationAction.Forward or ExplorationAction.Backward))
            throw new InvalidDataException("Invalid saved porter motion.");
        return new(saved.Id, saved.Start, saved.End, ExplorationState.Restore(saved.Motion, floor, tuning));
    }
}
