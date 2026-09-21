using System.Numerics;
using Rifles.Game.Characters;
using Rifles.Game.Magic;
using Rifles.Game.Party;
using Rifles.Game.Presentation;
using Rifles.Procgen.Generation;
using Rusty.Engine;

namespace Rifles.Game.Combat;

/// <summary>One provider's live contribution to a grouped ability order.</summary>
internal sealed record AbilityProviderReadout(string Member, bool Eligible, string Reason, string Target, string Lane);

/// <summary>A stable spell-id row in the party ability menu.</summary>
internal sealed record AbilityOrderReadout(
    string Id,
    string Name,
    SpellEffect Effect,
    SpellTarget Target,
    bool Shared,
    int Eligible,
    int Total,
    IReadOnlyList<AbilityProviderReadout> Providers);

internal sealed record ForwardAbilityCandidate(ulong Target, float ForwardDistance, float LeftOffset, bool Exposed);

/// <summary>
/// Pure forward preference for hostile spells. It intentionally shares the
/// order lane/fallback concepts while spell range and cone stay authored on
/// the spell rather than pretending a spell is a weapon.
/// </summary>
internal static class ForwardAbilityRules
{
    internal static ulong? Select(FormationDefinition formation, string attackerCell,
        IEnumerable<ForwardAbilityCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(formation);
        ArgumentNullException.ThrowIfNull(candidates);
        FormationCellDefinition attacker = formation.Cell(attackerCell);
        return candidates.Where(candidate => candidate.Exposed)
            .Where(candidate => Math.Abs(attacker.Left - (int)FormationRules.DeriveOffensiveLane(candidate.LeftOffset, formation.OffensiveLaneWidth))
                <= formation.MaximumLaneFallback)
            .OrderBy(candidate => Math.Abs(attacker.Left - (int)FormationRules.DeriveOffensiveLane(candidate.LeftOffset, formation.OffensiveLaneWidth)))
            .ThenBy(candidate => candidate.ForwardDistance * candidate.ForwardDistance + candidate.LeftOffset * candidate.LeftOffset)
            .ThenBy(candidate => candidate.Target)
            .Select(candidate => (ulong?)candidate.Target)
            .FirstOrDefault();
    }

    internal static bool InOriginalForwardArea(SpellDefinition spell, float forward, float left)
    {
        if (!float.IsFinite(forward) || !float.IsFinite(left)) return false;
        float lateralLimit = forward * MathF.Tan(spell.ForwardHalfAngleDegrees * MathF.PI / 180f);
        return forward > 0 && forward <= spell.Range && MathF.Abs(left) <= lateralLimit;
    }
}

internal static class AbilityOrderRules
{
    /// <summary>The designated party-effect settler is stable across menu refreshes and saves.</summary>
    internal static string? SharedEffectOwner(IEnumerable<AbilityProviderReadout> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        return providers.Where(provider => provider.Eligible)
            .OrderBy(provider => provider.Member, StringComparer.Ordinal)
            .Select(provider => provider.Member)
            .FirstOrDefault();
    }
}

internal sealed partial class RiflesCombat
{
    private sealed record AbilityTarget(EnemyState Enemy, float ForwardDistance, float LeftOffset, OffensiveLane Lane);

    internal IReadOnlyList<AbilityOrderReadout> ReadAbilities(string targetMember)
    {
        return definitions.Magic.Spells
            .Where(spell => party.Members.Any(member => !member.Definition.Commander
                && magic.HasBook(member.Definition.Id) && magic.For(member.Definition.Id).Known.Contains(spell.Id)))
            .OrderBy(spell => spell.Id, StringComparer.Ordinal)
            .Select(spell => EvaluateAbility(spell, targetMember, start: false).Readout)
            .ToArray();
    }

    /// <summary>
    /// Starts all ready known providers immediately. Party effects deliberately
    /// retain every provider's cost and recovery, while only the stable first
    /// provider settles the one shared condition at impact.
    /// </summary>
    internal GameOutcome BeginAbilityOrder(string spellId, string targetMember, bool paused)
    {
        SpellDefinition spell;
        try { spell = definitions.Magic.Spell(spellId); }
        catch (InvalidOperationException) { return GameOutcome.Reject("Unknown ability."); }
        if (paused || Defeated) return GameOutcome.Reject(paused ? "Resume before ordering an ability." : "The commander has fallen.");

        AbilityEvaluation evaluation = EvaluateAbility(spell, targetMember, start: true);
        if (evaluation.Started == 0)
            return GameOutcome.Reject(spell.Name + " found no ready provider. " + evaluation.FirstReason);

        scope.CancelRest("Rest interrupted by an ability order.");
        CombatMessage(spell.Name + ": " + evaluation.Started + "/" + evaluation.Readout.Total + " providers started.");
        return GameOutcome.Accept();
    }

    private AbilityEvaluation EvaluateAbility(SpellDefinition spell, string targetMember, bool start)
    {
        GridPoint origin = scope.Exploration.Position;
        CardinalDirection facing = scope.Exploration.Facing;
        (ulong Target, ulong Revision) target = AbilityTargetFor(spell);
        List<AbilityProviderReadout> providers = [];
        List<AbilityPlan> ready = [];

        foreach (RiflesCharacter member in party.Members.Where(member => !member.Definition.Commander)
            .OrderBy(member => member.Definition.Id, StringComparer.Ordinal))
        {
            if (!magic.HasBook(member.Definition.Id) || !magic.For(member.Definition.Id).Known.Contains(spell.Id)) continue;
            AbilityPlan plan = AssessAbility(member, spell, targetMember, target.Target, target.Revision, origin, facing);
            providers.Add(plan.Readout);
            if (plan.Readout.Eligible) ready.Add(plan);
        }

        bool shared = spell.Target == SpellTarget.Party;
        string? sharedOwner = shared ? AbilityOrderRules.SharedEffectOwner(providers) : null;
        int started = 0;
        if (start)
        {
            foreach (AbilityPlan plan in ready)
            {
                bool suppressSharedEffect = shared && plan.Member.Definition.Id != sharedOwner;
                StartAbility(plan, spell, targetMember, target.Target, target.Revision, origin, facing, suppressSharedEffect);
                started++;
            }
        }

        AbilityOrderReadout readout = new(spell.Id, spell.Name, spell.Effect, spell.Target, shared, ready.Count, providers.Count, providers);
        string firstReason = providers.FirstOrDefault(provider => !provider.Eligible)?.Reason ?? "No known soldier can provide it.";
        return new AbilityEvaluation(readout, started, firstReason);
    }

    private AbilityPlan AssessAbility(RiflesCharacter member, SpellDefinition spell, string targetMember, ulong target, ulong revision,
        GridPoint origin, CardinalDirection facing)
    {
        if (spell.Target == SpellTarget.Enemy)
        {
            if (BasicSpellAvailability(member.Definition.Id, spell, targetMember, target, revision) is { } basic)
                return AbilityPlan.Reject(member, basic);
            AbilityTarget? selected = SelectAbilityTarget(member, spell, origin, facing);
            return selected is null
                ? AbilityPlan.Reject(member, "No exposed hostile in the forward casting area.")
                : AbilityPlan.Ready(member, selected);
        }

        string? reason = BasicSpellAvailability(member.Definition.Id, spell, targetMember, target, revision);
        return reason is null ? AbilityPlan.Ready(member, null) : AbilityPlan.Reject(member, reason);
    }

    private void StartAbility(AbilityPlan plan, SpellDefinition spell, string targetMember, ulong abilityTarget, ulong featureRevision,
        GridPoint origin, CardinalDirection facing, bool suppressSharedEffect)
    {
        AbilityTarget? target = plan.Target;
        ActionOf(plan.Member).Start(new ActionSnapshot(CombatActionKind.Cast, 0, null, null, target?.Enemy.Id ?? abilityTarget,
            spell.Target == SpellTarget.Ally ? targetMember : null, spell.Windup, ActionPhase.Windup, spell.Recovery,
            target?.Enemy.Motion.Position, target?.Enemy.Motion.CrowdOffset.X ?? 0, target?.Enemy.Motion.CrowdOffset.Y ?? 0,
            spell.Id, SpellCost(plan.Member.Definition.Id, spell), featureRevision,
            spell.Target == SpellTarget.Enemy ? origin : null, spell.Target == SpellTarget.Enemy ? facing : null,
            suppressSharedEffect));
    }

    private (ulong Target, ulong Revision) AbilityTargetFor(SpellDefinition spell)
    {
        if (spell.Target != SpellTarget.Feature) return (0, 0);
        var focused = scope.Features().Readout?.Selected;
        ulong target = focused is { } selected && (scope.GeneratedFeatures.Gates.Any(gate => gate.Id == selected.Id)
            || scope.GeneratedFeatures.Hazards.Any(hazard => hazard.Id == selected.Id)) ? focused.Value.Id : scope.ItemWorld.LeverId;
        return (target, target == scope.ItemWorld.LeverId ? scope.ItemWorld.Revision : scope.GeneratedFeatures.Revision);
    }

    private AbilityTarget? ResolveAbilityTarget(RiflesCharacter member, SpellDefinition spell, ActionSnapshot action, ulong? only = null)
    {
        if (action.OrderOrigin is not { } origin || action.OrderFacing is not { } facing) return null;
        return SelectAbilityTarget(member, spell, origin, facing, only)
            ?? (only is null ? null : SelectAbilityTarget(member, spell, origin, facing));
    }

    private AbilityTarget? SelectAbilityTarget(RiflesCharacter member, SpellDefinition spell, GridPoint origin, CardinalDirection facing,
        ulong? only = null)
    {
        AbilityTarget[] candidates = AbilityTargets(spell, origin, facing)
            .Where(candidate => only is null || candidate.Enemy.Id == only.Value)
            .ToArray();
        ulong? selected = ForwardAbilityRules.Select(definitions.Formation, member.Position,
            candidates.Select(candidate => new ForwardAbilityCandidate(candidate.Enemy.Id, candidate.ForwardDistance, candidate.LeftOffset, true)));
        return selected is null ? null : candidates.Single(candidate => candidate.Enemy.Id == selected.Value);
    }

    private IEnumerable<AbilityTarget> AbilityTargets(SpellDefinition spell, GridPoint origin, CardinalDirection facing)
    {
        Vector3 originalStart = Aim(origin);
        Vector3 currentStart = Aim(scope.Exploration.Position);
        GridPoint forwardAxis = facing.Offset();
        GridPoint leftAxis = facing.Rotate(-1).Offset();
        foreach (EnemyState enemy in enemies.Where(enemy => enemy.Alive))
        {
            Vector3 end = EnemyAim(enemy);
            Vector3 originalOffset = end - originalStart;
            float originalForward = originalOffset.X * forwardAxis.X + originalOffset.Z * forwardAxis.Y;
            float originalLeft = originalOffset.X * leftAxis.X + originalOffset.Z * leftAxis.Y;
            Vector3 currentOffset = end - currentStart;
            float currentForward = currentOffset.X * forwardAxis.X + currentOffset.Z * forwardAxis.Y;
            float currentLeft = currentOffset.X * leftAxis.X + currentOffset.Z * leftAxis.Y;
            SpatialHit hit = scope.Scene.Trace(currentStart, end, OrderBodies(), scope.PartyId);
            bool exposed = ForwardAbilityRules.InOriginalForwardArea(spell, originalForward, originalLeft)
                && ForwardAbilityRules.InOriginalForwardArea(spell, currentForward, currentLeft)
                && currentOffset.Length() <= spell.Range && hit.Present && hit.Kind == SpatialHitKind.Entity && hit.Entity == enemy.Id;
            if (exposed)
                yield return new AbilityTarget(enemy, currentForward, currentLeft,
                    FormationRules.DeriveOffensiveLane(currentLeft, definitions.Formation.OffensiveLaneWidth));
        }
    }

    private sealed record AbilityPlan(RiflesCharacter Member, AbilityTarget? Target, AbilityProviderReadout Readout)
    {
        internal static AbilityPlan Ready(RiflesCharacter member, AbilityTarget? target) => new(member, target,
            new AbilityProviderReadout(member.Definition.Id, true, "Ready.", target?.Enemy.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "", target?.Lane.ToString() ?? ""));
        internal static AbilityPlan Reject(RiflesCharacter member, string reason) => new(member, null,
            new AbilityProviderReadout(member.Definition.Id, false, reason, "", ""));
    }

    private sealed record AbilityEvaluation(AbilityOrderReadout Readout, int Started, string FirstReason);
}
