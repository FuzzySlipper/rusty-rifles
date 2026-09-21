using Rifles.Game.Content;
using Rifles.Game.Items;

internal static class WeaponChecks
{
    internal static void Run(GameDefinitions definitions)
    {
        VerifySlotAdmission(definitions.Items);
        VerifyMusketIdentityAndCapabilities(definitions.Items);
        Console.WriteLine("Weapon checks passed: martial slots, capability tuning, and unique musket state.");
    }

    private static void VerifySlotAdmission(ItemDefinitions definitions)
    {
        Require(definitions.EquipmentSlots.SequenceEqual(["weapon", "outfit", "accessory"]),
            "The active equipment contract has weapon, outfit, and accessory slots.");
        Require(definitions.Item("knife").Weapon?.Reach == "short-sword" && definitions.Item("knife").Defense > 0,
            "Short sword and shield are one weapon loadout, not paired attachments.");
        Require(definitions.Item("sword").Weapon?.Reach == "sword" && definitions.Item("pike").Weapon?.Reach == "pike"
            && definitions.Item("rifle").Weapon?.Reach == "musket", "Each required martial weapon authors its M01 reach profile.");
        Require(definitions.Item("rifle").Weapon?.Bayonet?.Reach == "fixed-bayonet", "The musket carries its compatible bayonet definition.");

        ItemInventory inventory = NewInventory(definitions);
        ulong id = 10;
        inventory.Grant(new PartyOwner(), "rifle", 1, () => id++);
        string rifle = inventory.Items(ItemInventory.PartyKey).Single(item => item.Definition == "rifle").Token;
        RequireRejected(() => inventory.Equip(ItemRef.Parse(ItemInventory.PartyKey, rifle), "outfit", long.MaxValue, inventory.Revision, new MemberOwner("warden")),
            "A musket cannot enter a non-weapon slot.");
        inventory.Equip(ItemRef.Parse(ItemInventory.PartyKey, rifle), "weapon", long.MaxValue, inventory.Revision, new MemberOwner("warden"));
        Require(inventory.Items("member:warden").Single().Slots.SequenceEqual(["weapon"]), "Engine equipment admits the concrete weapon slot.");
    }

    private static void VerifyMusketIdentityAndCapabilities(ItemDefinitions definitions)
    {
        ItemInventory inventory = NewInventory(definitions);
        ulong id = 40;
        inventory.Grant(new PartyOwner(), "rifle", 1, () => id++);
        CarriedItem rifle = inventory.Items(ItemInventory.PartyKey).Single(item => item.Definition == "rifle");
        WeaponState weapons = WeaponState.Create(definitions, inventory);
        WeaponCapabilities baseCapabilities = weapons.Capabilities(rifle.Entity, inventory);
        Require(!baseCapabilities.Loaded && !baseCapabilities.BayonetFixed && baseCapabilities.MeleeReach is null
            && baseCapabilities.FireReach == "musket", "New muskets start unloaded and unfixed with their authored fire reach.");

        weapons.SetLoaded(rifle.Entity, inventory, true);
        weapons.SetBayonetFixed(rifle.Entity, inventory, true);
        WeaponCapabilities fixedCapabilities = weapons.Capabilities(rifle.Entity, inventory);
        Require(fixedCapabilities.Loaded && fixedCapabilities.BayonetFixed && fixedCapabilities.MeleeReach == "fixed-bayonet"
            && fixedCapabilities.MeleeDamage > 0 && fixedCapabilities.ChargeEligible
            && fixedCapabilities.Accuracy < baseCapabilities.Accuracy && fixedCapabilities.ReloadSeconds > baseCapabilities.ReloadSeconds,
            "Fixing a bayonet changes melee/charge capability and applies the authored accuracy and reload penalties.");
        _ = weapons.RollFireHit(rifle.Entity, inventory, 77);

        inventory.Transfer(ItemRef.Parse(ItemInventory.PartyKey, rifle.Token), new AnchorOwner("crate"), 1, inventory.Revision);
        Require(weapons.Capabilities(rifle.Entity, inventory).Loaded && weapons.Capabilities(rifle.Entity, inventory).BayonetFixed,
            "A dropped or transferred musket keeps state by its Engine unique-item identity.");
        WeaponStateSnapshot saved = weapons.Capture(inventory);
        WeaponState restored = WeaponState.Restore(definitions, saved, inventory);
        Require(restored.Capabilities(rifle.Entity, inventory) == fixedCapabilities && saved.Muskets.Single().ShotCount == 1,
            "Save restoration preserves the same musket state and independent shot sequence without copying it.");

        inventory.Destroy(ItemRef.Parse("crate", rifle.Token));
        Require(restored.Capture(inventory).Muskets.Length == 0, "Destroyed muskets retire their state rather than leaving a stale duplicate.");
    }

    private static ItemInventory NewInventory(ItemDefinitions definitions) => new(definitions,
    [
        new PackOwner(1, "member:warden", definitions.Backpack.Mass, definitions.Backpack.Space),
        new PackOwner(2, ItemInventory.PartyKey, definitions.Party.Mass, definitions.Party.Space),
        new PackOwner(3, "crate", definitions.Container.Mass, definitions.Container.Space),
    ]);

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
