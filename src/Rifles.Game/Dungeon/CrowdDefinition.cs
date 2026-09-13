using Rifles.Procgen.Generation;
namespace Rifles.Game.Dungeon;

/// <summary>Authored within-cell occupancy for the bounded crowd policy.</summary>
internal sealed record CrowdDefinition(
    int Capacity,
    int WaitingAttemptLimit,
    IReadOnlyDictionary<string, FootprintDefinition> Footprints)
{
    internal const uint QuadrantMask = 0b1111;

    internal void Validate()
    {
        if (Capacity <= 0) throw new InvalidDataException("Crowd capacity must be positive.");
        if (WaitingAttemptLimit <= 0) throw new InvalidDataException("Crowd waiting attempt limit must be positive.");
        if (Footprints is null || Footprints.Count == 0) throw new InvalidDataException("Crowd footprints are required.");

        foreach ((string id, FootprintDefinition footprint) in Footprints)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("Crowd footprint id is required.");
            footprint.Validate(Capacity, id);
        }
    }

    internal FootprintDefinition Footprint(string id)
    {
        return Footprints.TryGetValue(id, out FootprintDefinition? footprint)
            ? footprint
            : throw new InvalidDataException($"Unknown crowd footprint '{id}'.");
    }
}

internal sealed record FootprintDefinition(
    int Size,
    int Cost,
    float EdgeClearance,
    IReadOnlyList<PlacementDefinition> Placements)
{
    internal void Validate(int capacity, string footprintId)
    {
        if (Size <= 0 || Size > 4) throw new InvalidDataException($"Crowd footprint '{footprintId}' size must use one to four fixed quadrants.");
        if (Cost <= 0 || Cost > capacity) throw new InvalidDataException($"Crowd footprint '{footprintId}' cost is outside capacity.");
        if (!float.IsFinite(EdgeClearance) || EdgeClearance < 0)
            throw new InvalidDataException($"Crowd footprint '{footprintId}' edge clearance is invalid.");
        if (Placements is null || Placements.Count == 0)
            throw new InvalidDataException($"Crowd footprint '{footprintId}' needs a placement.");

        HashSet<string> ids = [];
        foreach (PlacementDefinition placement in Placements)
        {
            if (!ids.Add(placement.Id)) throw new InvalidDataException($"Crowd footprint '{footprintId}' repeats placement '{placement.Id}'.");
            placement.Validate(footprintId);
            if (System.Numerics.BitOperations.PopCount(placement.Mask) != Size)
                throw new InvalidDataException($"Crowd footprint '{footprintId}' placement '{placement.Id}' mask does not match its size.");
        }

        for (int index = 0; index < Placements.Count; index++)
        for (int other = index + 1; other < Placements.Count; other++)
        {
            PlacementDefinition first = Placements[index];
            PlacementDefinition second = Placements[other];
            if (first.Overlaps(second) && (first.Mask & second.Mask) == 0)
                throw new InvalidDataException($"Crowd footprint '{footprintId}' has overlapping placement bounds without a shared mask.");
        }
    }

    internal PlacementDefinition Placement(string? id)
    {
        if (id is null) return Placements[0];
        return Placements.FirstOrDefault(placement => placement.Id == id)
            ?? throw new InvalidDataException($"Unknown crowd placement '{id}'.");
    }
}

/// <summary>
/// A placement is centered in normalized cell coordinates. Its low four mask bits
/// identify the fixed quarter-cells that may overlap another sharing actor.
/// </summary>
internal sealed record PlacementDefinition(
    string Id,
    uint Mask,
    float OffsetX,
    float OffsetY,
    float Width,
    float Depth)
{
    internal void Validate(string footprintId)
    {
        if (string.IsNullOrWhiteSpace(Id)) throw new InvalidDataException($"Crowd footprint '{footprintId}' has an unnamed placement.");
        if ((Mask & ~CrowdDefinition.QuadrantMask) != 0 || Mask == 0)
            throw new InvalidDataException($"Crowd placement '{Id}' has an invalid quadrant mask.");
        if (!float.IsFinite(OffsetX) || !float.IsFinite(OffsetY) || !float.IsFinite(Width) || !float.IsFinite(Depth)
            || Width <= 0 || Depth <= 0 || Width > 1 || Depth > 1
            || OffsetX - Width / 2 < -.5f || OffsetX + Width / 2 > .5f
            || OffsetY - Depth / 2 < -.5f || OffsetY + Depth / 2 > .5f)
            throw new InvalidDataException($"Crowd placement '{Id}' is outside normalized cell bounds.");
    }

    internal bool Overlaps(PlacementDefinition other) =>
        OffsetX - Width / 2 < other.OffsetX + other.Width / 2
        && OffsetX + Width / 2 > other.OffsetX - other.Width / 2
        && OffsetY - Depth / 2 < other.OffsetY + other.Depth / 2
        && OffsetY + Depth / 2 > other.OffsetY - other.Depth / 2;
}

internal readonly record struct CrowdBlockedState(ulong ActorId, GridPoint Destination, int Attempts, bool Expired, string Reason);
