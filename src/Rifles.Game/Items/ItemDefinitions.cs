using Rifles.Game.Content;
using Rusty.Engine.Mechanics;

namespace Rifles.Game.Items;

internal enum ItemUse { None, Vitality, Resource, Key }
internal sealed record GearDefinition(string Id, string Name, ItemKind Kind, ulong MaximumQuantity,
    ulong Mass, ulong Space, string[] Slots, long MinimumPower, long Power, long Defense,
    string Ammunition, ItemUse Use, long Effect, ulong Cost, string Image)
{
    internal ItemDefinition Mechanical => new(ItemDefinitionId.Parse(Id), Kind, MaximumQuantity,
        Slots.Length == 0 ? [] : [ItemClassificationId.Parse("gear")],
        [new(ItemInventory.MassMetric, Mass), new(ItemInventory.SpaceMetric, Space)],
        Slots.Length == 0 ? null : new ItemEquipmentPolicy(checked((ushort)Slots.Length)));
}
internal sealed record PackDefinition(ulong Mass, ulong Space);
internal sealed record StartingItem(string Owner, string Definition, ulong Quantity, bool Equipped, string? Preset = null);
internal sealed record ItemDefinitions(PackDefinition Backpack, PackDefinition Container, PackDefinition Anchor,
    string[] EquipmentSlots, GearDefinition[] Items, StartingItem[] StartingItems)
{
    internal GearDefinition Item(string id) => Items.SingleOrDefault(i => i.Id == id)
        ?? throw new InvalidDataException("Unknown item: " + id);
    internal void Validate()
    {
        GameDefinitions.Require(Backpack.Mass > 0 && Backpack.Space > 0 && Container.Mass > 0 && Container.Space > 0
            && Anchor.Mass > 0 && Anchor.Space > 0, "inventory capacities");
        GameDefinitions.Require(EquipmentSlots.Length > 0 && EquipmentSlots.Distinct().Count() == EquipmentSlots.Length, "equipment slots");
        GameDefinitions.Require(Items.Length > 0 && Items.Select(i => i.Id).Distinct().Count() == Items.Length, "item identities");
        foreach (GearDefinition item in Items)
        {
            GameDefinitions.Require(!string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.Image)
                && item.Mass > 0 && item.Space > 0 && item.MinimumPower >= 0 && item.Power is >= 0 and <= 1000
                && item.Defense is >= 0 and <= 1000 && item.Effect is >= 0 and <= 1000 && Enum.IsDefined(item.Use)
                && item.Slots.Distinct().Count() == item.Slots.Length && item.Slots.All(EquipmentSlots.Contains)
                && (item.Slots.Length == 0 || item.Kind == ItemKind.Unique)
                && (item.Use is not (ItemUse.Vitality or ItemUse.Resource) || item.Cost > 0 && item.Effect > 0 && item.Kind == ItemKind.Fungible), "item " + item.Id);
            _ = item.Mechanical;
        }
        foreach (StartingItem grant in StartingItems)
            GameDefinitions.Require(grant.Quantity > 0 && grant.Quantity <= Item(grant.Definition).MaximumQuantity
                && (!grant.Equipped || Item(grant.Definition).Slots.Length > 0), "starting item " + grant.Definition);
    }
}
