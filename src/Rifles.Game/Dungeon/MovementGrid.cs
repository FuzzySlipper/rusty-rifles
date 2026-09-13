using Rifles.Procgen.Generation;
namespace Rifles.Game.Dungeon;

internal readonly record struct GridEdge(GridPoint A, GridPoint B)
{
    internal static GridEdge Between(GridPoint a, GridPoint b) => a.Y < b.Y || a.Y == b.Y && a.X < b.X ? new(a, b) : new(b, a);
}
internal sealed record MovementReservation(ulong ActorId, GridPoint From, GridPoint To);

/// <summary>Product occupancy policy over Engine step admission; no alternate pathfinder.</summary>
internal sealed class MovementGrid(IReadOnlySet<GridPoint> cells, Func<GridPoint, GridPoint, bool> admit)
{
    private readonly Dictionary<ulong, GridPoint> occupants = [];
    private readonly Dictionary<ulong, MovementReservation> reservations = [];
    private readonly HashSet<GridEdge> blockedEdges = [];
    internal void Add(ulong actor, GridPoint cell)
    {
        if (actor == 0 || !cells.Contains(cell) || occupants.ContainsKey(actor) || Occupied(cell))
            throw new InvalidDataException("Invalid or occupied actor placement.");
        occupants.Add(actor, cell);
    }
    internal GridPoint Position(ulong actor) => occupants[actor];
    internal bool Occupied(GridPoint cell) => occupants.Values.Contains(cell) || reservations.Values.Any(r => r.To == cell);
    internal void SetBlocked(GridPoint a, GridPoint b, bool blocked)
    {
        if (a.ManhattanDistance(b) != 1) throw new ArgumentException("An edge must join adjacent cells.");
        GridEdge edge = GridEdge.Between(a, b);
        if (blocked) blockedEdges.Add(edge); else blockedEdges.Remove(edge);
        foreach (MovementReservation reservation in reservations.Values.Where(r => GridEdge.Between(r.From, r.To) == edge).ToArray())
            Cancel(reservation.ActorId);
    }
    internal bool TryReserve(ulong actor, GridPoint destination)
    {
        if (!occupants.TryGetValue(actor, out GridPoint from) || reservations.ContainsKey(actor)
            || from.ManhattanDistance(destination) != 1 || !cells.Contains(destination)
            || blockedEdges.Contains(GridEdge.Between(from, destination)) || Occupied(destination)
            || reservations.Values.Any(r => GridEdge.Between(r.From, r.To) == GridEdge.Between(from, destination))
            || !admit(from, destination)) return false;
        reservations.Add(actor, new(actor, from, destination));
        return true;
    }
    internal bool Commit(ulong actor)
    {
        if (!reservations.Remove(actor, out MovementReservation? reservation)) return false;
        if (blockedEdges.Contains(GridEdge.Between(reservation.From, reservation.To)) || !admit(reservation.From, reservation.To)) return false;
        occupants[actor] = reservation.To;
        return true;
    }
    internal void Cancel(ulong actor) => reservations.Remove(actor);
}
