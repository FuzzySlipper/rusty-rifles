using Rifles.Procgen.Generation;

namespace Rifles.Game.Dungeon;

internal enum ExplorationAction { Forward, Backward, StrafeLeft, StrafeRight, TurnLeft, TurnRight }

/// <summary>One grid pose for the whole party; Engine admits movement and supplies elapsed simulation time.</summary>
internal sealed class ExplorationState(GridPoint entrance, ExplorationTuning tuning)
{
    internal GridPoint Position { get; private set; } = entrance;
    internal CardinalDirection Facing { get; private set; } = tuning.InitialFacing;
    internal double ElapsedSeconds { get; private set; }
    internal double RecoverySeconds { get; private set; }

    internal void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        ElapsedSeconds += seconds;
        RecoverySeconds = Math.Max(0, RecoverySeconds - seconds);
    }

    internal bool Act(ExplorationAction action, Func<GridPoint, GridPoint, bool> admitStep)
    {
        if (RecoverySeconds > 0) return false;
        if (action is ExplorationAction.TurnLeft or ExplorationAction.TurnRight)
            Facing = Facing.Rotate(action == ExplorationAction.TurnLeft ? -1 : 1);
        else
        {
            CardinalDirection direction = action switch
            {
                ExplorationAction.Forward => Facing,
                ExplorationAction.Backward => Facing.Opposite(),
                ExplorationAction.StrafeLeft => Facing.Rotate(-1),
                ExplorationAction.StrafeRight => Facing.Rotate(1),
                _ => throw new ArgumentOutOfRangeException(nameof(action)),
            };
            GridPoint destination = Position + direction.Offset();
            if (!admitStep(Position, destination)) return false;
            Position = destination;
        }
        RecoverySeconds = action is ExplorationAction.TurnLeft or ExplorationAction.TurnRight ? tuning.TurnSeconds : tuning.StepSeconds;
        return true;
    }
}
