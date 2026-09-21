using Rifles.Game.Audio;
using System.Numerics;
using Rifles.Game.Combat;
using Rifles.Game.Magic;
using Rifles.Game.Characters;
using Rifles.Game.Items;
using Rifles.Game.Party;
using Rifles.Game.Presentation;
using Rusty.Engine;

namespace Rifles.Game;

public sealed partial class RiflesProduct
{
    private Light? spellLight;
    private ulong spellLightId;
    private double PartySpeed => party.Members.Where(m => m.IsLiving).Select(m => active.Magic.Speed(new MemberTarget(m.Definition.Id))).DefaultIfEmpty(1).Min();


    private LightRequest SpellLightRequest()
    {
        MagicDefinition tuning = definitions.Magic;
        Vector3 point = active.Scene.Eye(active.Exploration.VisualCell) with { Y = active.Scene.GroundHeight(active.Exploration.VisualCell) + tuning.LightHeight };
        return new(spellLightId, false, 0, new LightDescriptor(LightKind.Point,
            new(tuning.LightColor[0], tuning.LightColor[1], tuning.LightColor[2]),
            active.Magic.Has(new PartyTarget(), SpellEffect.Light) && !active.Combat.Defeated ? tuning.LightIntensity : 0,
            true, point, Vector3.UnitY, true, tuning.LightRange, 1, 0, 0, LightShadowIntent.Disabled));
    }
    private void UpdateSpellLight() => engine.Graphics.UpdateLight(new LightUpdateRequest(spellLight!, SpellLightRequest()));
    private void CancelRest(string reason)
    {
        if (party.RestRemaining <= 0) return;
        party.RestRemaining = 0; party.RestOwner = ""; CombatMessage(reason);
    }
    private GameOutcome MagicCommand(SessionCommand command)
    {
        string id = selectedMember;
        switch (command.Action)
        {
            case "spell-select": return SelectSpell(id, command.Spell ?? "");
            case "spell-cancel": return SelectSpell(id, "");
            case "spell-assign":
                if (!int.TryParse(command.Slot, out int slot)) return GameOutcome.Reject("Choose a hotbar slot.");
                return AssignSpell(id, command.Spell ?? SelectedSpell(id), slot);
            case "spell-hotbar":
                if (!int.TryParse(command.Slot, out int hotbar) || hotbar < 0 || hotbar >= active.Magic.For(id).Hotbar.Length)
                    return GameOutcome.Reject("Unknown hotbar slot.");
                return SelectSpell(id, active.Magic.For(id).Hotbar[hotbar]);
            case "advance":
                if (Member(command.Member ?? id).Definition.Commander) return GameOutcome.Reject("Choose a soldier to advance.");
                if (active.Combat.ActionOf(Member(command.Member ?? id)).Busy) return GameOutcome.Reject("Finish the current action before advancing.");
                try { active.Magic.AdvanceMember(command.Member ?? id, command.Choice ?? ""); }
                catch (InvalidDataException error) { return GameOutcome.Reject(error.Message); }
                CombatMessage("Advancement learned."); return GameOutcome.Accept();
            case "rest-cancel": CancelRest("Rest cancelled; no recovery granted."); return GameOutcome.Accept();
            case "rest": return BeginRest(id);
            case "ability":
            case "cast":
                if (paused || active.Combat.Defeated) return GameOutcome.Reject("Resume with a living commander before casting.");
                string spellId = command.Spell ?? SelectedSpell(id);
                if (spellId.Length == 0) return GameOutcome.Reject("Select a known spell.");
                return active.Combat.BeginAbilityOrder(spellId, command.Member ?? id, paused);
            default: return GameOutcome.Reject("Unknown command");
        }
    }

    private string SelectedSpell(string member)
    {
        try { return active.Magic.For(member).Selected; }
        catch (InvalidDataException) { return ""; }
    }

    private GameOutcome SelectSpell(string member, string spell)
    {
        try { active.Magic.Select(member, spell); }
        catch (InvalidDataException error) { return GameOutcome.Reject(error.Message); }
        return GameOutcome.Accept();
    }

    private GameOutcome AssignSpell(string member, string spell, int slot)
    {
        try { active.Magic.Assign(member, spell, slot); }
        catch (InvalidDataException error) { return GameOutcome.Reject(error.Message); }
        return GameOutcome.Accept();
    }

    private GameOutcome BeginRest(string id)
    {
        if (paused || active.Combat.Defeated) return GameOutcome.Reject("Resume with a living party before acting.");
        if (party.RestRemaining > 0) return GameOutcome.Reject("Already resting.");
        if (active.Combat.ChargeExecuting || party.Formation.Executing || active.Combat.Threatened || active.Exploration.Moving || active.Combat.ActionsBusy) return GameOutcome.Reject("Rest requires a still, idle party without threats.");
        if (!Member(id).IsLiving) return GameOutcome.Reject("A living member must supply the rest remedy.");
        if (!HasItem(id, definitions.Magic.RestItem)) return GameOutcome.Reject("Selected member needs " + definitions.Magic.RestItem + " for rest.");
        party.RestOwner = id; party.RestRemaining = definitions.Magic.RestSeconds;
        CombatMessage("Rest begun; remedy and recovery settle only on completion.");
        return GameOutcome.Accept();
    }
    private string AllySpellReason(SpellDefinition spell)
    {
        string reason = "";
        foreach (RiflesCharacter candidate in party.Members)
        {
            string? availability = active.Combat.SpellAvailability(selectedMember, spell, candidate.Definition.Id, active.Combat.SelectedTarget, active.ItemWorld.Revision);
            if (availability is null) return "";
            reason = availability;
        }
        return reason;
    }

    private bool HasItem(string member, string item) => active.Inventory.Items("member:" + member).Any(i => i.Definition == item && i.Quantity > 0);
    private void AdvanceMagic(double seconds)
    {
        active.Magic.Advance(seconds, (target, spell) =>
        {
            if (target.StartsWith("member:", StringComparison.Ordinal))
            {
                RiflesCharacter member = Member(target["member:".Length..]);
                active.Combat.DamageMember(member, active.Combat.Resisted(spell.Power, member.Definition.Archetype));
            }
            else if (target.StartsWith("enemy:", StringComparison.Ordinal))
            {
                EnemyState enemy = active.Combat.Enemies.Single(e => e.Id.ToString() == target["enemy:".Length..]);
                if (enemy.Alive) active.Combat.DamageEnemy(enemy, active.Combat.Resisted(spell.Power, enemy.Definition.Id));
            }
        });
        if (party.RestRemaining > 0)
        {
            if (active.Combat.ChargeExecuting || party.Formation.Executing || active.Combat.Threatened || active.Exploration.Moving || active.Combat.ActionsBusy) CancelRest("Rest interrupted; no recovery granted.");
            else
            {
                party.RestRemaining = Math.Max(0, party.RestRemaining - seconds);
                if (party.RestRemaining == 0)
                {
                    string owner = party.RestOwner; party.RestOwner = "";
                    if (!Member(owner).IsLiving || !HasItem(owner, definitions.Magic.RestItem)) CombatMessage("Rest failed: remedy unavailable.");
                    else
                    {
                        active.Inventory.Consume(new MemberOwner(owner), definitions.Magic.RestItem, 1);
                        foreach (RiflesCharacter member in party.Members.Where(m => m.IsLiving))
                        {
                            member.Heal(definitions.Magic.RestVitality); member.RecoverResource(definitions.Magic.RestResource);
                            active.Magic.Clear(new MemberTarget(member.Definition.Id), true);
                        }
                        CombatMessage("Rest complete; one remedy consumed. Fallen members still need revival.");
                    }
                }
            }
        }
    }
    private uint MagicProjection(SessionValueBuilder value)
    {
        var book = active.Magic.For(selectedMember);
        uint spells = value.Object(definitions.Magic.Spells.Select(spell =>
        {
            // Rendered availability is the execution check, not a copy of it.
            string reason = spell.Target != SpellTarget.Ally
                ? active.Combat.SpellAvailability(selectedMember, spell, selectedMember, active.Combat.SelectedTarget, active.ItemWorld.Revision) ?? ""
                : AllySpellReason(spell);
            if (paused) reason = "Resume to cast.";
            return (spell.Id, value.Object(("name", value.String(spell.Name)), ("description", value.String(spell.Description)),
                ("target", value.String(spell.Target.ToString())), ("cost", value.Number(active.Combat.SpellCost(selectedMember, spell))),
                ("windup", value.Number(spell.Windup)), ("recovery", value.Number(spell.Recovery)),
                ("known", value.Number(book.Known.Contains(spell.Id) ? 1 : 0)), ("available", value.Number(reason.Length == 0 ? 1 : 0)), ("reason", value.String(reason))));
        }).ToArray());
        uint members = value.Object(party.Members.Select(member =>
        {
            var state = active.Magic.For(member.Definition.Id);
            return (member.Definition.Id, value.Object(("name", value.String(member.Definition.Name)),
                ("conditions", value.String(active.Magic.Describe(new MemberTarget(member.Definition.Id)))), ("experience", value.Number(state.Experience)),
                ("unspent", value.Number(state.Unspent)), ("revivals", value.Number(definitions.Magic.MaximumRevivals - state.Revivals))));
        }).ToArray());
        string utility = active.Magic.Describe(new PartyTarget());
        if (active.Magic.Has(new PartyTarget(), SpellEffect.Reveal))
        {
            float radius = active.Magic.Radius(new PartyTarget(), SpellEffect.Reveal);
            bool Revealed(Vector3 point) => Vector3.Distance(Aim(active.Exploration.Position), point) <= radius
                && !active.Scene.Trace(Aim(active.Exploration.Position), point, [], partyId).Present;
            var anchors = active.ItemWorld.Capture().Anchors.Where(a => Revealed(active.Scene.Eye(a.Cell)));
            utility += " · Near and visible: " + string.Join(", ", anchors.Select(a => a.Key + " (" + active.Inventory.Items(a.Key).Count + " item stacks)"));
            if (Revealed(active.ItemWorld.LeverPoint(active.Scene))) utility += " · Gate requires key, weighted plate and lever together.";
        }
        return value.Object(("selectedSpell", value.String(book.Selected)), ("spells", spells), ("members", members),
            ("hotbar", value.Object(book.Hotbar.Select((spell, i) => (i.ToString(), value.String(spell))).ToArray())),
            ("rest", value.Object(("active", value.Number(party.RestRemaining > 0 ? 1 : 0)), ("remaining", value.Number(party.RestRemaining)),
                ("reason", value.String(party.RestRemaining > 0 ? "Resting · " + utility : (active.Combat.Threatened ? "Threats prevent rest. " : "Rest needs an idle party and a remedy. ") + utility)))),
            ("choices", value.Object(definitions.Magic.Choices.Select(choice => (choice.Id,
                value.Object(("name", value.String(choice.Name)), ("description", value.String(choice.Description))))).ToArray())));
    }
}
