using System.Numerics;
using Rifles.Game.Audio;
using Rifles.Game.Characters;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Items;
using Rifles.Game.Magic;
using Rifles.Game.Party;
using Rifles.Game.Presentation;
using Rifles.Procgen.Generation;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;

namespace Rifles.Game.Combat;

internal sealed partial class RiflesCombat
{
    private bool HasItem(string member, string item) => inventory.Items("member:" + member).Any(i => i.Definition == item && i.Quantity > 0);

    /// <summary>
    /// Planning-time spell eligibility shared by execution and projection.
    /// Translates this owner's own rejection channel into a reason; only
    /// InvalidDataException converts, programming failures still propagate.
    /// </summary>
    internal string? SpellAvailability(string memberId, SpellDefinition spell, string targetMember, ulong target, ulong featureRevision)
    {
        try
        {
            string? reason = BasicSpellAvailability(memberId, spell, targetMember, target, featureRevision);
            if (reason is not null) return reason;
            if (spell.Target == SpellTarget.Enemy
                && SelectAbilityTarget(Member(memberId), spell, scope.Exploration.Position, scope.Exploration.Facing) is null)
                return "No exposed hostile in the forward casting area.";
            return null;
        }
        catch (InvalidDataException error) { return error.Message; }
    }

    internal void ValidateSpell(string memberId, SpellDefinition spell, string targetMember, ulong target, ulong featureRevision, bool committing)
    {
        if (ChargeExecuting) throw new InvalidDataException("The party is charging.");
        if (BasicSpellAvailability(memberId, spell, targetMember, target, featureRevision, requireReady: false) is { } reason)
            throw new InvalidDataException(reason);
    }

    private string? BasicSpellAvailability(string memberId, SpellDefinition spell, string targetMember, ulong target, ulong featureRevision,
        bool requireReady = true)
    {
        if (ChargeExecuting) return "The party is charging.";
        RiflesCharacter member;
        try { member = Member(memberId); }
        catch (InvalidDataException) { return "That character cannot cast this spell."; }
        if (Defeated || member.Definition.Commander || !member.IsLiving || !magic.HasBook(memberId) || !magic.For(memberId).Known.Contains(spell.Id)) return "That character cannot cast this spell.";
        if (party.Formation.Affects(member.InstanceId)) return "That character is repositioning.";
        if (member.Resource < SpellCost(memberId, spell)) return "Insufficient resource.";
        if (requireReady && ActionOf(member).Busy) return "That character is still acting.";
        if (spell.Target == SpellTarget.Ally)
        {
            RiflesCharacter ally;
            try { ally = Member(targetMember); }
            catch (InvalidDataException) { return "Choose an ally."; }
            if (spell.Effect == SpellEffect.Revive)
            {
                if (ally.Definition.Commander || ally.IsLiving || magic.For(targetMember).Revivals >= definitions.Magic.MaximumRevivals || Threatened)
                    return "Revival needs a fallen ally with a revival remaining and no threats.";
                if (!HasItem(memberId, definitions.Magic.RevivalItem)) return "Caster needs " + definitions.Magic.RevivalItem + " for revival.";
            }
            else if (!ally.IsLiving) return "Choose a living ally.";
            if (spell.Effect == SpellEffect.Heal && ally.Vitality == ally.MaximumVitality) return "Ally needs no healing.";
        }
        if (spell.Target == SpellTarget.Feature)
        {
            bool generated = scope.GeneratedFeatures.Gates.Any(g => g.Id == target) || scope.GeneratedFeatures.Hazards.Any(h => h.Id == target);
            if (generated && scope.FeatureUseProblem(target) is { } problem) return problem;
            Vector3 point = generated ? scope.FeaturePoint(target) : scope.ItemWorld.LeverPoint(scope.Scene);
            ulong revision = generated ? scope.GeneratedFeatures.Revision : scope.ItemWorld.Revision;
            if (!definitions.Magic.AllowLeverMagic || featureRevision != revision
                || !scope.ItemWorld.Reachable(point, scope.Exploration, scope.Scene) || Vector3.Distance(Aim(scope.Exploration.Position), point) > spell.Range)
                return "No permitted mechanism within reach, or the feature changed.";
        }
        return null;
    }

    internal GameOutcome BeginSpell(string member, SpellDefinition spell, string targetMember)
    {
        return BeginAbilityOrder(spell.Id, targetMember, paused: false);
    }

    private bool TryEnemySpell(EnemyState enemy, float distance)
    {
        if (!definitions.Magic.EnemySpells.TryGetValue(enemy.Definition.Id, out string? id)) return false;
        SpellDefinition spell = definitions.Magic.Spell(id);
        if (enemy.Resource < spell.Cost || distance > spell.Range) return false;
        enemy.Action.Start(new(CombatActionKind.Cast, 0, null, null, scope.PartyId, null,
            spell.Windup, ActionPhase.Windup, spell.Recovery, scope.Exploration.Position, Spell: id, Cost: spell.Cost));
        return true;
    }

    private void CommitSpell(string? memberId, EnemyState? enemy, ActionSnapshot action)
    {
        SpellDefinition spell = definitions.Magic.Spell(action.Spell!);
        AbilityTarget? castTarget = null;
        if (memberId is not null)
        {
            if (spell.Target == SpellTarget.Enemy)
            {
                castTarget = ResolveAbilityTarget(Member(memberId), spell, action, action.Target);
                if (castTarget is null)
                {
                    CombatMessage(Member(memberId).Definition.Name + " found no replacement in the committed forward casting area.");
                    return;
                }
                action = action with { Target = castTarget.Enemy.Id, AimCell = castTarget.Enemy.Motion.Position,
                    AimOffsetX = castTarget.Enemy.Motion.CrowdOffset.X, AimOffsetY = castTarget.Enemy.Motion.CrowdOffset.Y };
            }
            ValidateSpell(memberId, spell, action.TargetMember!, action.Target, action.FeatureRevision, true);
            if (Member(memberId).Resource < action.Cost) throw new InvalidDataException("Insufficient resource at commit.");
            if (spell.Effect == SpellEffect.Revive) inventory.Consume(new MemberOwner(memberId), definitions.Magic.RevivalItem, 1);
            Member(memberId).SpendResource(action.Cost);
        }
        else
        {
            if (enemy is null || !enemy.Alive || enemy.Resource < action.Cost) return;
            enemy.SpendResource(action.Cost);
        }
        scope.Sound(SoundCue.Spell, enemy is null ? Aim(scope.Exploration.Position) : EnemyAim(enemy));
        if (spell.Target == SpellTarget.Enemy)
        {
            Vector3 source = enemy is null ? Aim(scope.Exploration.Position) : EnemyAim(enemy);
            Vector3 end = Aim(action.AimCell!.Value) + new Vector3(action.AimOffsetX, 0, action.AimOffsetY) * scope.Scene.LogicalCellSize;
            Vector3 offset = end - source;
            if (offset.LengthSquared() == 0) { CombatMessage("Spell dissipated at its origin."); return; }
            Vector3 direction = Vector3.Normalize(offset);
            flights.Add(new FlightState(scope.AllocateIds(), enemy?.Id ?? scope.PartyId, memberId, CombatActionKind.Cast,
                source.X, source.Y, source.Z, direction.X, direction.Y, direction.Z, Math.Min(offset.Length(), spell.Range),
                enemy?.Motion.Position ?? scope.Exploration.Position, null, null, spell.Id));
        }
        else if (spell.Effect == SpellEffect.Lever)
        {
            if (action.Target == scope.ItemWorld.LeverId) scope.ItemWorld.ToggleLever(scope.Exploration, scope.Scene, action.FeatureRevision);
            else CombatMessage(scope.UseFeature(action.Target, action.FeatureRevision));
        }
        else if (spell.Target == SpellTarget.Party)
        {
            if (action.SuppressSharedEffect)
                CombatMessage(Member(memberId!).Definition.Name + " supports " + spell.Name + ".");
            else magic.Apply(new PartyTarget(), spell);
        }
        else
        {
            RiflesCharacter target = Member(action.TargetMember!);
            MemberTarget key = new(target.Definition.Id);
            switch (spell.Effect)
            {
                case SpellEffect.Heal: target.Heal(spell.Power); break;
                case SpellEffect.Revive: target.Heal(spell.Power); magic.For(target.Definition.Id).Revivals++; break;
                case SpellEffect.Cleanse: magic.Clear(key, true); break;
                default: magic.Apply(key, spell); break;
            }
        }
        CombatMessage((memberId is null ? enemy!.Definition.Name : Member(memberId).Definition.Name) + " casts " + spell.Name + ".");
    }

    private void ResolveSpellImpact(FlightState flight, SpatialHit hit, Vector3 point)
    {
        SpellDefinition spell = definitions.Magic.Spell(flight.Spell!);
        if (spell.Radius <= 0)
        {
            if (hit.Present && hit.Kind == SpatialHitKind.Entity) ApplySpellHit(hit.Entity, flight.Shooter, spell);
            else CombatMessage(spell.Name + " stopped or missed.");
            return;
        }
        // An explosion starts on the incoming side of its impact surface. Each victim
        // needs its own masonry/furniture visibility ray, independent of other victims.
        Vector3 origin = hit.Present ? new(MathF.BitDecrement(point.X), point.Y, MathF.BitDecrement(point.Z)) : point;
        if (hit.Present)
        {
            float Before(float coordinate, float direction) => direction > 0 ? MathF.BitDecrement(coordinate) : direction < 0 ? MathF.BitIncrement(coordinate) : coordinate;
            origin = new(Before(point.X, flight.DirectionX), point.Y, Before(point.Z, flight.DirectionZ));
        }
        var dressing = scope.Features().Dressing;
        SpatialEntityCollider[] obstacles = CombatBodies().Where(b => b.Entity == dressing.BenchId || b.Entity == dressing.CrateId).ToArray();
        foreach (EnemyState target in enemies.Where(e => e.Alive).ToArray())
            if (Vector3.Distance(origin, EnemyAim(target)) <= spell.Radius && !scope.Scene.Trace(origin, EnemyAim(target), obstacles, 0).Present)
                ApplySpellHit(target.Id, flight.Shooter, spell);
        if (Vector3.Distance(origin, Aim(scope.Exploration.Position)) <= spell.Radius && !scope.Scene.Trace(origin, Aim(scope.Exploration.Position), obstacles, 0).Present)
            ApplySpellHit(scope.PartyId, flight.Shooter, spell);
        CombatMessage(spell.Name + " bursts; walls and closed gates block its spread.");
    }

    internal long Resisted(long power, string definition) => power * (100 - definitions.Magic.Resistances[definition]) / 100;

    private void ApplySpellHit(ulong target, ulong shooter, SpellDefinition spell)
    {
        EnemyState? foe = enemies.SingleOrDefault(e => e.Id == target && e.Alive);
        if (foe is not null)
        {
            if (shooter != scope.PartyId && !Combat.FriendlyFire) return;
            if (spell.Effect == SpellEffect.Damage) DamageEnemy(foe, Resisted(spell.Power, foe.Definition.Id));
            else if (definitions.Magic.Resistances[foe.Definition.Id] < 100) magic.Apply(new EnemyTarget(foe.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)), spell, spell.Duration * (100 - definitions.Magic.Resistances[foe.Definition.Id]) / 100);
            foe.Brain.Observe(null, scope.Exploration.Position);
        }
        else if (target == scope.PartyId && (shooter != scope.PartyId || Combat.FriendlyFire))
        {
            // Hostile magic follows the caster approach; directionless effects reach the center.
            EnemyState? caster = enemies.SingleOrDefault(enemy => enemy.Id == shooter);
            RiflesCharacter? member = caster is null ? party.Commander
                : MemberInLineOfFire(Aim(scope.Exploration.Position) - EnemyAim(caster));
            if (member is null) return;
            scope.CancelRest("Rest interrupted by hostile magic.");
            if (spell.Effect == SpellEffect.Damage) DamageMember(member, Resisted(spell.Power, member.Definition.Archetype));
            else if (definitions.Magic.Resistances[member.Definition.Archetype] < 100) magic.Apply(new MemberTarget(member.Definition.Id), spell, spell.Duration * (100 - definitions.Magic.Resistances[member.Definition.Archetype]) / 100);
        }
        CombatMessage(spell.Name + " struck " + (foe?.Definition.Name ?? "the party") + ".");
    }
}
