using Rifles.Procgen.Generation;

namespace Rifles.Game.Combat;

/// <summary>
/// Live projectile state. Mutable position/remaining replace the previous
/// record-with snapshot mutation; <see cref="Capture"/> freezes the DTO.
/// </summary>
internal sealed class FlightState
{
    internal FlightState(ulong id, ulong shooter, string? member, CombatActionKind kind,
        float x, float y, float z, float directionX, float directionY, float directionZ,
        float remaining, GridPoint lastCell, string? owner, string? destination, string? spell = null)
    {
        Id = id; Shooter = shooter; Member = member; Kind = kind;
        X = x; Y = y; Z = z; DirectionX = directionX; DirectionY = directionY; DirectionZ = directionZ;
        Remaining = remaining; LastCell = lastCell; Owner = owner; Destination = destination; Spell = spell;
    }

    internal ulong Id { get; }
    internal ulong Shooter { get; }
    internal string? Member { get; }
    internal CombatActionKind Kind { get; }
    internal float X { get; set; }
    internal float Y { get; set; }
    internal float Z { get; set; }
    internal float DirectionX { get; }
    internal float DirectionY { get; }
    internal float DirectionZ { get; }
    internal float Remaining { get; set; }
    internal GridPoint LastCell { get; set; }
    internal string? Owner { get; }
    internal string? Destination { get; }
    internal string? Spell { get; }

    internal FlightSnapshot Capture() => new(Id, Shooter, Member, Kind, X, Y, Z,
        DirectionX, DirectionY, DirectionZ, Remaining, LastCell, Owner, Destination, Spell);

    internal static FlightState Restore(FlightSnapshot saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        return new FlightState(saved.Id, saved.Shooter, saved.Member, saved.Kind, saved.X, saved.Y, saved.Z,
            saved.DirectionX, saved.DirectionY, saved.DirectionZ, saved.Remaining, saved.LastCell,
            saved.Owner, saved.Destination, saved.Spell);
    }
}
