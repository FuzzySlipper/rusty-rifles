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
    private MagicState? magic;
    private Light? spellLight;
    private ulong spellLightId;
    private double PartySpeed => party.Members.Where(m => m.IsLiving).Select(m => magic!.Speed("member:" + m.Definition.Id)).DefaultIfEmpty(1).Min();


    private LightRequest SpellLightRequest()
    {
        MagicDefinition tuning = definitions.Magic;
        Vector3 point = scene!.Eye(exploration.VisualCell) with { Y = scene.GroundHeight(exploration.VisualCell) + tuning.LightHeight };
        return new(spellLightId, false, 0, new LightDescriptor(LightKind.Point,
            new(tuning.LightColor[0], tuning.LightColor[1], tuning.LightColor[2]),
            magic!.Has("party", SpellEffect.Light) && !combat.Defeated ? tuning.LightIntensity : 0,
            true, point, Vector3.UnitY, true, tuning.LightRange, 1, 0, 0, LightShadowIntent.Disabled));
    }
    private void UpdateSpellLight() => engine.Graphics.UpdateLight(new LightUpdateRequest(spellLight!, SpellLightRequest()));
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
                if (combat.ActionOf(Member(command.Member ?? id)).Busy) throw new InvalidDataException("Finish the current action before advancing.");
                magic!.AdvanceMember(command.Member ?? id, command.Choice ?? ""); combat.RefreshDevelopment();
                CombatMessage("Advancement learned."); return;
            case "rest-cancel": CancelRest("Rest cancelled; no recovery granted."); return;
        }
        if (paused || combat.Defeated) throw new InvalidDataException("Resume with a living party before acting.");
        if (command.Action == "rest")
        {
            if (magic!.RestRemaining > 0) throw new InvalidDataException("Already resting.");
            if (combat.Threatened || exploration.Moving || combat.ActionsBusy) throw new InvalidDataException("Rest requires a still, idle party without threats.");
            if (!Member(id).IsLiving) throw new InvalidDataException("A living member must supply the rest remedy.");
            if (!HasItem(id, definitions.Magic.RestItem)) throw new InvalidDataException("Selected member needs " + definitions.Magic.RestItem + " for rest.");
            magic.RestOwner = id; magic.RestRemaining = definitions.Magic.RestSeconds;
            CombatMessage("Rest begun; remedy and recovery settle only on completion."); return;
        }
        combat.BeginSpell(id, definitions.Magic.Spell(command.Spell ?? magic!.For(id).Selected), command.Member ?? id);
    }
    private bool HasItem(string member, string item) => inventory!.Items("member:" + member).Any(i => i.Definition == item && i.Quantity > 0);
    private void AdvanceMagic(double seconds)
    {
        magic!.Advance(seconds, (target, spell) =>
        {
            if (target.StartsWith("member:", StringComparison.Ordinal))
            {
                RiflesCharacter member = Member(target["member:".Length..]);
                combat.DamageMember(member, combat.Resisted(spell.Power, member.Definition.Archetype));
            }
            else if (target.StartsWith("enemy:", StringComparison.Ordinal))
            {
                EnemyState enemy = combat.Enemies.Single(e => e.Id.ToString() == target["enemy:".Length..]);
                if (enemy.Alive) combat.DamageEnemy(enemy, combat.Resisted(spell.Power, enemy.Definition.Id));
            }
        });
        if (magic!.RestRemaining > 0)
        {
            if (combat.Threatened || exploration.Moving || combat.ActionsBusy) CancelRest("Rest interrupted; no recovery granted.");
            else
            {
                magic.RestRemaining = Math.Max(0, magic.RestRemaining - seconds);
                if (magic.RestRemaining == 0)
                {
                    string owner = magic.RestOwner; magic.RestOwner = "";
                    if (!Member(owner).IsLiving || !HasItem(owner, definitions.Magic.RestItem)) CombatMessage("Rest failed: remedy unavailable.");
                    else
                    {
                        inventory!.Consume(new MemberOwner(owner), definitions.Magic.RestItem, 1);
                        foreach (RiflesCharacter member in party.Members.Where(m => m.IsLiving))
                        {
                            member.Heal(definitions.Magic.RestVitality); member.RecoverResource(definitions.Magic.RestResource);
                            magic.Clear("member:" + member.Definition.Id, true);
                        }
                        CombatMessage("Rest complete; one remedy consumed. Fallen members still need revival.");
                    }
                }
            }
        }
        combat.RefreshDevelopment();
    }
    private uint MagicProjection(SessionValueBuilder value)
    {
        var book = magic!.For(selectedMember);
        uint spells = value.Object(definitions.Magic.Spells.Select(spell =>
        {
            string reason = "";
            try
            {
                if (spell.Target != SpellTarget.Ally) combat.ValidateSpell(selectedMember, spell, selectedMember, combat.SelectedTarget, itemWorld!.Revision, false);
                else
                {
                    bool valid = false;
                    foreach (RiflesCharacter candidate in party.Members)
                    {
                        try { combat.ValidateSpell(selectedMember, spell, candidate.Definition.Id, combat.SelectedTarget, itemWorld!.Revision, false); valid = true; break; }
                        catch (InvalidDataException error) { reason = error.Message; }
                    }
                    if (valid) reason = "";
                }
            }
            catch (InvalidDataException error) { reason = error.Message; }
            if (paused) reason = "Resume to cast.";
            return (spell.Id, value.Object(("name", value.String(spell.Name)), ("description", value.String(spell.Description)),
                ("target", value.String(spell.Target.ToString())), ("cost", value.Number(combat.SpellCost(selectedMember, spell))),
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
                ("reason", value.String(magic.RestRemaining > 0 ? "Resting · " + utility : (combat.Threatened ? "Threats prevent rest. " : "Rest needs an idle party and a remedy. ") + utility)))),
            ("choices", value.Object(definitions.Magic.Choices.Select(choice => (choice.Id,
                value.Object(("name", value.String(choice.Name)), ("description", value.String(choice.Description))))).ToArray())));
    }
}
