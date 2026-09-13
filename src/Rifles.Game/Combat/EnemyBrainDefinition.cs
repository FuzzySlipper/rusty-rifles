using Rifles.Game.Content;

namespace Rifles.Game.Combat;

/// <summary>Authored attention, search, and ranged-position preferences for one enemy kind.</summary>
internal sealed record EnemyBrainDefinition(
    double MemorySeconds,
    double SearchSeconds,
    int MaxRetreatSteps,
    double RetreatCooldownSeconds,
    float PreferredMinimumRange,
    float PreferredMaximumRange,
    double PathRetrySeconds,
    float SightConeDegrees,
    float GunfireHearingRange,
    float FootstepHearingRange,
    float AlarmHearingRange)
{
    internal void Validate()
    {
        GameDefinitions.Require(double.IsFinite(MemorySeconds) && MemorySeconds > 0, nameof(MemorySeconds));
        GameDefinitions.Require(double.IsFinite(SearchSeconds) && SearchSeconds > 0, nameof(SearchSeconds));
        GameDefinitions.Require(MaxRetreatSteps >= 0, nameof(MaxRetreatSteps));
        GameDefinitions.Require(double.IsFinite(RetreatCooldownSeconds) && RetreatCooldownSeconds >= 0, nameof(RetreatCooldownSeconds));
        GameDefinitions.Require(float.IsFinite(PreferredMinimumRange) && PreferredMinimumRange >= 0
            && float.IsFinite(PreferredMaximumRange) && PreferredMaximumRange >= PreferredMinimumRange, "preferred range");
        GameDefinitions.Require(double.IsFinite(PathRetrySeconds) && PathRetrySeconds > 0, nameof(PathRetrySeconds));
        GameDefinitions.Require(float.IsFinite(SightConeDegrees) && SightConeDegrees is > 0 and <= 360, nameof(SightConeDegrees));
        GameDefinitions.Require(float.IsFinite(GunfireHearingRange) && GunfireHearingRange >= 0
            && float.IsFinite(FootstepHearingRange) && FootstepHearingRange >= 0
            && float.IsFinite(AlarmHearingRange) && AlarmHearingRange >= 0, "hearing ranges");
    }
}
