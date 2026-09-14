using System.Numerics;
using Rifles.Game.Combat;
using Rifles.Game.Magic;
using Rifles.Game.Party;
using Rifles.Game.Presentation;
using Rusty.Engine;

namespace Rifles.Game;

public sealed partial class RiflesProduct
{
    private MagicState? magic;
    private Light? spellLight;
    private ulong spellLightId;
    private double PartySpeed => party.Members.Where(m => m.IsLiving).Select(m => magic!.Speed("member:" + m.Definition.Id)).DefaultIfEmpty(1).Min();


    private void RecomputeMagic()
    {
        foreach (PartyMemberState member in party.Members)
            member.SetDevelopmentBonuses(magic!.Power(member.Definition.Id),
                magic.Defense(member.Definition.Id) + magic.DefenseBonus("member:" + member.Definition.Id));
    }
    private LightRequest SpellLightRequest()
    {
        MagicDefinition tuning = definitions.Magic;
        Vector3 point = scene!.Eye(exploration.VisualCell) with { Y = scene.GroundHeight(exploration.VisualCell) + tuning.LightHeight };
        return new(spellLightId, false, 0, new LightDescriptor(LightKind.Point,
            new(tuning.LightColor[0], tuning.LightColor[1], tuning.LightColor[2]),
            magic!.Has("party", SpellEffect.Light) && !Defeated ? tuning.LightIntensity : 0,
            true, point, Vector3.UnitY, true, tuning.LightRange, 1, 0, 0, LightShadowIntent.Disabled));
    }
    private void UpdateSpellLight() => engine.Graphics.UpdateLight(new LightUpdateRequest(spellLight!, SpellLightRequest()));
    private bool Threatened => enemies.Any(e => e.Alive && (e.Aware
        || Vector3.Distance(EnemyAim(e), Aim(exploration.Position)) <= definitions.Magic.ThreatRange && SeesParty(e)));
    private void CancelRest(string reason)
    {
        if (magic!.RestRemaining <= 0) return;
        magic.RestRemaining = 0; magic.RestOwner = ""; CombatMessage(reason);
    }
    private void MagicCommand(SessionCommand command)
    {
        string id = selectedMember;
        switch (command.Action)
        {
            case "spell-select": magic!.Select(id, command.Spell ?? ""); return;
            case "spell-cancel": magic!.Select(id, ""); return;
            case "spell-assign":
                if (!int.TryParse(command.Slot, out int slot)) throw new InvalidDataException("Choose a hotbar slot.");
                magic!.Assign(id, command.Spell ?? magic.For(id).Selected, slot); return;
            case "spell-hotbar":
                if (!int.TryParse(command.Slot, out int hotbar) || hotbar < 0 || hotbar >= magic!.For(id).Hotbar.Length)
                    throw new InvalidDataException("Unknown hotbar slot.");
                magic.Select(id, magic.For(id).Hotbar[hotbar]); return;
            case "advance":
                if (actions[command.Member ?? id].Busy) throw new InvalidDataException("Finish the current action before advancing.");
                magic!.AdvanceMember(command.Member ?? id, command.Choice ?? ""); RecomputeMagic();
                CombatMessage("Advancement learned."); return;
            case "rest-cancel": CancelRest("Rest cancelled; no recovery granted."); return;
        }
        if (paused || Defeated) throw new InvalidDataException("Resume with a living party before acting.");
        if (command.Action == "rest")
        {
            if (magic!.RestRemaining > 0) throw new InvalidDataException("Already resting.");
            if (Threatened || exploration.Moving || actions.Values.Any(a => a.Busy)) throw new InvalidDataException("Rest requires a still, idle party without threats.");
            if (!Member(id).IsLiving) throw new InvalidDataException("A living member must supply the rest remedy.");
            if (!HasItem(id, definitions.Magic.RestItem)) throw new InvalidDataException("Selected member needs " + definitions.Magic.RestItem + " for rest.");
            magic.RestOwner = id; magic.RestRemaining = definitions.Magic.RestSeconds;
            CombatMessage("Rest begun; remedy and recovery settle only on completion."); return;
        }
        BeginSpell(id, definitions.Magic.Spell(command.Spell ?? magic!.For(id).Selected), command.Member ?? id);
    }
    private bool HasItem(string member, string item) => inventory!.Items("member:" + member).Any(i => i.Definition == item && i.Quantity > 0);
    private long SpellCost(string member, SpellDefinition spell) => Math.Max(0, spell.Cost - magic!.CostDiscount(member));
    private void ValidateSpell(string memberId, SpellDefinition spell, string targetMember, ulong target, ulong featureRevision, bool committing)
    {
        PartyMemberState member = Member(memberId);
        if (!member.IsLiving || !magic!.For(memberId).Known.Contains(spell.Id)) throw new InvalidDataException("That character cannot cast this spell.");
        if (member.Resource < SpellCost(memberId, spell)) throw new InvalidDataException("Insufficient resource.");
        if (!committing && actions[memberId].Busy) throw new InvalidDataException("That character is still acting.");
        if (spell.Target == SpellTarget.Enemy && !committing)
        {
            EnemyState? foe = enemies.SingleOrDefault(e => e.Id == target && Visible(e));
            if (foe is null || Vector3.Distance(Aim(exploration.Position), EnemyAim(foe)) > spell.Range)
                throw new InvalidDataException("Select a visible enemy within spell range.");
        }
        if (spell.Target == SpellTarget.Ally)
        {
            PartyMemberState ally = Member(targetMember);
            if (spell.Effect == SpellEffect.Revive)
            {
                if (ally.IsLiving || magic.For(targetMember).Revivals >= definitions.Magic.MaximumRevivals || Threatened)
                    throw new InvalidDataException("Revival needs a fallen ally with a revival remaining and no threats.");
                if (!HasItem(memberId, definitions.Magic.RevivalItem)) throw new InvalidDataException("Caster needs " + definitions.Magic.RevivalItem + " for revival.");
            }
            else if (!ally.IsLiving) throw new InvalidDataException("Choose a living ally.");
            if (spell.Effect == SpellEffect.Heal && ally.Vitality == ally.MaximumVitality) throw new InvalidDataException("Ally needs no healing.");
        }
        if (spell.Target == SpellTarget.Feature)
        {
            bool generated = generatedFeatures.Gates.Any(g => g.Id == target) || generatedFeatures.Hazards.Any(h => h.Id == target);
            if (generated && GeneratedUseProblem(target) is { } problem) throw new InvalidDataException(problem);
            Vector3 point = generated ? GeneratedFeaturePoint(target) : itemWorld!.LeverPoint(scene!);
            ulong revision = generated ? generatedFeatures.Revision : itemWorld!.Revision;
            if (!definitions.Magic.AllowLeverMagic || featureRevision != revision
                || !itemWorld!.Reachable(point, exploration, scene!) || Vector3.Distance(Aim(exploration.Position), point) > spell.Range)
                throw new InvalidDataException("No permitted mechanism within reach, or the feature changed.");
        }
    }
    private void BeginSpell(string member, SpellDefinition spell, string targetMember)
    {
        ulong targetId = selectedTarget;
        ulong targetRevision = itemWorld!.Revision;
        if (spell.Target == SpellTarget.Feature)
        {
            var focused = features!.Readout?.Selected;
            targetId = focused is { } selected && (generatedFeatures.Gates.Any(g => g.Id == selected.Id)
                || generatedFeatures.Hazards.Any(h => h.Id == selected.Id)) ? focused.Value.Id : itemWorld.Capture().LeverId;
            targetRevision = targetId == itemWorld.Capture().LeverId ? itemWorld.Revision : generatedFeatures.Revision;
        }
        ValidateSpell(member, spell, targetMember, targetId, targetRevision, false);
        CancelRest("Rest interrupted by casting.");
        EnemyState? target = spell.Target == SpellTarget.Enemy ? enemies.Single(e => e.Id == selectedTarget) : null;
        actions[member].Start(new(CombatActionKind.Cast, 0, null, null, spell.Target == SpellTarget.Feature ? targetId : target?.Id ?? 0, targetMember,
            spell.Windup, ActionPhase.Windup, spell.Recovery, target?.Motion.Position,
            target?.Motion.CrowdOffset.X ?? 0, target?.Motion.CrowdOffset.Y ?? 0, spell.Id, SpellCost(member, spell), targetRevision));
        CombatMessage(Member(member).Definition.Name + " prepares " + spell.Name + ".");
    }
    private bool TryEnemySpell(EnemyState enemy, float distance)
    {
        if (!definitions.Magic.EnemySpells.TryGetValue(enemy.Definition.Id, out string? id)) return false;
        SpellDefinition spell = definitions.Magic.Spell(id);
        if (enemy.Resource < spell.Cost || distance > spell.Range) return false;
        enemy.Action.Start(new(CombatActionKind.Cast, 0, null, null, partyId, null,
            spell.Windup, ActionPhase.Windup, spell.Recovery, exploration.Position, Spell: id, Cost: spell.Cost));
        return true;
    }
    private void CommitSpell(string? memberId, EnemyState? enemy, ActionSnapshot action)
    {
        SpellDefinition spell = definitions.Magic.Spell(action.Spell!);
        if (memberId is not null)
        {
            ValidateSpell(memberId, spell, action.TargetMember!, action.Target, action.FeatureRevision, true);
            if (Member(memberId).Resource < action.Cost) throw new InvalidDataException("Insufficient resource at commit.");
            if (spell.Effect == SpellEffect.Revive) inventory!.Consume("member:" + memberId, definitions.Magic.RevivalItem, 1);
            Member(memberId).SpendResource(action.Cost);
        }
        else
        {
            if (enemy is null || !enemy.Alive || enemy.Resource < action.Cost) return;
            enemy.SpendResource(action.Cost);
        }
        if (spell.Target == SpellTarget.Enemy)
        {
            Vector3 source = enemy is null ? Aim(exploration.Position) : EnemyAim(enemy);
            Vector3 end = Aim(action.AimCell!.Value) + new Vector3(action.AimOffsetX, 0, action.AimOffsetY) * scene!.LogicalCellSize;
            Vector3 offset = end - source;
            if (offset.LengthSquared() == 0) { CombatMessage("Spell dissipated at its origin."); return; }
            Vector3 direction = Vector3.Normalize(offset);
            flights.Add(new(AllocateId(), enemy?.Id ?? partyId, memberId, CombatActionKind.Cast,
                source.X, source.Y, source.Z, direction.X, direction.Y, direction.Z, Math.Min(offset.Length(), spell.Range),
                enemy?.Motion.Position ?? exploration.Position, null, null, spell.Id));
        }
        else if (spell.Effect == SpellEffect.Lever)
        {
            if (action.Target == itemWorld!.Capture().LeverId) itemWorld.ToggleLever(exploration, scene!, action.FeatureRevision);
            else CombatMessage(UseGeneratedFeature(new(action.Target, action.FeatureRevision)));
        }
        else if (spell.Target == SpellTarget.Party) magic!.Apply("party", spell);
        else
        {
            PartyMemberState target = Member(action.TargetMember!);
            string key = "member:" + target.Definition.Id;
            switch (spell.Effect)
            {
                case SpellEffect.Heal: target.Heal(spell.Power); break;
                case SpellEffect.Revive: target.Heal(spell.Power); magic!.For(target.Definition.Id).Revivals++; break;
                case SpellEffect.Cleanse: magic!.Clear(key, true); break;
                default: magic!.Apply(key, spell); break;
            }
        }
        RecomputeMagic();
        CombatMessage((memberId is null ? enemy!.Definition.Name : Member(memberId).Definition.Name) + " casts " + spell.Name + ".");
    }
    private void ResolveSpellImpact(FlightSnapshot flight, SpatialHit hit, Vector3 point)
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
        var dressing = features!.Capture().Dressing;
        SpatialEntityCollider[] obstacles = CombatBodies().Where(b => b.Entity == dressing.BenchId || b.Entity == dressing.CrateId).ToArray();
        foreach (EnemyState target in enemies.Where(e => e.Alive).ToArray())
            if (Vector3.Distance(origin, EnemyAim(target)) <= spell.Radius && !scene!.Trace(origin, EnemyAim(target), obstacles, 0).Present)
                ApplySpellHit(target.Id, flight.Shooter, spell);
        if (Vector3.Distance(origin, Aim(exploration.Position)) <= spell.Radius && !scene!.Trace(origin, Aim(exploration.Position), obstacles, 0).Present)
            ApplySpellHit(partyId, flight.Shooter, spell);
        CombatMessage(spell.Name + " bursts; walls and closed gates block its spread.");
    }
    private long Resisted(long power, string definition) => power * (100 - definitions.Magic.Resistances[definition]) / 100;
    private void ApplySpellHit(ulong target, ulong shooter, SpellDefinition spell)
    {
        EnemyState? foe = enemies.SingleOrDefault(e => e.Id == target && e.Alive);
        if (foe is not null)
        {
            if (shooter != partyId && !Combat.FriendlyFire) return;
            if (spell.Effect == SpellEffect.Damage) DamageEnemy(foe, Resisted(spell.Power, foe.Definition.Id));
            else if (definitions.Magic.Resistances[foe.Definition.Id] < 100) magic!.Apply("enemy:" + foe.Id, spell, spell.Duration * (100 - definitions.Magic.Resistances[foe.Definition.Id]) / 100);
            foe.Brain.Observe(null, exploration.Position);
        }
        else if (target == partyId && (shooter != partyId || Combat.FriendlyFire))
        {
            PartyMemberState? member = party.Members.Where(m => m.IsLiving).OrderBy(m => m.Slot).FirstOrDefault();
            if (member is null) return;
            CancelRest("Rest interrupted by hostile magic.");
            if (spell.Effect == SpellEffect.Damage) DamageMember(member, Resisted(spell.Power, member.Definition.Id));
            else if (definitions.Magic.Resistances[member.Definition.Id] < 100) magic!.Apply("member:" + member.Definition.Id, spell, spell.Duration * (100 - definitions.Magic.Resistances[member.Definition.Id]) / 100);
        }
        CombatMessage(spell.Name + " struck " + (foe?.Definition.Name ?? "the party") + ".");
        RecomputeMagic();
    }
    private long DamageMember(PartyMemberState member, long damage)
    {
        CancelRest("Rest interrupted by injury.");
        long applied = member.ApplyDamage(checked((long)Math.Ceiling(damage * definitions.Run.Difficulty(progress.Difficulty).IncomingDamageMultiplier)));
        if (!member.IsLiving) { actions[member.Definition.Id].Cancel(); magic!.Clear("member:" + member.Definition.Id); }
        return applied;
    }
    private void AdvanceMagic(double seconds)
    {
        magic!.Advance(seconds, (target, spell) =>
        {
            if (target.StartsWith("member:", StringComparison.Ordinal))
            {
                PartyMemberState member = Member(target["member:".Length..]);
                DamageMember(member, Resisted(spell.Power, member.Definition.Id));
            }
            else if (target.StartsWith("enemy:", StringComparison.Ordinal))
            {
                EnemyState enemy = enemies.Single(e => e.Id.ToString() == target["enemy:".Length..]);
                if (enemy.Alive) DamageEnemy(enemy, Resisted(spell.Power, enemy.Definition.Id));
            }
        });
        if (magic.RestRemaining > 0)
        {
            if (Threatened || exploration.Moving || actions.Values.Any(a => a.Busy)) CancelRest("Rest interrupted; no recovery granted.");
            else
            {
                magic.RestRemaining = Math.Max(0, magic.RestRemaining - seconds);
                if (magic.RestRemaining == 0)
                {
                    string owner = magic.RestOwner; magic.RestOwner = "";
                    if (!Member(owner).IsLiving || !HasItem(owner, definitions.Magic.RestItem)) CombatMessage("Rest failed: remedy unavailable.");
                    else
                    {
                        inventory!.Consume("member:" + owner, definitions.Magic.RestItem, 1);
                        foreach (PartyMemberState member in party.Members.Where(m => m.IsLiving))
                        {
                            member.Heal(definitions.Magic.RestVitality); member.RecoverResource(definitions.Magic.RestResource);
                            magic.Clear("member:" + member.Definition.Id, true);
                        }
                        CombatMessage("Rest complete; one remedy consumed. Fallen members still need revival.");
                    }
                }
            }
        }
        RecomputeMagic();
    }
    private uint MagicProjection(SessionValueBuilder value)
    {
        var book = magic!.For(selectedMember);
        uint spells = value.Object(definitions.Magic.Spells.Select(spell =>
        {
            string reason = "";
            try
            {
                if (spell.Target != SpellTarget.Ally) ValidateSpell(selectedMember, spell, selectedMember, selectedTarget, itemWorld!.Revision, false);
                else
                {
                    bool valid = false;
                    foreach (PartyMemberState candidate in party.Members)
                    {
                        try { ValidateSpell(selectedMember, spell, candidate.Definition.Id, selectedTarget, itemWorld!.Revision, false); valid = true; break; }
                        catch (InvalidDataException error) { reason = error.Message; }
                    }
                    if (valid) reason = "";
                }
            }
            catch (InvalidDataException error) { reason = error.Message; }
            if (paused) reason = "Resume to cast.";
            return (spell.Id, value.Object(("name", value.String(spell.Name)), ("description", value.String(spell.Description)),
                ("target", value.String(spell.Target.ToString())), ("cost", value.Number(SpellCost(selectedMember, spell))),
                ("windup", value.Number(spell.Windup)), ("recovery", value.Number(spell.Recovery)),
                ("known", value.Number(book.Known.Contains(spell.Id) ? 1 : 0)), ("available", value.Number(reason.Length == 0 ? 1 : 0)), ("reason", value.String(reason))));
        }).ToArray());
        uint members = value.Object(party.Members.Select(member =>
        {
            var state = magic.For(member.Definition.Id);
            return (member.Definition.Id, value.Object(("name", value.String(member.Definition.Name)),
                ("conditions", value.String(magic.Describe("member:" + member.Definition.Id))), ("experience", value.Number(state.Experience)),
                ("unspent", value.Number(state.Unspent)), ("revivals", value.Number(definitions.Magic.MaximumRevivals - state.Revivals))));
        }).ToArray());
        string utility = magic.Describe("party");
        if (magic.Has("party", SpellEffect.Reveal))
        {
            float radius = magic.Radius("party", SpellEffect.Reveal);
            bool Revealed(Vector3 point) => Vector3.Distance(Aim(exploration.Position), point) <= radius
                && !scene!.Trace(Aim(exploration.Position), point, [], partyId).Present;
            var anchors = itemWorld!.Capture().Anchors.Where(a => Revealed(scene!.Eye(a.Cell)));
            utility += " · Near and visible: " + string.Join(", ", anchors.Select(a => a.Key + " (" + inventory!.Items(a.Key).Count + " item stacks)"));
            if (Revealed(itemWorld.LeverPoint(scene!))) utility += " · Gate requires key, weighted plate and lever together.";
        }
        return value.Object(("selectedSpell", value.String(book.Selected)), ("spells", spells), ("members", members),
            ("hotbar", value.Object(book.Hotbar.Select((spell, i) => (i.ToString(), value.String(spell))).ToArray())),
            ("rest", value.Object(("active", value.Number(magic.RestRemaining > 0 ? 1 : 0)), ("remaining", value.Number(magic.RestRemaining)),
                ("reason", value.String(magic.RestRemaining > 0 ? "Resting · " + utility : (Threatened ? "Threats prevent rest. " : "Rest needs an idle party and a remedy. ") + utility)))),
            ("choices", value.Object(definitions.Magic.Choices.Select(choice => (choice.Id,
                value.Object(("name", value.String(choice.Name)), ("description", value.String(choice.Description))))).ToArray())));
    }
}
