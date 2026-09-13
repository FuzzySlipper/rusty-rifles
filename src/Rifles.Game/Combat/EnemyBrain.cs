using Rifles.Procgen.Generation;

namespace Rifles.Game.Combat;

internal enum EnemyBrainMode { Patrol, Pursue, Search, Return }
internal enum EnemyBrainReason { None, VisibleTarget, HeardNoise, LostTarget, MemoryExpired, SearchExhausted, ReturnedHome }

/// <summary>All durable intent needed to resume a bounded enemy attention policy.</summary>
internal sealed record EnemyBrainSnapshot(
    GridPoint Home,
    GridPoint[] PatrolRoute,
    int PatrolIndex,
    EnemyBrainMode Mode,
    GridPoint? LastKnownTarget,
    double MemoryRemaining,
    double SearchRemaining,
    bool SearchingAtLastKnown,
    EnemyBrainReason Reason,
    int RetreatBudgetRemaining,
    double RetreatCooldownRemaining,
    double PathRetryRemaining);

/// <summary>
/// Pure enemy intent policy. Callers supply only current sight and heard-noise facts;
/// they retain spatial traces, hearing tests, path queries, and movement admission.
/// </summary>
internal sealed class EnemyBrain
{
    private readonly EnemyBrainDefinition definition;
    private readonly GridPoint[] patrolRoute;
    private int patrolIndex;
    private EnemyBrainMode mode;
    private GridPoint? lastKnownTarget;
    private double memoryRemaining;
    private double searchRemaining;
    private bool searchingAtLastKnown;
    private EnemyBrainReason reason;
    private int retreatBudgetRemaining;
    private double retreatCooldownRemaining;
    private double pathRetryRemaining;

    internal GridPoint Home { get; }
    internal EnemyBrainMode Mode => mode;
    internal EnemyBrainReason Reason => reason;
    internal GridPoint? LastKnownTarget => lastKnownTarget;
    internal bool RetreatReady => retreatBudgetRemaining > 0 && retreatCooldownRemaining == 0;
    internal bool CanRequestPath => pathRetryRemaining == 0;

    internal EnemyBrain(EnemyBrainDefinition definition, GridPoint home, GridPoint[] patrolRoute)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.Validate();
        ValidateRoute(patrolRoute);

        this.definition = definition;
        Home = home;
        this.patrolRoute = [.. patrolRoute];
        patrolIndex = 0;
        mode = EnemyBrainMode.Patrol;
        reason = EnemyBrainReason.None;
        retreatBudgetRemaining = definition.MaxRetreatSteps;
    }

    internal static EnemyBrain FromSnapshot(EnemyBrainDefinition definition, EnemyBrainSnapshot saved, IReadOnlySet<GridPoint> floorCells)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(floorCells);
        definition.Validate();
        ValidateRoute(saved.PatrolRoute);

        if (!floorCells.Contains(saved.Home) || saved.PatrolRoute.Any(cell => !floorCells.Contains(cell))
            || saved.LastKnownTarget is { } target && !floorCells.Contains(target)
            || saved.PatrolIndex < 0 || saved.PatrolIndex >= saved.PatrolRoute.Length
            || !Enum.IsDefined(saved.Mode) || !Enum.IsDefined(saved.Reason)
            || !ValidDuration(saved.MemoryRemaining, definition.MemorySeconds)
            || !ValidDuration(saved.SearchRemaining, definition.SearchSeconds)
            || !ValidDuration(saved.RetreatCooldownRemaining, definition.RetreatCooldownSeconds)
            || !ValidDuration(saved.PathRetryRemaining, definition.PathRetrySeconds)
            || saved.RetreatBudgetRemaining < 0 || saved.RetreatBudgetRemaining > definition.MaxRetreatSteps)
            throw new InvalidDataException("Invalid saved enemy brain.");

        ValidateModeState(definition, saved);

        EnemyBrain brain = new(definition, saved.Home, saved.PatrolRoute)
        {
            patrolIndex = saved.PatrolIndex,
            mode = saved.Mode,
            lastKnownTarget = saved.LastKnownTarget,
            memoryRemaining = saved.MemoryRemaining,
            searchRemaining = saved.SearchRemaining,
            searchingAtLastKnown = saved.SearchingAtLastKnown,
            reason = saved.Reason,
            retreatBudgetRemaining = saved.RetreatBudgetRemaining,
            retreatCooldownRemaining = saved.RetreatCooldownRemaining,
            pathRetryRemaining = saved.PathRetryRemaining,
        };
        return brain;
    }

    internal EnemyBrainSnapshot Capture() => new(Home, [.. patrolRoute], patrolIndex, mode, lastKnownTarget,
        memoryRemaining, searchRemaining, searchingAtLastKnown, reason, retreatBudgetRemaining,
        retreatCooldownRemaining, pathRetryRemaining);

    /// <summary>Updates attention from this decision's sensory facts. Noise directs a search but never becomes sight.</summary>
    internal void Observe(GridPoint? visibleTarget, GridPoint? heardNoise)
    {
        if (visibleTarget is { } target)
        {
            bool newEncounter = mode is EnemyBrainMode.Patrol or EnemyBrainMode.Return;
            EnterPursue(target, newEncounter);
            return;
        }

        if (heardNoise is { } noise)
        {
            if (mode is EnemyBrainMode.Patrol or EnemyBrainMode.Return)
                retreatBudgetRemaining = definition.MaxRetreatSteps;
            lastKnownTarget = noise;
            memoryRemaining = definition.MemorySeconds;
            searchRemaining = definition.SearchSeconds;
            searchingAtLastKnown = false;
            mode = EnemyBrainMode.Search;
            reason = EnemyBrainReason.HeardNoise;
            return;
        }

        if (mode == EnemyBrainMode.Pursue) EnterSearch(EnemyBrainReason.LostTarget);
    }

    /// <summary>Advances only admitted simulation time; no wall-clock expiry is used.</summary>
    internal void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));

        retreatCooldownRemaining = SpendTime(retreatCooldownRemaining, seconds);
        pathRetryRemaining = SpendTime(pathRetryRemaining, seconds);
        if (mode is EnemyBrainMode.Pursue or EnemyBrainMode.Search)
        {
            memoryRemaining = SpendTime(memoryRemaining, seconds);
            if (memoryRemaining == 0)
            {
                EnterReturn(EnemyBrainReason.MemoryExpired);
                return;
            }
        }

        if (mode != EnemyBrainMode.Search || !searchingAtLastKnown) return;
        searchRemaining = SpendTime(searchRemaining, seconds);
        if (searchRemaining == 0) EnterReturn(EnemyBrainReason.SearchExhausted);
    }

    /// <summary>Returns the single cell worth pursuing now and changes state only on a confirmed arrival.</summary>
    internal GridPoint Goal(GridPoint currentCell)
    {
        return mode switch
        {
            EnemyBrainMode.Patrol => PatrolGoal(currentCell),
            EnemyBrainMode.Pursue => lastKnownTarget!.Value,
            EnemyBrainMode.Search => SearchGoal(currentCell),
            EnemyBrainMode.Return => ReturnGoal(currentCell),
            _ => throw new InvalidOperationException("Unknown enemy brain mode."),
        };
    }

    /// <summary>Records a failed path attempt so the caller can defer another Engine query.</summary>
    internal void ReportPathUnavailable() => pathRetryRemaining = definition.PathRetrySeconds;

    internal bool InPreferredRange(float distance)
    {
        ValidateDistance(distance);
        return distance >= definition.PreferredMinimumRange && distance <= definition.PreferredMaximumRange;
    }

    internal bool NeedsRetreat(float distance)
    {
        ValidateDistance(distance);
        return mode == EnemyBrainMode.Pursue && RetreatReady && distance < definition.PreferredMinimumRange;
    }

    internal bool NeedsAdvance(float distance)
    {
        ValidateDistance(distance);
        return mode == EnemyBrainMode.Pursue && distance > definition.PreferredMaximumRange;
    }

    /// <summary>Consumes one retreat step. Repeated visible observations cannot replenish this engagement budget.</summary>
    internal bool TrySpendRetreatStep()
    {
        if (mode != EnemyBrainMode.Pursue || !RetreatReady) return false;
        retreatBudgetRemaining--;
        retreatCooldownRemaining = definition.RetreatCooldownSeconds;
        return true;
    }

    private GridPoint PatrolGoal(GridPoint currentCell)
    {
        if (currentCell == patrolRoute[patrolIndex]) patrolIndex = (patrolIndex + 1) % patrolRoute.Length;
        return patrolRoute[patrolIndex];
    }

    private GridPoint SearchGoal(GridPoint currentCell)
    {
        GridPoint target = lastKnownTarget!.Value;
        if (currentCell != target) return target;

        searchingAtLastKnown = true;
        if (searchRemaining > 0) return target;
        EnterReturn(EnemyBrainReason.SearchExhausted);
        return Home;
    }

    private GridPoint ReturnGoal(GridPoint currentCell)
    {
        if (currentCell != Home) return Home;
        BeginPatrol(EnemyBrainReason.ReturnedHome);
        return PatrolGoal(currentCell);
    }

    private void EnterPursue(GridPoint target, bool newEncounter)
    {
        lastKnownTarget = target;
        memoryRemaining = definition.MemorySeconds;
        searchRemaining = 0;
        searchingAtLastKnown = false;
        mode = EnemyBrainMode.Pursue;
        reason = EnemyBrainReason.VisibleTarget;
        if (newEncounter)
        {
            retreatBudgetRemaining = definition.MaxRetreatSteps;
            retreatCooldownRemaining = 0;
        }
    }

    private void EnterSearch(EnemyBrainReason transitionReason)
    {
        mode = EnemyBrainMode.Search;
        reason = transitionReason;
        searchRemaining = definition.SearchSeconds;
        searchingAtLastKnown = false;
    }

    private void EnterReturn(EnemyBrainReason transitionReason)
    {
        mode = EnemyBrainMode.Return;
        lastKnownTarget = null;
        memoryRemaining = 0;
        searchRemaining = 0;
        searchingAtLastKnown = false;
        reason = transitionReason;
    }

    private void BeginPatrol(EnemyBrainReason transitionReason)
    {
        mode = EnemyBrainMode.Patrol;
        lastKnownTarget = null;
        memoryRemaining = 0;
        searchRemaining = 0;
        searchingAtLastKnown = false;
        reason = transitionReason;
        retreatBudgetRemaining = definition.MaxRetreatSteps;
        retreatCooldownRemaining = 0;
    }

    private static void ValidateRoute(GridPoint[]? route)
    {
        if (route is not { Length: > 0 }) throw new InvalidDataException("Enemy patrol route must contain a cell.");
    }

    private static bool ValidDuration(double value, double maximum) => double.IsFinite(value) && value >= 0 && value <= maximum;

    private static void ValidateModeState(EnemyBrainDefinition definition, EnemyBrainSnapshot saved)
    {
        bool attentive = saved.Mode is EnemyBrainMode.Pursue or EnemyBrainMode.Search;
        if (attentive != saved.LastKnownTarget.HasValue || attentive && saved.MemoryRemaining == 0
            || !attentive && saved.MemoryRemaining != 0
            || saved.Mode != EnemyBrainMode.Search && (saved.SearchRemaining != 0 || saved.SearchingAtLastKnown)
            || saved.Mode == EnemyBrainMode.Search && (saved.SearchRemaining == 0
                || !saved.SearchingAtLastKnown && saved.SearchRemaining != definition.SearchSeconds))
            throw new InvalidDataException("Invalid saved enemy brain state.");
    }

    private static double SpendTime(double remaining, double seconds) => Math.Max(0, remaining - seconds);

    private static void ValidateDistance(float distance)
    {
        if (!float.IsFinite(distance) || distance < 0) throw new ArgumentOutOfRangeException(nameof(distance));
    }
}
