using Rifles.Game.Content;
using Rifles.Game.Items;

internal static class CombatInventoryChecks
{
    internal static void Run(GameDefinitions definitions)
    {
        ItemInventory inventory = new(definitions.Items,
        [new PackOwner(1, "member:warden", definitions.Items.Backpack.Mass, definitions.Items.Backpack.Space)]);
        PackOwner flight = new(2, "combat:flight:1", definitions.Items.Container.Mass, definitions.Items.Container.Space);
        inventory.RegisterOwner(flight);
        RequireRejected(() => inventory.RegisterOwner(flight), "Duplicate combat owner registration is rejected.");

        ulong itemId = 100;
        inventory.Grant(InventoryOwner.Parse("member:warden"), "rifle", 1, () => itemId++);
        string rifleToken = inventory.Items("member:warden").Single(item => item.Definition == "rifle").Token;
        ulong rifleId = inventory.Find("member:warden", rifleToken).Entity;
        inventory.Equip(ItemRef.Parse("member:warden", rifleToken), "main-hand", long.MaxValue, inventory.Revision, new MemberOwner("warden"));
        inventory.Transfer(ItemRef.Parse("member:warden", rifleToken), InventoryOwner.Parse(flight.Key), 1, inventory.Revision);

        Require(inventory.Items("member:warden").All(item => item.Entity != rifleId), "Transferred weapon leaves the member pack.");
        CarriedItem flyingRifle = inventory.Items(flight.Key).Single(item => item.Entity == rifleId);
        Require(flyingRifle.Definition == "rifle" && flyingRifle.Slots.Length == 0,
            "A weapon keeps its Engine identity and is unequipped when transferred to combat flight.");

        inventory.Grant(InventoryOwner.Parse(flight.Key), "shot", 4, () => itemId++);
        inventory.Grant(InventoryOwner.Parse(flight.Key), "tonic", 2, () => itemId++);
        Require(ItemQuantity(inventory, flight.Key, "shot") == 4 && ItemQuantity(inventory, flight.Key, "tonic") == 2,
            "Combat flight owners admit multiple fungible item kinds.");
        inventory.Consume(InventoryOwner.Parse(flight.Key), "shot", 3);
        Require(ItemQuantity(inventory, flight.Key, "shot") == 1, "Ammo consumption settles against the Engine item stack.");
        string beforeOverdraw = Describe(inventory);
        RequireRejected(() => inventory.Consume(InventoryOwner.Parse(flight.Key), "shot", 2), "Ammo consumption cannot overdraw the flight stack.");
        Require(Describe(inventory) == beforeOverdraw, "Rejected ammo consumption leaves the combat inventory unchanged.");

        InventorySnapshot saved = inventory.Capture();
        ItemInventory restored = ItemInventory.Restore(definitions.Items, saved);
        Require(restored.Owner(flight.Key) == flight && Describe(restored) == Describe(inventory),
            "Combat flight owners and their exact contents survive inventory restoration.");
    }

    private static ulong ItemQuantity(ItemInventory inventory, string owner, string definition) => inventory.Items(owner)
        .Where(item => item.Definition == definition).Aggregate(0UL, (total, item) => checked(total + item.Quantity));

    private static string Describe(ItemInventory inventory) => string.Join("|", inventory.Owners.OrderBy(owner => owner.Key, StringComparer.Ordinal)
        .Select(owner => owner.Key + ":" + string.Join(",", inventory.Items(owner.Key).OrderBy(item => item.Token, StringComparer.Ordinal)
            .Select(item => $"{item.Token}:{item.Definition}:{item.Quantity}:{string.Join('+', item.Slots.OrderBy(slot => slot, StringComparer.Ordinal))}"))));

    private static void RequireRejected(Action action, string message)
    {
        try
        {
            action();
        }
        catch (Exception) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
