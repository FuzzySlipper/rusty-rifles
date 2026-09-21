using System.Numerics;
using Rifles.Game.Characters;
using Rifles.Game.Dungeon;
using Rifles.Game.Items;
using Rifles.Game.Party;
using Rifles.Game.Presentation;
using Rifles.Procgen.Generation;
using Rusty.Engine;

namespace Rifles.Game.Combat;

/// <summary>Charge contact needs both authored reach and an unobstructed first hit.</summary>
internal static class ChargeContactRules
{
    internal static bool CanContact(FormationDefinition formation, MartialWeaponReachDefinition weapon,
        FormationCellDefinition attacker, FormationTarget target) => target.Exposed
            && FormationRules.CanReach(formation, weapon, attacker, target);
}

internal sealed partial class RiflesCombat
{
    private sealed record ChargeParticipant(RiflesCharacter Member, CarriedItem Weapon, WeaponCapabilities Capabilities);
    private sealed record ChargePlan(EnemyState Target, GridPoint Stop, ChargeParticipant[] Contributors);
    private sealed record ChargeEvaluation(ChargeReadout Readout, ChargePlan? Plan);

    internal ChargeReadout ReadCharge()
    {
        if (charge.Capture() is { } active) return ReadActiveCharge(active);
        return EvaluateCharge().Readout;
    }

    private IReadOnlyList<MemberOrderReadout> ChargeLockedReadout() => party.Members
        .Where(member => !member.Definition.Commander)
        .Select(member => new MemberOrderReadout(member.Definition.Id, false, "Charging.", "", ""))
        .ToArray();

    internal GameOutcome BeginCharge(bool paused)
    {
        if (paused || Defeated) return GameOutcome.Reject(paused ? "Resume before charging." : "The commander has fallen.");
        if (ChargeExecuting) return GameOutcome.Reject("The party is already charging.");
        if (party.Formation.Executing) return GameOutcome.Reject("Finish repositioning before charging.");
        if (scope.Exploration.Moving) return GameOutcome.Reject("Finish the current movement before charging.");

        ChargeEvaluation evaluation = EvaluateCharge();
        if (evaluation.Plan is null) return GameOutcome.Reject(evaluation.Readout.Reason);
        ChargePlan plan = evaluation.Plan;
        if (!scope.Exploration.Act(ExplorationAction.Forward)) return GameOutcome.Reject("The forward charge lane closed before commitment.");

        ChargeSnapshot snapshot = new(plan.Target.Id, scope.Exploration.Facing, plan.Stop,
            scope.Exploration.Position.ManhattanDistance(plan.Stop), 0,
            plan.Contributors.Select(participant => new ChargeContributorSnapshot(participant.Member.Definition.Id,
                participant.Weapon.Entity, participant.Capabilities.RecoverySeconds)).ToArray());
        charge.Start(snapshot);
        foreach (ChargeParticipant participant in plan.Contributors)
            InterruptReloadForManeuver(participant.Member);
        scope.CancelRest("Rest interrupted by a charge.");
        CombatMessage("Charge committed: " + plan.Contributors.Length + " soldiers toward " + plan.Target.Definition.Name + ".");
        return GameOutcome.Accept();
    }

    /// <summary>
    /// Advances the one admitted forward reservation. Each next cell is
    /// reserved through ExplorationState after the preceding actual commit;
    /// a failed commit or reservation leaves the party at its last legal cell.
    /// </summary>
    internal void AdvanceCharge(double seconds, double partySpeed)
    {
        if (!ChargeExecuting) return;
        ChargeSnapshot active = charge.Current;
        GridPoint expected = scope.Exploration.Position + active.Facing.Offset();
        if (!scope.Exploration.Moving)
        {
            FinishCharge(active, contact: false, "Charge stopped before its next step.");
            return;
        }

        scope.Exploration.AdvanceManeuver(seconds, definitions.Charge.StepSeconds, partySpeed);
        if (scope.Exploration.Moving) return;
        if (scope.Exploration.Position != expected)
        {
            FinishCharge(active, contact: false, "Charge stopped by a changing obstruction.");
            return;
        }

        charge.AdvanceStep();
        active = charge.Current;
        if (active.CompletedSteps == active.PlannedSteps)
        {
            FinishCharge(active, contact: true, "Charge reached its planned contact.");
            return;
        }
        if (!scope.Exploration.Act(ExplorationAction.Forward))
            FinishCharge(active, contact: false, "Charge stopped by a changing obstruction.");
    }

    private ChargeEvaluation EvaluateCharge()
    {
        List<MemberOrderReadout> readout = [];
        List<ChargeParticipant> eligible = [];
        foreach (RiflesCharacter soldier in party.Members.Where(member => !member.Definition.Commander))
        {
            string? reason = ChargeReadiness(soldier, out ChargeParticipant? participant);
            if (reason is null) eligible.Add(participant!);
            readout.Add(new MemberOrderReadout(soldier.Definition.Id, reason is null, reason ?? "Ready.", "", ""));
        }
        if (eligible.Count == 0)
            return new(new(false, 0, readout.Count, "No living soldier has a ready charge weapon.", 0, 0, 0, readout), null);
        if (scope.Exploration.Moving)
            return UnavailableCharge(readout, "Finish the current movement before charging.");
        if (party.Formation.Executing)
            return UnavailableCharge(readout, "Finish repositioning before charging.");
        if (party.Members.Any(member =>
        {
            ActionState state = ActionOf(member);
            return state.Busy && !MusketActionRules.CanInterruptReload(state.Current);
        }))
            return UnavailableCharge(readout, "Finish current actions before charging.");

        GridPoint[] route = scope.Movement.ProbeStraight(scope.PartyId, scope.Exploration.Facing, definitions.Charge.MaximumCells);
        if (route.Length == 0) return UnavailableCharge(readout, "No legal straight forward charge step.");
        EnemyState[] targets = enemies.Where(enemy => enemy.Alive).OrderBy(enemy => enemy.Id).ToArray();
        foreach (GridPoint stop in route.Reverse())
            foreach (EnemyState target in targets)
                if (eligible.Any(participant => CanContact(participant.Member, participant.Capabilities, stop, scope.Exploration.Facing, target)))
                {
                    int steps = scope.Exploration.Position.ManhattanDistance(stop);
                    ChargeReadout ready = new(false, eligible.Count, readout.Count, "Ready.", 0, steps, 0, readout);
                    return new(ready, new ChargePlan(target, stop, eligible.ToArray()));
                }
        return UnavailableCharge(readout, "No forward target is reachable at a legal charge stop.");
    }

    private ChargeEvaluation UnavailableCharge(IReadOnlyList<MemberOrderReadout> readout, string reason) => new(
        new(false, 0, readout.Count, reason, 0, 0, 0,
            readout.Select(member => member.Eligible ? member with { Eligible = false, Reason = reason } : member).ToArray()), null);

    private string? ChargeReadiness(RiflesCharacter soldier, out ChargeParticipant? participant)
    {
        participant = null;
        if (!soldier.IsLiving) return "Fallen.";
        if (party.Formation.Affects(soldier.InstanceId)) return "Repositioning.";
        ActionState state = ActionOf(soldier);
        if (state.Busy && !MusketActionRules.CanInterruptReload(state.Current)) return "Busy.";
        CarriedItem? weapon = Weapon(soldier.Definition.Id);
        WeaponCapabilities? capabilities = Capabilities(weapon);
        if (weapon is null || capabilities is not { ChargeEligible: true, MeleeDamage: > 0, MeleeReach: not null }) return "No charge-capable weapon.";
        participant = new(soldier, weapon, capabilities);
        return null;
    }

    private ChargeReadout ReadActiveCharge(ChargeSnapshot active)
    {
        HashSet<string> contributorIds = active.Contributors.Select(contributor => contributor.Member).ToHashSet(StringComparer.Ordinal);
        MemberOrderReadout[] contributors = party.Members.Where(member => !member.Definition.Commander)
            .Select(member => contributorIds.Contains(member.Definition.Id)
                ? new MemberOrderReadout(member.Definition.Id, true, "Committed.", active.Target.ToString(System.Globalization.CultureInfo.InvariantCulture), "Forward")
                : new MemberOrderReadout(member.Definition.Id, false, "Not contributing.", "", ""))
            .ToArray();
        double remaining = scope.Exploration.RecoverySeconds * definitions.Charge.StepSeconds / definitions.Exploration.StepSeconds;
        return new(true, active.Contributors.Length, contributors.Length, "Charging.", active.CompletedSteps,
            active.PlannedSteps, remaining, contributors);
    }

    private void FinishCharge(ChargeSnapshot active, bool contact, string outcome)
    {
        ChargeSnapshot completed = charge.Clear(); // Clear durably before contact effects can mutate combat state.
        // The first reservation is the commitment point. A dynamic obstruction
        // may prevent its commit, but the charge has already displaced the party.
        ApplyChargeRecovery(completed);
        if (contact) ResolveChargeContact(completed);
        CombatMessage(outcome);
    }

    private void ApplyChargeRecovery(ChargeSnapshot active)
    {
        foreach (ChargeContributorSnapshot contributor in active.Contributors)
        {
            RiflesCharacter? member = party.Members.SingleOrDefault(member => member.Definition.Id == contributor.Member);
            if (member is null || !member.IsLiving) continue;
            ActionState state = ActionOf(member);
            if (state.Busy) continue;
            double recovery = definitions.Charge.RecoverySeconds + contributor.WeaponRecoverySeconds;
            state.StartRecovery(new ActionSnapshot(CombatActionKind.Charge, contributor.Weapon, null, null, 0, null,
                recovery, ActionPhase.Recovery, recovery));
        }
    }

    private void ResolveChargeContact(ChargeSnapshot active)
    {
        EnemyState? target = enemies.SingleOrDefault(enemy => enemy.Id == active.Target && enemy.Alive);
        if (target is null) { CombatMessage("Charge target departed before contact."); return; }
        foreach (ChargeContributorSnapshot contributor in active.Contributors)
        {
            RiflesCharacter? member = party.Members.SingleOrDefault(member => member.Definition.Id == contributor.Member);
            if (member is null || !member.IsLiving) continue;
            CarriedItem? carried = Weapon(member.Definition.Id);
            if (carried?.Entity != contributor.Weapon) continue;
            WeaponCapabilities capabilities = Capabilities(carried)!;
            if (!capabilities.ChargeEligible || capabilities.MeleeDamage <= 0 || capabilities.MeleeReach is null) continue;
            if (!CanContact(member, capabilities, scope.Exploration.Position, active.Facing, target)) continue;
            Vector3 start = Aim(scope.Exploration.Position), end = EnemyAim(target);
            SpatialHit hit = TraceOrderHit(start, end);
            if (hit.Present && hit.Kind == SpatialHitKind.Entity && hit.Entity == target.Id)
                ResolveHit(hit, CombatActionKind.Melee, member.Definition.Id, scope.PartyId, end - start,
                    capabilities.MeleeDamage + definitions.Charge.BonusDamage);
        }
    }

    private bool CanContact(RiflesCharacter member, WeaponCapabilities weapon, GridPoint origin, CardinalDirection facing, EnemyState target)
    {
        if (weapon.MeleeReach is null) return false;
        Vector3 start = Aim(origin), end = EnemyAim(target), offset = end - start;
        GridPoint forwardAxis = facing.Offset(), leftAxis = facing.Rotate(-1).Offset();
        float forward = offset.X * forwardAxis.X + offset.Z * forwardAxis.Y;
        float left = offset.X * leftAxis.X + offset.Z * leftAxis.Y;
        SpatialHit hit = TraceOrderHit(start, end);
        FormationTarget candidate = new(target.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), forward, left,
            hit.Present && hit.Kind == SpatialHitKind.Entity && hit.Entity == target.Id);
        return ChargeContactRules.CanContact(definitions.Formation, definitions.Formation.Weapon(weapon.MeleeReach),
            definitions.Formation.Cell(member.Position), candidate);
    }
}
