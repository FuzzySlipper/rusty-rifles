using Rifles.Game.Characters;
using Rifles.Game.Items;
using Rifles.Game.Presentation;

namespace Rifles.Game.Combat;

/// <summary>Small, pure admission checks shared by live musket actions and focused checks.</summary>
internal static class MusketActionRules
{
    internal static bool CanInterruptReload(ActionSnapshot? action) => action is
    {
        Kind: CombatActionKind.Reload,
        Phase: ActionPhase.Windup,
    };

    internal static bool CanAutoReload(bool living, bool idle, bool equipped, bool repositioning, WeaponCapabilities? weapon) => living && idle
        && equipped && !repositioning && weapon is { FireDamage: > 0, Loaded: false };
}

internal sealed partial class RiflesCombat
{
    internal IReadOnlyList<MemberOrderReadout> ReadBayonetOrder(bool fix) => ChargeExecuting
        ? ChargeLockedReadout() : EvaluateBayonetOrder(fix, false).Readout;

    internal GameOutcome BeginBayonetOrder(bool fix, bool paused)
    {
        if (ChargeExecuting) return GameOutcome.Reject("The party is charging.");
        if (paused || Defeated) return GameOutcome.Reject(paused ? "Resume before ordering." : "The commander has fallen.");
        (MemberOrderReadout[] Readout, int Started) evaluation = EvaluateBayonetOrder(fix, true);
        if (evaluation.Started == 0) return GameOutcome.Reject((fix ? "Fix" : "Unfix") + " order found no ready compatible musket.");
        scope.CancelRest("Rest interrupted by a bayonet order.");
        CombatMessage((fix ? "Fix" : "Unfix") + " bayonets: " + evaluation.Started + " soldiers started.");
        return GameOutcome.Accept();
    }

    /// <summary>
    /// Begins only the compatible soldiers' independent timed bayonet actions.
    /// Reload windup is discarded only when that soldier can immediately begin
    /// this maneuver; recovery is always left intact.
    /// </summary>
    private (MemberOrderReadout[] Readout, int Started) EvaluateBayonetOrder(bool fix, bool start)
    {
        List<MemberOrderReadout> readout = [];
        int started = 0;
        foreach (RiflesCharacter soldier in party.Members.Where(member => !member.Definition.Commander))
        {
            ActionState state = ActionOf(soldier);
            CarriedItem? carried = Weapon(soldier.Definition.Id);
            WeaponCapabilities? weapon = Capabilities(carried);
            BayonetDefinition? bayonet = Bayonet(carried, weapon);
            bool cancelReload = MusketActionRules.CanInterruptReload(state.Current);
            string? reason = BayonetReadiness(soldier, state, weapon, bayonet, fix, cancelReload);
            if (reason is not null) { readout.Add(new(soldier.Definition.Id, false, reason, "", "")); continue; }

            if (!start) { readout.Add(new(soldier.Definition.Id, true, "Ready.", "", "")); continue; }
            if (cancelReload) InterruptReloadForManeuver(soldier);
            BayonetActionTiming timing = fix ? bayonet!.Fix : bayonet!.Unfix;
            state.Start(NewAction(fix ? CombatActionKind.FixBayonet : CombatActionKind.UnfixBayonet,
                carried!.Entity, capabilities: weapon, bayonetTiming: timing));
            started++;
            readout.Add(new(soldier.Definition.Id, true, start ? "Started." : "Ready.", "", ""));
        }
        return (readout.ToArray(), started);
    }

    internal bool InterruptReloadForManeuver(RiflesCharacter member)
    {
        ActionState state = ActionOf(member);
        if (!MusketActionRules.CanInterruptReload(state.Current)) return false;
        state.Cancel();
        CombatMessage(member.Definition.Name + " stopped reloading.");
        return true;
    }

    private void BeginAutomaticReload(RiflesCharacter member, ActionState state)
    {
        if (ChargeExecuting) return;
        CarriedItem? carried = Weapon(member.Definition.Id);
        WeaponCapabilities? weapon = Capabilities(carried);
        if (!MusketActionRules.CanAutoReload(member.IsLiving, !state.Busy, carried is not null, party.Formation.Affects(member.InstanceId), weapon)) return;
        state.Start(NewAction(CombatActionKind.Reload, carried!.Entity, capabilities: weapon));
        CombatMessage(member.Definition.Name + " begins reloading.");
    }

    private BayonetDefinition? Bayonet(CarriedItem? carried, WeaponCapabilities? weapon) => carried is null || weapon?.FireReach is null
        ? null : definitions.Items.Item(carried.Definition).Weapon?.Bayonet;

    private string? BayonetReadiness(RiflesCharacter soldier, ActionState state, WeaponCapabilities? weapon,
        BayonetDefinition? bayonet, bool fix, bool cancelReload)
    {
        if (!soldier.IsLiving) return "Fallen.";
        if (party.Formation.Affects(soldier.InstanceId)) return "Repositioning.";
        if (state.Busy && !cancelReload) return "Busy.";
        if (bayonet is null || weapon is null) return "No compatible musket.";
        if (fix && weapon.BayonetFixed) return "Bayonet already fixed.";
        if (!fix && !weapon.BayonetFixed) return "Bayonet is not fixed.";
        return null;
    }
}
