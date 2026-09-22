using Rifles.Game.Combat;
using Rifles.Game.Content;
using Rifles.Game.Items;

internal static class MusketChecks
{
    internal static void Run(GameDefinitions definitions)
    {
        VerifyAuthoredBayonetTiming(definitions.Items);
        VerifyReloadManeuverAndAutoAdmission(definitions.Items);
        Require(definitions.Items.StartingItems.Where(grant => grant.Owner == ItemInventory.PartyKey || ItemInventory.IsMember(grant.Owner))
            .All(grant => grant.Definition != definitions.Combat.AmmunitionItem),
            "Player starting kits do not carry ordinary ammunition stacks.");
        Console.WriteLine("Musket checks passed: authored bayonet actions and manual/automatic reload admission.");
    }

    private static void VerifyAuthoredBayonetTiming(ItemDefinitions definitions)
    {
        BayonetDefinition bayonet = definitions.Item("rifle").Weapon!.Bayonet!;
        Require(bayonet.Fix.WindupSeconds > 0 && bayonet.Fix.RecoverySeconds > 0
            && bayonet.Unfix.WindupSeconds > 0 && bayonet.Unfix.RecoverySeconds > 0,
            "Fixing and removing a bayonet use authored positive timings.");
        RequireRejected(() => (bayonet with { Fix = new(0, bayonet.Fix.RecoverySeconds) }).Validate(),
            "Invalid bayonet windup timing is rejected at content admission.");

        ActionSnapshot fixedAction = new(CombatActionKind.FixBayonet, 19, null, null, 0, null,
            bayonet.Fix.WindupSeconds, ActionPhase.Windup, bayonet.Fix.RecoverySeconds);
        Require(ActionState.Restore(fixedAction).Capture() == fixedAction,
            "Item-authored bayonet actions persist without requiring a global combat action profile.");
    }

    private static void VerifyReloadManeuverAndAutoAdmission(ItemDefinitions definitions)
    {
        ItemInventory inventory = new(definitions,
        [new PackOwner(1, "member:warden", definitions.Backpack.Mass, definitions.Backpack.Space)]);
        ulong next = 19;
        inventory.Grant(new MemberOwner("warden"), "rifle", 1, () => next++);
        CarriedItem rifle = inventory.Items("member:warden").Single();
        WeaponState weapons = WeaponState.Create(definitions, inventory);
        WeaponCapabilities unloaded = weapons.Capabilities(rifle.Entity, inventory);
        ActionSnapshot reloadWindup = new(CombatActionKind.Reload, rifle.Entity, null, null, 0, null,
            unloaded.ReloadSeconds, ActionPhase.Windup, .35);
        ActionSnapshot reloadRecovery = reloadWindup with { Phase = ActionPhase.Recovery, Remaining = .35 };

        Require(MusketActionRules.CanInterruptReload(reloadWindup) && !MusketActionRules.CanInterruptReload(reloadRecovery),
            "Only an unfinished reload windup may be discarded for a maneuver.");
        ActionState reloading = ActionState.Restore(reloadWindup);
        Require(MusketActionRules.CanInterruptReload(reloading.Current) && reloading.Capture() == reloadWindup,
            "Readiness inspection leaves an interruptible reload windup intact until an order actually starts.");
        Require(MusketActionRules.CanBeginReload(true, true, true, false, unloaded)
            && !MusketActionRules.CanBeginReload(true, false, true, false, unloaded)
            && !MusketActionRules.CanBeginReload(true, true, false, false, unloaded)
            && !MusketActionRules.CanBeginReload(true, true, true, true, unloaded),
            "The manual reload order starts only an equipped idle unloaded musket, never a busy, dropped, or repositioning soldier.");
        Require(MusketActionRules.CanAutoReload(true, true, true, false, unloaded),
            "Automatic reload uses the same safe admission as the manual party order.");
        weapons.SetLoaded(rifle.Entity, inventory, true);
        Require(!MusketActionRules.CanBeginReload(true, true, true, false, weapons.Capabilities(rifle.Entity, inventory)),
            "Loaded muskets never begin a duplicate manual or automatic reload.");
    }

    private static void RequireRejected(Action action, string message)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
