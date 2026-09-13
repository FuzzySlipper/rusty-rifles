using Rifles.Procgen.Generation;
namespace Rifles.Game.Dungeon;

internal readonly record struct GridEdge(GridPoint A, GridPoint B)
{
    internal static GridEdge Between(GridPoint a, GridPoint b) => a.Y < b.Y || a.Y == b.Y && a.X < b.X ? new(a, b) : new(b, a);
}

internal sealed record MovementReservation(ulong ActorId, GridPoint From, GridPoint To, string? SourcePlacementId, string? DestinationPlacementId);
internal sealed record CrowdActor(string FootprintId, string Faction, bool Shares, string PlacementId);
internal sealed record WaitingIntent(ulong ActorId, GridPoint Destination, string? PlacementId, long Order, int Attempts);

/// <summary>Product occupancy policy over Engine step admission; no alternate pathfinder.</summary>
internal sealed class MovementGrid
{
    private static readonly PlacementDefinition ExclusivePlacement = new("exclusive", CrowdDefinition.QuadrantMask, 0, 0, 1, 1);
    private readonly IReadOnlySet<GridPoint> cells;
    private readonly Func<GridPoint, GridPoint, bool> admit;
    private readonly CrowdDefinition? crowd;
    private readonly Dictionary<ulong, GridPoint> occupants = [];
    private readonly Dictionary<ulong, CrowdActor> crowdActors = [];
    private readonly Dictionary<ulong, MovementReservation> reservations = [];
    private readonly HashSet<GridEdge> blockedEdges = [];
    private readonly Dictionary<GridEdge, float> clearances = [];
    private readonly Dictionary<ulong, WaitingIntent> waiting = [];
    private readonly Dictionary<ulong, CrowdBlockedState> blocked = [];
    private long nextWaitOrder;

    internal MovementGrid(IReadOnlySet<GridPoint> cells, Func<GridPoint, GridPoint, bool> admit, CrowdDefinition? crowd = null)
    {
        this.cells = cells ?? throw new ArgumentNullException(nameof(cells));
        this.admit = admit ?? throw new ArgumentNullException(nameof(admit));
        this.crowd = crowd;
        crowd?.Validate();
    }

    /// <summary>Adds a party member, prop, or legacy actor with exclusive-cell semantics.</summary>
    internal void Add(ulong actor, GridPoint cell)
    {
        ValidateNewActor(actor, cell);
        if (Occupied(cell)) throw new InvalidDataException("Invalid or occupied actor placement.");
        occupants.Add(actor, cell);
    }

    /// <summary>Adds a crowd actor at its supplied or first fitting authored placement.</summary>
    internal void Add(ulong actor, GridPoint cell, string footprintId, string faction, bool share, string? placementId = null)
    {
        ValidateNewActor(actor, cell);
        if (crowd is null) throw new InvalidOperationException("Crowd placement needs a crowd definition.");
        if (string.IsNullOrWhiteSpace(faction)) throw new InvalidDataException("Crowd faction is required.");

        FootprintDefinition footprint = crowd.Footprint(footprintId);
        PlacementDefinition placement = SelectPlacement(new CrowdActor(footprintId, faction, share, footprint.Placement(placementId).Id), cell, placementId, actor)
            ?? throw new InvalidDataException("Crowd actor placement does not fit.");
        occupants.Add(actor, cell);
        crowdActors.Add(actor, new CrowdActor(footprintId, faction, share, placement.Id));
    }

    internal GridPoint Position(ulong actor) => occupants[actor];
    internal IEnumerable<GridPoint> BlockedCells => occupants.Values.Concat(reservations.Values.Select(reservation => reservation.To)).Distinct();
    internal bool Occupied(GridPoint cell) => occupants.Values.Contains(cell) || reservations.Values.Any(reservation => reservation.To == cell);

    internal PlacementDefinition Placement(ulong actor)
    {
        if (!occupants.ContainsKey(actor)) throw new KeyNotFoundException($"Unknown actor '{actor}'.");
        if (!crowdActors.TryGetValue(actor, out CrowdActor? profile)) return ExclusivePlacement;
        return Footprint(profile).Placement(profile.PlacementId);
    }

    internal PlacementDefinition? DestinationPlacement(ulong actor) => reservations.TryGetValue(actor, out MovementReservation? reservation)
        && reservation.DestinationPlacementId is not null && crowdActors.TryGetValue(actor, out CrowdActor? profile)
            ? Footprint(profile).Placement(reservation.DestinationPlacementId)
            : null;

    internal CrowdBlockedState? Blocked(ulong actor) => blocked.TryGetValue(actor, out CrowdBlockedState state) ? state : null;

    internal void SetBlocked(GridPoint a, GridPoint b, bool isBlocked)
    {
        RequireEdge(a, b);
        GridEdge edge = GridEdge.Between(a, b);
        if (isBlocked) blockedEdges.Add(edge); else blockedEdges.Remove(edge);
        foreach (MovementReservation reservation in reservations.Values.Where(reservation => GridEdge.Between(reservation.From, reservation.To) == edge).ToArray())
            Cancel(reservation.ActorId);
    }

    internal void SetClearance(GridPoint from, GridPoint to, float width)
    {
        RequireEdge(from, to);
        if (!float.IsFinite(width) || width < 0) throw new ArgumentOutOfRangeException(nameof(width));
        clearances[GridEdge.Between(from, to)] = width;
    }

    internal bool CanFit(ulong actor, GridPoint cell)
    {
        if (!occupants.ContainsKey(actor) || !cells.Contains(cell)) return false;
        if (!crowdActors.TryGetValue(actor, out CrowdActor? profile)) return ExclusiveFit(actor, cell);
        return SelectPlacement(profile, cell, null, actor) is not null;
    }

    /// <summary>Checks a prospective crowd profile before it receives an actor identity.</summary>
    internal bool CanFit(GridPoint cell, string footprintId, string faction, bool share)
    {
        if (crowd is null || !cells.Contains(cell) || string.IsNullOrWhiteSpace(faction)) return false;
        try
        {
            FootprintDefinition footprint = crowd.Footprint(footprintId);
            CrowdActor prospective = new(footprintId, faction, share, footprint.Placement(null).Id);
            return SelectPlacement(prospective, cell, null, 0) is not null;
        }
        catch (InvalidDataException) { return false; }
    }

    internal bool TryReserve(ulong actor, GridPoint destination) => TryReserve(actor, destination, null);

    internal bool TryReserve(ulong actor, GridPoint destination, string? destinationPlacementId)
    {
        if (!CanAttempt(actor, destination, out GridPoint from)) return false;
        if (waiting.TryGetValue(actor, out WaitingIntent? previous) && (previous.Destination != destination || previous.PlacementId != destinationPlacementId))
            CancelWaiting(actor);

        if (YieldToEarlierWaiter(actor, destination, destinationPlacementId))
        {
            RegisterWait(actor, destination, destinationPlacementId);
            return false;
        }
        if (!CanTraverse(actor, from, destination)) return false;

        PlacementDefinition? placement = ReservePlacement(actor, destination, destinationPlacementId);
        if (crowdActors.ContainsKey(actor) && placement is null)
        {
            RegisterWait(actor, destination, destinationPlacementId);
            return false;
        }
        if (!crowdActors.ContainsKey(actor) && !ExclusiveFit(actor, destination)) return false;

        CrowdActor? profile = crowdActors.GetValueOrDefault(actor);
        reservations.Add(actor, new MovementReservation(actor, from, destination, profile?.PlacementId, placement?.Id));
        ClearWait(actor);
        return true;
    }

    /// <summary>Restores an in-transit crowd actor using its exact saved source and destination anchors.</summary>
    internal bool RestoreReservation(ulong actor, GridPoint destination, string? sourcePlacementId, string? destinationPlacementId)
    {
        if (!CanAttempt(actor, destination, out GridPoint from) || !CanTraverse(actor, from, destination)) return false;
        if (!crowdActors.TryGetValue(actor, out CrowdActor? profile)) return TryReserve(actor, destination);
        if (sourcePlacementId is null || destinationPlacementId is null) return false;

        PlacementDefinition source = Footprint(profile).Placement(sourcePlacementId);
        CrowdActor restored = profile with { PlacementId = source.Id };
        if (!Fits(restored, from, source, actor)) return false;
        PlacementDefinition? target = SelectPlacement(restored, destination, destinationPlacementId, actor);
        if (target is null || !LaneClear(actor, from, destination, source, target)) return false;

        crowdActors[actor] = restored;
        reservations.Add(actor, new MovementReservation(actor, from, destination, source.Id, target.Id));
        ClearWait(actor);
        return true;
    }

    internal bool Commit(ulong actor)
    {
        if (!reservations.Remove(actor, out MovementReservation? reservation)) return false;
        if (blockedEdges.Contains(GridEdge.Between(reservation.From, reservation.To)) || !admit(reservation.From, reservation.To)
            || !HasClearance(actor, reservation.From, reservation.To)) return false;
        occupants[actor] = reservation.To;
        if (reservation.DestinationPlacementId is not null && crowdActors.TryGetValue(actor, out CrowdActor? profile))
            crowdActors[actor] = profile with { PlacementId = reservation.DestinationPlacementId };
        return true;
    }

    internal void Remove(ulong actor)
    {
        Cancel(actor);
        CancelWaiting(actor);
        occupants.Remove(actor);
        crowdActors.Remove(actor);
    }

    internal void Cancel(ulong actor)
    {
        reservations.Remove(actor);
        CancelWaiting(actor);
    }

    /// <summary>Cleans a cancelled/dead actor's fairness intent and its observable blocked state.</summary>
    internal bool CancelWaiting(ulong actor)
    {
        bool removed = waiting.Remove(actor);
        blocked.Remove(actor);
        return removed;
    }

    private void ValidateNewActor(ulong actor, GridPoint cell)
    {
        if (actor == 0 || !cells.Contains(cell) || occupants.ContainsKey(actor))
            throw new InvalidDataException("Invalid or occupied actor placement.");
    }

    private FootprintDefinition Footprint(CrowdActor profile) => crowd?.Footprint(profile.FootprintId)
        ?? throw new InvalidOperationException("Crowd profile used without a crowd definition.");

    private bool CanAttempt(ulong actor, GridPoint destination, out GridPoint from)
    {
        return occupants.TryGetValue(actor, out from) && !reservations.ContainsKey(actor)
            && from.ManhattanDistance(destination) == 1 && cells.Contains(destination);
    }

    private bool CanTraverse(ulong actor, GridPoint from, GridPoint destination) =>
        !blockedEdges.Contains(GridEdge.Between(from, destination))
        && !reservations.Values.Any(reservation => GridEdge.Between(reservation.From, reservation.To) == GridEdge.Between(from, destination))
        && admit(from, destination)
        && HasClearance(actor, from, destination);

    private bool HasClearance(ulong actor, GridPoint from, GridPoint destination)
    {
        if (!crowdActors.TryGetValue(actor, out CrowdActor? profile)) return true;
        return !clearances.TryGetValue(GridEdge.Between(from, destination), out float width)
            || width >= Footprint(profile).EdgeClearance;
    }

    private PlacementDefinition? ReservePlacement(ulong actor, GridPoint destination, string? requestedPlacementId)
    {
        if (!crowdActors.TryGetValue(actor, out CrowdActor? profile)) return null;
        GridPoint source = occupants[actor];
        IEnumerable<PlacementDefinition> choices = requestedPlacementId is not null
            ? [Footprint(profile).Placement(requestedPlacementId)]
            : Footprint(profile).Placements.OrderBy(p => p.Id != profile.PlacementId);
        return choices.FirstOrDefault(p => Fits(profile, destination, p, actor)
            && LaneClear(actor, source, destination, Placement(actor), p));
    }

    private PlacementDefinition? SelectPlacement(CrowdActor profile, GridPoint cell, string? requestedPlacementId, ulong actor)
    {
        FootprintDefinition footprint = Footprint(profile);
        if (requestedPlacementId is not null)
        {
            PlacementDefinition requested = footprint.Placement(requestedPlacementId);
            return Fits(profile, cell, requested, actor) ? requested : null;
        }

        PlacementDefinition current = footprint.Placement(profile.PlacementId);
        if (Fits(profile, cell, current, actor)) return current;
        return footprint.Placements.FirstOrDefault(placement => Fits(profile, cell, placement, actor));
    }

    private bool ExclusiveFit(ulong actor, GridPoint cell) => !AnchorsAt(cell, actor).Any();

    private bool Fits(CrowdActor candidate, GridPoint cell, PlacementDefinition placement, ulong actor)
    {
        int usedCapacity = Footprint(candidate).Cost;
        foreach ((ulong otherActor, CrowdActor? otherProfile, PlacementDefinition? otherPlacement) in AnchorsAt(cell, actor))
        {
            if (otherProfile is null || otherPlacement is null) return false;
            if (!candidate.Shares || !otherProfile.Shares || candidate.Faction != otherProfile.Faction) return false;
            usedCapacity += Footprint(otherProfile).Cost;
            if ((placement.Mask & otherPlacement.Mask) != 0 || placement.Overlaps(otherPlacement)) return false;
        }
        if (usedCapacity > crowd!.Capacity) return false;
        // A new/loaded anchor cannot appear inside an already admitted departure lane.
        CrowdLane bounds = CrowdLane.At(cell, placement);
        return !reservations.Values.Any(r => r.ActorId != actor && ReservationLane(r).Overlaps(bounds));
    }

    private CrowdLane ReservationLane(MovementReservation reservation)
    {
        PlacementDefinition source = Placement(reservation.ActorId);
        PlacementDefinition target = DestinationPlacement(reservation.ActorId) ?? ExclusivePlacement;
        return CrowdLane.Between(reservation.From, source, reservation.To, target);
    }

    private bool LaneClear(ulong actor, GridPoint from, GridPoint to, PlacementDefinition source, PlacementDefinition target)
    {
        CrowdLane lane = CrowdLane.Between(from, source, to, target);
        foreach (GridPoint cell in new[] { from, to })
            foreach (var anchor in AnchorsAt(cell, actor))
                if (lane.Overlaps(CrowdLane.At(cell, anchor.Placement ?? ExclusivePlacement))) return false;
        return !reservations.Values.Any(r => r.ActorId != actor && lane.Overlaps(ReservationLane(r)));
    }

    private IEnumerable<(ulong Actor, CrowdActor? Profile, PlacementDefinition? Placement)> AnchorsAt(GridPoint cell, ulong excludedActor)
    {
        foreach ((ulong actor, GridPoint position) in occupants)
        {
            if (actor == excludedActor || position != cell) continue;
            yield return (actor, crowdActors.GetValueOrDefault(actor), crowdActors.TryGetValue(actor, out CrowdActor? profile) ? Footprint(profile).Placement(profile.PlacementId) : null);
        }
        foreach (MovementReservation reservation in reservations.Values)
        {
            if (reservation.ActorId == excludedActor || reservation.To != cell) continue;
            yield return (reservation.ActorId, crowdActors.GetValueOrDefault(reservation.ActorId),
                crowdActors.TryGetValue(reservation.ActorId, out CrowdActor? profile) && reservation.DestinationPlacementId is not null
                    ? Footprint(profile).Placement(reservation.DestinationPlacementId) : null);
        }
    }

    private bool YieldToEarlierWaiter(ulong actor, GridPoint destination, string? placementId)
    {
        long currentOrder = waiting.TryGetValue(actor, out WaitingIntent? current) ? current.Order : nextWaitOrder;
        WaitingIntent? earlier = waiting.Values
            .Where(intent => intent.ActorId != actor && intent.Destination == destination && intent.Order < currentOrder)
            .OrderBy(intent => intent.Order)
            .FirstOrDefault();
        if (earlier is null) return false;

        // Let a blocking front actor leave when the older rear actor has no clear lane.
        bool eligible = CanAttempt(earlier.ActorId, destination, out GridPoint from)
            && CanTraverse(earlier.ActorId, from, destination)
            && (crowdActors.ContainsKey(earlier.ActorId)
                ? ReservePlacement(earlier.ActorId, destination, earlier.PlacementId) is not null
                : ExclusiveFit(earlier.ActorId, destination));
        ExpireWait(earlier);
        return eligible;
    }

    private void RegisterWait(ulong actor, GridPoint destination, string? placementId)
    {
        WaitingIntent intent = waiting.TryGetValue(actor, out WaitingIntent? existing)
            ? existing with { Attempts = existing.Attempts + 1 }
            : new WaitingIntent(actor, destination, placementId, nextWaitOrder++, 1);
        if (intent.Attempts >= crowd!.WaitingAttemptLimit)
        {
            waiting.Remove(actor);
            blocked[actor] = new CrowdBlockedState(actor, destination, intent.Attempts, true, "Waiting intent expired without a fitting placement.");
        }
        else
        {
            waiting[actor] = intent;
            blocked[actor] = new CrowdBlockedState(actor, destination, intent.Attempts, false, "Destination is occupied or over crowd capacity.");
        }
    }

    private void ExpireWait(WaitingIntent intent)
    {
        WaitingIntent next = intent with { Attempts = intent.Attempts + 1 };
        if (next.Attempts >= crowd!.WaitingAttemptLimit)
        {
            waiting.Remove(intent.ActorId);
            blocked[intent.ActorId] = new CrowdBlockedState(intent.ActorId, intent.Destination, next.Attempts, true, "Waiting intent expired before it could be admitted.");
        }
        else
        {
            waiting[intent.ActorId] = next;
            blocked[intent.ActorId] = new CrowdBlockedState(intent.ActorId, intent.Destination, next.Attempts, false, "An earlier waiting intent has priority.");
        }
    }

    private void ClearWait(ulong actor)
    {
        waiting.Remove(actor);
        blocked.Remove(actor);
    }

    private static void RequireEdge(GridPoint a, GridPoint b)
    {
        if (a.ManhattanDistance(b) != 1) throw new ArgumentException("An edge must join adjacent cells.");
    }
}

// Conservative reservation bounds in normalized grid units, not a second world collision query.
// Engine still admits the cell edge; this prevents authored crowd anchors crossing one another.
internal readonly record struct CrowdLane(float Left, float Top, float Right, float Bottom)
{
    internal static CrowdLane At(GridPoint cell, PlacementDefinition placement) => new(
        cell.X + placement.OffsetX - placement.Width / 2, cell.Y + placement.OffsetY - placement.Depth / 2,
        cell.X + placement.OffsetX + placement.Width / 2, cell.Y + placement.OffsetY + placement.Depth / 2);
    internal static CrowdLane Between(GridPoint from, PlacementDefinition source, GridPoint to, PlacementDefinition target)
    {
        CrowdLane a = At(from, source), b = At(to, target);
        return new(Math.Min(a.Left, b.Left), Math.Min(a.Top, b.Top), Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));
    }
    internal bool Overlaps(CrowdLane other) => Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;
}
