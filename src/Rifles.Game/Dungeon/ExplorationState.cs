using System.Numerics;
using Rifles.Procgen.Generation;
namespace Rifles.Game.Dungeon;

internal sealed record ExplorationSnapshot(GridPoint Position, CardinalDirection Facing, double ElapsedSeconds,
    ExplorationAction? Action, GridPoint Destination, CardinalDirection DestinationFacing, double RemainingSeconds, string? Placement = null, string? DestinationPlacement = null);
internal enum ExplorationAction { Forward, Backward, StrafeLeft, StrafeRight, TurnLeft, TurnRight }

/// <summary>Source cell owns hits while in transit; destination is reserved until exact completion.</summary>
internal sealed class ExplorationState(GridPoint entrance, ExplorationTuning tuning)
{
    internal GridPoint Position { get; private set; } = entrance;
    internal CardinalDirection Facing { get; private set; } = tuning.InitialFacing;
    internal double ElapsedSeconds { get; private set; }
    internal double RecoverySeconds { get; private set; }
    private ExplorationAction? action;
    private GridPoint destination;
    private CardinalDirection destinationFacing;
    private MovementGrid? grid;
    private ulong actorId;
    private Vector2 lastOffset;
    private string? savedPlacement;
    private string? savedDestinationPlacement;
    internal Vector2 CrowdOffset => grid is null ? lastOffset : new(grid.Placement(actorId).OffsetX, grid.Placement(actorId).OffsetY);
    internal Vector2 VisualCrowdOffset => Moving && !Turning && grid?.DestinationPlacement(actorId) is { } target
        ? Vector2.Lerp(CrowdOffset, new(target.OffsetX, target.OffsetY), Progress) : CrowdOffset;
    internal bool Moving => action is not null;
    private bool Turning => action is ExplorationAction.TurnLeft or ExplorationAction.TurnRight;
    private double Duration => Turning ? tuning.TurnSeconds : tuning.StepSeconds;
    internal float Progress => Moving ? (float)Math.Clamp(1 - RecoverySeconds / Duration, 0, 1) : 0;
    internal Vector2 VisualCell => Moving && !Turning
        ? Vector2.Lerp(new(Position.X, Position.Y), new(destination.X, destination.Y), Progress) : new(Position.X, Position.Y);
    internal double VisualYaw => (int)Facing * 90d + (Turning ? (action == ExplorationAction.TurnLeft ? -90 : 90) * Progress : 0);

    internal void Bind(MovementGrid movement, ulong id)
    {
        grid = movement; actorId = id;
        grid.Add(id, Position);
        if (Moving && !Turning && !grid.TryReserve(id, destination)) throw new InvalidDataException("Saved movement destination is unavailable.");
    }
    internal void Bind(MovementGrid movement, ulong id, string footprint, string faction, bool share)
    {
        grid = movement; actorId = id;
        grid.Add(id, Position, footprint, faction, share, savedPlacement);
        if (Moving && !Turning && !grid.RestoreReservation(id, destination, savedPlacement, savedDestinationPlacement))
            throw new InvalidDataException("Saved crowd movement destination is unavailable.");
    }
    internal ExplorationSnapshot Capture() => new(Position, Facing, ElapsedSeconds, action, destination, destinationFacing, RecoverySeconds,
        grid is null ? savedPlacement : grid.Placement(actorId).Id, grid?.DestinationPlacement(actorId)?.Id ?? savedDestinationPlacement);
    internal static ExplorationState Restore(ExplorationSnapshot saved, DungeonFloor floor, ExplorationTuning tuning)
    {
        bool turning = saved.Action is ExplorationAction.TurnLeft or ExplorationAction.TurnRight;
        double duration = turning ? tuning.TurnSeconds : tuning.StepSeconds;
        if (!floor.Cells.Contains(saved.Position) || !Enum.IsDefined(saved.Facing)
            || !double.IsFinite(saved.ElapsedSeconds) || saved.ElapsedSeconds < 0
            || !double.IsFinite(saved.RemainingSeconds) || saved.RemainingSeconds < 0 || saved.RemainingSeconds > duration
            || saved.Action is { } action && (!Enum.IsDefined(action) || saved.RemainingSeconds == 0)
            || saved.Action is null && saved.RemainingSeconds != 0)
            throw new InvalidDataException("Invalid saved party pose/time.");
        ExplorationState state = new(saved.Position, tuning) { Facing = saved.Facing, ElapsedSeconds = saved.ElapsedSeconds, savedPlacement = saved.Placement, savedDestinationPlacement = saved.DestinationPlacement };
        if (saved.Action is { } active)
        {
            state.Plan(active);
            if (state.destination != saved.Destination || state.destinationFacing != saved.DestinationFacing
                || !floor.Cells.Contains(saved.Destination)) throw new InvalidDataException("Invalid saved move.");
            state.RecoverySeconds = saved.RemainingSeconds;
        }
        return state;
    }
    internal void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        ElapsedSeconds += seconds;
        RecoverySeconds = Math.Max(0, RecoverySeconds - seconds);
        if (!Moving || RecoverySeconds > 0) return;
        if (Turning) Facing = destinationFacing;
        else if (grid!.Commit(actorId)) Position = destination;
        action = null; savedDestinationPlacement = null;
    }
    internal bool Act(ExplorationAction requested)
    {
        if (Moving) return false;
        Plan(requested);
        if (!Turning && !grid!.TryReserve(actorId, destination)) { action = null; return false; }
        RecoverySeconds = Duration;
        return true;
    }
    internal void RestoreVisualOffset(Vector2 offset) => lastOffset = offset;
    internal void Detach() { grid = null; }
    internal void Stop()
    {
        if (grid is not null) { var placement = grid.Placement(actorId); savedPlacement = placement.Id; lastOffset = new(placement.OffsetX, placement.OffsetY); }
        grid?.Cancel(actorId); action = null; RecoverySeconds = 0; savedDestinationPlacement = null;
    }
    internal bool StepTo(GridPoint cell)
    {
        foreach (ExplorationAction candidate in new[] { ExplorationAction.Forward, ExplorationAction.Backward, ExplorationAction.StrafeLeft, ExplorationAction.StrafeRight })
        {
            CardinalDirection direction = candidate switch { ExplorationAction.Forward => Facing, ExplorationAction.Backward => Facing.Opposite(), ExplorationAction.StrafeLeft => Facing.Rotate(-1), _ => Facing.Rotate(1) };
            if (Position + direction.Offset() == cell) return Act(candidate);
        }
        return false;
    }
    private void Plan(ExplorationAction requested)
    {
        action = requested; destination = Position; destinationFacing = Facing;
        if (Turning) destinationFacing = Facing.Rotate(requested == ExplorationAction.TurnLeft ? -1 : 1);
        else
        {
            CardinalDirection direction = requested switch
            {
                ExplorationAction.Forward => Facing, ExplorationAction.Backward => Facing.Opposite(),
                ExplorationAction.StrafeLeft => Facing.Rotate(-1), ExplorationAction.StrafeRight => Facing.Rotate(1),
                _ => throw new ArgumentOutOfRangeException(nameof(requested)),
            };
            destination = Position + direction.Offset();
        }
    }
}
