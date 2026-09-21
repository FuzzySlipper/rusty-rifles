namespace Rifles.Game.Party;

internal sealed record FormationMove(string Member, string From, string To);
internal sealed record FormationExecutionSnapshot(double Remaining, double Duration, FormationMove[] Moves);

/// <summary>
/// A paused draft is disposable. Only an executing rearrangement is saved;
/// it keeps the original defensive positions until one simultaneous commit.
/// </summary>
internal sealed class FormationPlanner(PartyState party)
{
    private Dictionary<string, string>? draft;
    private FormationExecutionSnapshot? execution;

    internal bool Open => draft is not null;
    internal bool Executing => execution is not null;
    internal bool PreviouslyPaused { get; private set; }
    internal IReadOnlyDictionary<string, string>? Draft => draft;
    internal FormationExecutionSnapshot? Execution => execution;
    internal bool Affects(string member) => execution?.Moves.Any(move => move.Member == member) == true;

    internal void Begin(bool paused)
    {
        if (Open || Executing || party.Defeated) throw new InvalidDataException("Formation planning is unavailable.");
        PreviouslyPaused = paused;
        draft = party.Members.ToDictionary(member => member.InstanceId, member => member.Position, StringComparer.Ordinal);
    }

    internal void Place(string memberId, string position)
    {
        if (draft is null) throw new InvalidDataException("Open the formation planner first.");
        var member = party.Members.SingleOrDefault(member => member.InstanceId == memberId);
        var destination = party.Positions.SingleOrDefault(candidate => candidate.Id == position);
        if (member is null || !member.IsLiving || member.Definition.Commander || destination is null || destination.Commander)
            throw new InvalidDataException("Choose a living soldier and an outer formation position.");
        string original = draft[memberId];
        string? occupantId = draft.SingleOrDefault(entry => entry.Value == position).Key;
        if (occupantId == memberId) return;
        if (occupantId is not null)
        {
            var occupant = party.Members.Single(candidate => candidate.InstanceId == occupantId);
            if (!occupant.IsLiving || occupant.Definition.Commander) throw new InvalidDataException("That position cannot be exchanged.");
            draft[occupantId] = original;
        }
        draft[memberId] = position;
    }

    internal FormationMove[] PlannedMoves()
    {
        if (draft is null) throw new InvalidDataException("Open the formation planner first.");
        return party.Members.Where(member => member.Position != draft[member.InstanceId])
            .Select(member => new FormationMove(member.InstanceId, member.Position, draft[member.InstanceId])).ToArray();
    }

    internal void Cancel()
    {
        if (draft is null) throw new InvalidDataException("No formation draft is open.");
        draft = null;
    }

    internal bool Execute(double duration)
    {
        FormationMove[] moves = PlannedMoves();
        if (!double.IsFinite(duration) || duration <= 0) throw new InvalidDataException("Formation duration must be positive.");
        if (moves.Length == 0) { draft = null; return false; }
        FormationExecutionSnapshot next = new(duration, duration, moves);
        Validate(next, duration);
        if (moves.Any(move => !party.Members.Single(member => member.InstanceId == move.Member).IsLiving))
            throw new InvalidDataException("A fallen soldier cannot begin repositioning.");
        execution = next;
        draft = null;
        return true;
    }

    internal void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (execution is null || party.Defeated) return;
        double remaining = execution.Remaining - seconds;
        if (remaining > 0) { execution = execution with { Remaining = remaining }; return; }
        // No observers run between these mutations. Death during execution
        // does not revive, remove, or replace a soldier.
        foreach (FormationMove move in execution.Moves)
        {
            var member = party.Members.Single(member => member.InstanceId == move.Member);
            var position = party.Positions.Single(position => position.Id == move.To);
            member.SetPosition(position.Id, position.Rank);
        }
        execution = null;
    }

    internal FormationExecutionSnapshot? Capture() => execution is null ? null : execution with { Moves = [.. execution.Moves] };

    internal void Restore(FormationExecutionSnapshot? saved, double duration)
    {
        if (saved is not null) Validate(saved, duration);
        draft = null;
        execution = saved is null ? null : saved with { Moves = [.. saved.Moves] };
    }

    private void Validate(FormationExecutionSnapshot saved, double duration)
    {
        if (!double.IsFinite(saved.Remaining) || saved.Remaining <= 0 || saved.Remaining > duration
            || saved.Duration != duration || saved.Moves is not { Length: > 0 }
            || saved.Moves.Any(move => move is null)
            || saved.Moves.Select(move => move.Member).Distinct(StringComparer.Ordinal).Count() != saved.Moves.Length)
            throw new InvalidDataException("Invalid saved formation transition timing or participants.");
        Dictionary<string, string> final = party.Members.ToDictionary(member => member.InstanceId, member => member.Position);
        foreach (FormationMove move in saved.Moves)
        {
            var member = party.Members.SingleOrDefault(member => member.InstanceId == move.Member);
            var destination = party.Positions.SingleOrDefault(position => position.Id == move.To);
            if (member is null || member.Definition.Commander || member.Position != move.From || move.From == move.To
                || destination is null || destination.Commander)
                throw new InvalidDataException("Invalid saved formation transition destination.");
            final[move.Member] = move.To;
        }
        if (final.Values.Distinct(StringComparer.Ordinal).Count() != final.Count)
            throw new InvalidDataException("Formation destinations must be unique.");
    }
}
