using System.Numerics;
using Rifles.Game.Content;
using Rifles.Procgen.Generation;

namespace Rifles.Game.Party;

internal enum FormationAttackSector { Front, Right, Rear, Left }

/// <summary>Front/rear sectors use Left/Center/Right; flanks use Front/Center/Rear.</summary>
internal enum FormationScreeningLane { Left, Center, Right, Front, Rear }
internal enum OffensiveLane { Right = -1, Center = 0, Left = 1 }

internal sealed record FormationScreeningDefinition(FormationAttackSector Sector, FormationScreeningLane Lane);

/// <summary>One internal 3x3 cell. The center is reserved for the fixed commander.</summary>
internal sealed record FormationCellDefinition(string Id, string Name, int Forward, int Left, bool Commander,
    FormationScreeningDefinition[] Screening)
{
    internal void Validate()
    {
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(Name)
            && Forward is >= -1 and <= 1 && Left is >= -1 and <= 1 && Screening is not null, "formation cell fields");
        GameDefinitions.Require(Screening.All(screen => screen is not null && FormationRules.IsValid(screen)), "formation screening");
        GameDefinitions.Require(Screening.Distinct().Count() == Screening.Length, "duplicate formation screening");
        GameDefinitions.Require(!Commander || Screening.Length == 0, "commander screening");
    }
}

/// <summary>Physical reach uses actual world-relative target offsets supplied by combat.</summary>
internal sealed record MartialWeaponReachDefinition(string Id, string Name, int MaximumRowsBehindFront, int MaximumLateralLaneDifference,
    float MaximumForwardDistance, float AimHalfAngleDegrees)
{
    internal void Validate()
    {
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(Name)
            && MaximumRowsBehindFront is >= 0 and <= 2 && MaximumLateralLaneDifference is >= 0 and <= 2 && float.IsFinite(MaximumForwardDistance)
            && MaximumForwardDistance > 0 && float.IsFinite(AimHalfAngleDegrees)
            && AimHalfAngleDegrees is >= 0 and < 90, "martial weapon reach " + Id);
    }
}

internal sealed record FormationDefinition(float LaneBoundaryRatio, float OffensiveLaneWidth, int MaximumLaneFallback,
    FormationCellDefinition[] Cells, MartialWeaponReachDefinition[] Weapons, double RepositionSeconds)
{
    internal void Validate()
    {
        GameDefinitions.Require(double.IsFinite(RepositionSeconds) && RepositionSeconds > 0, "formation reposition seconds");
        GameDefinitions.Require(float.IsFinite(LaneBoundaryRatio) && LaneBoundaryRatio is > 0 and < 1
            && float.IsFinite(OffensiveLaneWidth) && OffensiveLaneWidth > 0
            && MaximumLaneFallback is >= 0 and <= 2 && Cells is { Length: 9 } && Weapons is { Length: > 0 }, "formation tuning");
        foreach (FormationCellDefinition cell in Cells)
        {
            if (cell is null) throw new InvalidDataException("Invalid formation cell.");
            cell.Validate();
        }
        GameDefinitions.Require(Cells.Select(cell => cell.Id).Distinct(StringComparer.Ordinal).Count() == Cells.Length
            && Cells.Select(cell => (cell.Forward, cell.Left)).Distinct().Count() == Cells.Length, "formation cell identities");
        GameDefinitions.Require(Cells.Select(cell => (cell.Forward, cell.Left)).ToHashSet().SetEquals(
            Enumerable.Range(-1, 3).SelectMany(forward => Enumerable.Range(-1, 3).Select(left => (forward, left)))), "formation 3x3 layout");
        GameDefinitions.Require(Cells.Count(cell => cell.Commander) == 1
            && Cells.Single(cell => cell.Commander).Forward == 0 && Cells.Single(cell => cell.Commander).Left == 0, "formation commander center");
        foreach (MartialWeaponReachDefinition weapon in Weapons)
        {
            if (weapon is null) throw new InvalidDataException("Invalid martial weapon reach.");
            weapon.Validate();
        }
        GameDefinitions.Require(Weapons.Select(weapon => weapon.Id).Distinct(StringComparer.Ordinal).Count() == Weapons.Length, "martial weapon identities");
    }

    internal FormationCellDefinition Cell(string id) => Cells.Single(cell => cell.Id == id);
    internal MartialWeaponReachDefinition Weapon(string id) => Weapons.Single(weapon => weapon.Id == id);
}

internal readonly record struct FormationApproach(FormationAttackSector Sector, FormationScreeningLane Lane);
internal sealed record FormationOccupant(string Id, string CellId, bool Living);
/// <summary>Exposed world target in metres on the party's facing-local axes.</summary>
internal sealed record FormationTarget(string Id, float ForwardDistance, float LeftOffset, bool Exposed);

/// <summary>Pure coverage and preference rules. This class neither queries Engine nor mutates party state.</summary>
internal static class FormationRules
{
    /// <summary>
    /// SourceOffset is hostile world position (including crowd offset) minus party world center.
    /// The lane band is relative to the dominant axis, so it remains meaningful at every range.
    /// </summary>
    internal static FormationApproach DeriveApproach(CardinalDirection facing, Vector2 sourceOffset, float laneBoundaryRatio)
    {
        if (!Enum.IsDefined(facing) || !float.IsFinite(sourceOffset.X) || !float.IsFinite(sourceOffset.Y)
            || sourceOffset.LengthSquared() == 0 || !float.IsFinite(laneBoundaryRatio) || laneBoundaryRatio is <= 0 or >= 1)
            throw new InvalidDataException("Formation approach needs a non-zero finite source offset and a lane boundary ratio between zero and one.");

        GridPoint forwardAxis = facing.Offset();
        GridPoint leftAxis = facing.Rotate(-1).Offset();
        float forward = sourceOffset.X * forwardAxis.X + sourceOffset.Y * forwardAxis.Y;
        float left = sourceOffset.X * leftAxis.X + sourceOffset.Y * leftAxis.Y;
        if (MathF.Abs(forward) >= MathF.Abs(left))
        {
            FormationAttackSector sector = forward >= 0 ? FormationAttackSector.Front : FormationAttackSector.Rear;
            return new FormationApproach(sector, FrontOrRearLane(left, MathF.Abs(forward), laneBoundaryRatio));
        }
        FormationAttackSector side = left >= 0 ? FormationAttackSector.Left : FormationAttackSector.Right;
        return new FormationApproach(side, SideLane(forward, MathF.Abs(left), laneBoundaryRatio));
    }

    /// <summary>One matching soldier screens one incoming hit. A null result exposes the commander; damage never spills.</summary>
    internal static string? SelectScreeningRecipient(FormationDefinition definition, FormationApproach approach,
        IEnumerable<FormationOccupant> occupants)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(occupants);
        foreach (FormationOccupant occupant in occupants.Where(occupant => occupant is not null && occupant.Living)
            .OrderBy(occupant => occupant.Id, StringComparer.Ordinal))
        {
            FormationCellDefinition? cell = definition.Cells.SingleOrDefault(candidate => candidate.Id == occupant.CellId);
            if (cell is not null && !cell.Commander && cell.Screening.Any(screen => screen.Sector == approach.Sector && screen.Lane == approach.Lane))
                return occupant.Id;
        }
        return null;
    }

    internal static bool CanReach(FormationDefinition definition, MartialWeaponReachDefinition weapon, FormationCellDefinition attacker, FormationTarget target)
    {
        ArgumentNullException.ThrowIfNull(weapon); ArgumentNullException.ThrowIfNull(attacker); ArgumentNullException.ThrowIfNull(target);
        if (attacker.Commander || !float.IsFinite(target.ForwardDistance) || !float.IsFinite(target.LeftOffset)
            || target.ForwardDistance <= 0 || target.ForwardDistance > weapon.MaximumForwardDistance
            || 1 - attacker.Forward > weapon.MaximumRowsBehindFront
            || Math.Abs(attacker.Left - (int)DeriveOffensiveLane(target.LeftOffset, definition.OffensiveLaneWidth)) > weapon.MaximumLateralLaneDifference) return false;
        float maximumLateral = target.ForwardDistance * MathF.Tan(weapon.AimHalfAngleDegrees * MathF.PI / 180f);
        return MathF.Abs(target.LeftOffset) <= maximumLateral;
    }

    /// <summary>Reach admits first; own lane, then adjacent lanes, distance and stable id decide the preference.</summary>
    internal static string? SelectPreferredTarget(FormationDefinition definition, string weaponId, string attackerCellId,
        IEnumerable<FormationTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(targets);
        MartialWeaponReachDefinition weapon = definition.Weapon(weaponId);
        FormationCellDefinition attacker = definition.Cell(attackerCellId);
        return targets.Where(target => target is not null && target.Exposed && CanReach(definition, weapon, attacker, target))
            .Where(target => Math.Abs(attacker.Left - (int)DeriveOffensiveLane(target.LeftOffset, definition.OffensiveLaneWidth)) <= definition.MaximumLaneFallback)
            .OrderBy(target => Math.Abs(attacker.Left - (int)DeriveOffensiveLane(target.LeftOffset, definition.OffensiveLaneWidth)))
            .ThenBy(target => target.ForwardDistance * target.ForwardDistance + target.LeftOffset * target.LeftOffset)
            .ThenBy(target => target.Id, StringComparer.Ordinal)
            .Select(target => target.Id).FirstOrDefault();
    }

    internal static bool IsValid(FormationScreeningDefinition screening) => screening.Sector switch
    {
        FormationAttackSector.Front or FormationAttackSector.Rear => screening.Lane is FormationScreeningLane.Left or FormationScreeningLane.Center or FormationScreeningLane.Right,
        FormationAttackSector.Left or FormationAttackSector.Right => screening.Lane is FormationScreeningLane.Front or FormationScreeningLane.Center or FormationScreeningLane.Rear,
        _ => false,
    };

    private static FormationScreeningLane FrontOrRearLane(float lateral, float dominant, float boundary) => lateral > dominant * boundary
        ? FormationScreeningLane.Left : lateral < -dominant * boundary ? FormationScreeningLane.Right : FormationScreeningLane.Center;
    private static FormationScreeningLane SideLane(float forward, float dominant, float boundary) => forward > dominant * boundary
        ? FormationScreeningLane.Front : forward < -dominant * boundary ? FormationScreeningLane.Rear : FormationScreeningLane.Center;
    internal static OffensiveLane DeriveOffensiveLane(float sourceLeftOffset, float laneWidth) => sourceLeftOffset >= laneWidth / 2
        ? OffensiveLane.Left : sourceLeftOffset <= -laneWidth / 2 ? OffensiveLane.Right : OffensiveLane.Center;
}
