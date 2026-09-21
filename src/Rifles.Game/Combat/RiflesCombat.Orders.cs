using System.Numerics;
using Rifles.Game.Characters;
using Rifles.Game.Items;
using Rifles.Game.Party;
using Rifles.Game.Presentation;
using Rifles.Procgen.Generation;
using Rusty.Engine;

namespace Rifles.Game.Combat;

/// <summary>Live per-soldier readiness for a forward order.</summary>
internal sealed record MemberOrderReadout(string Member, bool Eligible, string Reason, string Target, string Lane);
internal sealed record ForwardOrderCandidate(ulong Target, float ForwardDistance, float LeftOffset, bool Exposed);
internal sealed record ForwardOrderSelection(ulong Target, OffensiveLane Lane);
internal sealed record ForwardOrderAvailability(bool Eligible, string Reason, ForwardOrderSelection? Selection);

/// <summary>Concrete, pure target choice for one soldier's forward order.</summary>
internal static class ForwardOrderRules
{
    internal static ForwardOrderSelection? Select(FormationDefinition formation, string reach, string attackerCell,
        IEnumerable<ForwardOrderCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(formation); ArgumentNullException.ThrowIfNull(candidates);
        ForwardOrderCandidate[] all = candidates.ToArray();
        string? target = FormationRules.SelectPreferredTarget(formation, reach, attackerCell,
            all.Select(candidate => new FormationTarget(candidate.Target.ToString(System.Globalization.CultureInfo.InvariantCulture),
                candidate.ForwardDistance, candidate.LeftOffset, candidate.Exposed)));
        if (target is null) return null;
        ForwardOrderCandidate selected = all.Single(candidate => candidate.Target.ToString(System.Globalization.CultureInfo.InvariantCulture) == target);
        return new ForwardOrderSelection(selected.Target, FormationRules.DeriveOffensiveLane(selected.LeftOffset, formation.OffensiveLaneWidth));
    }

    internal static ForwardOrderAvailability Assess(FormationDefinition formation, string attackerCell, bool living, bool busy,
        bool repositioning, bool hasWeapon, string? reach, bool fireOrder, bool loaded, IEnumerable<ForwardOrderCandidate> candidates)
    {
        if (!living) return new(false, "Fallen.", null);
        if (busy) return new(false, "Busy.", null);
        if (repositioning) return new(false, "Repositioning.", null);
        if (!hasWeapon) return new(false, "No weapon.", null);
        if (reach is null) return new(false, "Weapon cannot perform this order.", null);
        if (fireOrder && !loaded) return new(false, "Dry musket.", null);
        ForwardOrderSelection? selection = Select(formation, reach, attackerCell, candidates);
        return selection is null ? new(false, "No exposed target in reach.", null) : new(true, "Ready.", selection);
    }

    internal static bool InOriginalForwardArea(FormationDefinition formation, string reach, float forward, float left)
    {
        MartialWeaponReachDefinition weapon = formation.Weapon(reach);
        return float.IsFinite(forward) && float.IsFinite(left) && forward > 0 && forward <= weapon.MaximumForwardDistance
            && MathF.Abs(left) <= forward * MathF.Tan(weapon.AimHalfAngleDegrees * MathF.PI / 180f);
    }
}

internal sealed partial class RiflesCombat
{
    private sealed record OrderTarget(EnemyState Enemy, FormationTarget Target, OffensiveLane Lane);

    internal IReadOnlyList<MemberOrderReadout> ReadOrder(CombatActionKind kind)
    {
        if (kind is not (CombatActionKind.Fire or CombatActionKind.Melee)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (ChargeExecuting) return ChargeLockedReadout();
        return EvaluateOrder(kind, false).Readout;
    }

    /// <summary>
    /// Starts every immediately eligible soldier's independent action. A failed
    /// participant is recorded but never queued for a later surprise attack.
    /// </summary>
    internal GameOutcome BeginOrder(CombatActionKind kind, bool paused)
    {
        if (kind is not (CombatActionKind.Fire or CombatActionKind.Melee)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (ChargeExecuting) return GameOutcome.Reject("The party is charging.");
        IReadOnlyList<RiflesCharacter> soldiers = party.Members.Where(member => !member.Definition.Commander).ToArray();
        if (paused || Defeated)
        {
            return GameOutcome.Reject(paused ? "Resume before ordering." : "The commander has fallen.");
        }
        (MemberOrderReadout[] Readout, int Started) evaluation = EvaluateOrder(kind, true);
        if (evaluation.Started == 0) return GameOutcome.Reject(kind + " order found no ready soldier with an exposed target.");
        scope.CancelRest("Rest interrupted by an order.");
        CombatMessage(kind + " order: " + evaluation.Started + "/" + soldiers.Count + " soldiers started.");
        return GameOutcome.Accept();
    }

    private (MemberOrderReadout[] Readout, int Started) EvaluateOrder(CombatActionKind kind, bool start)
    {
        GridPoint origin = scope.Exploration.Position;
        CardinalDirection facing = scope.Exploration.Facing;
        List<MemberOrderReadout> readout = [];
        int started = 0;
        Dictionary<string, OrderTarget[]> targetsByReach = new(StringComparer.Ordinal);
        foreach (RiflesCharacter soldier in party.Members.Where(member => !member.Definition.Commander))
        {
            ActionState state = ActionOf(soldier);
            CarriedItem? carried = Weapon(soldier.Definition.Id);
            WeaponCapabilities? weapon = Capabilities(carried);
            string? reach = kind == CombatActionKind.Fire ? weapon?.FireReach : weapon?.MeleeReach;
            bool cancelReload = kind == CombatActionKind.Melee && MusketActionRules.CanInterruptReload(state.Current);
            OrderTarget[] candidates = [];
            if (reach is not null && soldier.IsLiving && (!state.Busy || cancelReload) && !party.Formation.Affects(soldier.InstanceId)
                && (kind != CombatActionKind.Fire || weapon!.Loaded))
            {
                if (!targetsByReach.TryGetValue(reach, out var cached))
                    targetsByReach[reach] = cached = OrderTargets(origin, facing, reach).ToArray();
                candidates = cached;
            }
            ForwardOrderAvailability availability = ForwardOrderRules.Assess(definitions.Formation, soldier.Position, soldier.IsLiving,
                state.Busy && !cancelReload, party.Formation.Affects(soldier.InstanceId), weapon is not null, reach, kind == CombatActionKind.Fire, weapon?.Loaded ?? false, candidates.Select(target => new ForwardOrderCandidate(
                    target.Enemy.Id, target.Target.ForwardDistance, target.Target.LeftOffset, target.Target.Exposed)));
            if (!availability.Eligible) { readout.Add(new(soldier.Definition.Id, false, availability.Reason, "", "")); continue; }
            OrderTarget target = candidates.Single(candidate => candidate.Enemy.Id == availability.Selection!.Target);
            if (!start) { readout.Add(new(soldier.Definition.Id, true, availability.Reason,
                target.Enemy.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), target.Lane.ToString())); continue; }
            if (cancelReload) InterruptReloadForManeuver(soldier);
            ActionSnapshot action = NewAction(kind, carried!.Entity, target: target.Enemy.Id, aim: target.Enemy.Motion.Position,
                capabilities: weapon, orderOrigin: origin, orderFacing: facing) with
            { AimOffsetX = target.Enemy.Motion.CrowdOffset.X, AimOffsetY = target.Enemy.Motion.CrowdOffset.Y };
            state.Start(action);
            started++;
            readout.Add(new(soldier.Definition.Id, true, "Started.", target.Enemy.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), target.Lane.ToString()));
        }
        return (readout.ToArray(), started);
    }

    /// <summary>Rechecks only from the origin pose retained by the committed order.</summary>
    private OrderTarget? ResolveOrderTarget(RiflesCharacter soldier, ActionSnapshot action, WeaponCapabilities weapon)
    {
        if (action.OrderOrigin is not { } origin || action.OrderFacing is not { } facing) return null;
        string? reach = action.Kind == CombatActionKind.Fire ? weapon.FireReach : weapon.MeleeReach;
        if (reach is null) return null;
        OrderTarget? current = SelectOrderTarget(soldier, reach, origin, facing, action.Target);
        return current ?? SelectOrderTarget(soldier, reach, origin, facing);
    }

    private OrderTarget? SelectOrderTarget(RiflesCharacter soldier, string reach, GridPoint origin, CardinalDirection facing, ulong? only = null)
    {
        OrderTarget[] candidates = OrderTargets(origin, facing, reach).Where(candidate => only is null || candidate.Enemy.Id == only).ToArray();
        if (candidates.Length == 0) return null;
        ForwardOrderSelection? selected = ForwardOrderRules.Select(definitions.Formation, reach, soldier.Position,
            candidates.Select(candidate => new ForwardOrderCandidate(candidate.Enemy.Id, candidate.Target.ForwardDistance,
                candidate.Target.LeftOffset, candidate.Target.Exposed)));
        return selected is null ? null : candidates.Single(candidate => candidate.Enemy.Id == selected.Target);
    }

    private IEnumerable<OrderTarget> OrderTargets(GridPoint origin, CardinalDirection facing, string reach)
    {
        Vector3 originalStart = Aim(origin);
        Vector3 currentStart = Aim(scope.Exploration.Position);
        SpatialEntityCollider[] hostiles = OrderBodies();
        GridPoint forwardAxis = facing.Offset();
        GridPoint leftAxis = facing.Rotate(-1).Offset();
        foreach (EnemyState enemy in enemies.Where(enemy => enemy.Alive))
        {
            Vector3 end = EnemyAim(enemy);
            Vector3 originalOffset = end - originalStart;
            float originalForward = originalOffset.X * forwardAxis.X + originalOffset.Z * forwardAxis.Y;
            float originalLeft = originalOffset.X * leftAxis.X + originalOffset.Z * leftAxis.Y;
            Vector3 currentOffset = end - currentStart;
            float forward = currentOffset.X * forwardAxis.X + currentOffset.Z * forwardAxis.Y;
            float left = currentOffset.X * leftAxis.X + currentOffset.Z * leftAxis.Y;
            SpatialHit hit = scope.Scene.Trace(currentStart, end, hostiles, scope.PartyId);
            bool exposed = ForwardOrderRules.InOriginalForwardArea(definitions.Formation, reach, originalForward, originalLeft)
                && hit.Present && hit.Kind == SpatialHitKind.Entity && hit.Entity == enemy.Id;
            FormationTarget target = new(enemy.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), forward, left, exposed);
            yield return new OrderTarget(enemy, target, FormationRules.DeriveOffensiveLane(left, definitions.Formation.OffensiveLaneWidth));
        }
    }

    private SpatialHit TraceOrderHit(Vector3 start, Vector3 end) => scope.Scene.Trace(start, end, OrderBodies(), scope.PartyId);

    private SpatialEntityCollider[] OrderBodies()
    {
        // Allies do not obstruct an order, but solid furniture still does.
        return CombatBodies().Where(body => body.Entity != scope.PartyId && !allies.ContainsKey(body.Entity)).ToArray();
    }
}
