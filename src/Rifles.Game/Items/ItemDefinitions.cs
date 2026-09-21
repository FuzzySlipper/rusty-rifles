using Rifles.Game.Content;
using Rusty.Engine.Mechanics;

namespace Rifles.Game.Items;

internal enum ItemUse { None, Vitality, Resource, Key }
internal sealed record BayonetDefinition(string Reach, long MeleeDamage, float AccuracyMultiplier, float ReloadSecondsMultiplier, bool ChargeEligible)
{
    internal void Validate()
    {
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(Reach) && MeleeDamage > 0 && float.IsFinite(AccuracyMultiplier) && AccuracyMultiplier is > 0 and <= 1
            && float.IsFinite(ReloadSecondsMultiplier) && ReloadSecondsMultiplier >= 1, "bayonet modifier");
    }
}

/// <summary>Concrete martial capability owned by one unique gear definition.</summary>
internal sealed record MartialWeaponDefinition(string Reach, long MeleeDamage, long FireDamage, double WindupSeconds,
    double RecoverySeconds, float Accuracy, bool ChargeEligible, double ReloadSeconds, BayonetDefinition? Bayonet)
{
    internal bool IsMusket => FireDamage > 0;

    internal void Validate()
    {
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(Reach) && MeleeDamage >= 0 && FireDamage >= 0
            && (MeleeDamage > 0 || FireDamage > 0) && double.IsFinite(WindupSeconds) && WindupSeconds > 0
            && double.IsFinite(RecoverySeconds) && RecoverySeconds > 0 && float.IsFinite(Accuracy) && Accuracy is > 0 and <= 1
            && double.IsFinite(ReloadSeconds) && ReloadSeconds >= 0, "martial weapon capability");
        GameDefinitions.Require(IsMusket == (Bayonet is not null) && (IsMusket ? ReloadSeconds > 0 : ReloadSeconds == 0), "martial weapon musket fields");
        Bayonet?.Validate();
    }
}

internal sealed record GearDefinition(string Id, string Name, ItemKind Kind, ulong MaximumQuantity,
    ulong Mass, ulong Space, string[] Slots, long MinimumPower, long Power, long Defense,
    string Ammunition, ItemUse Use, long Effect, ulong Cost, string Image, MartialWeaponDefinition? Weapon = null)
{
    internal ItemDefinition Mechanical => new(ItemDefinitionId.Parse(Id), Kind, MaximumQuantity,
        Slots.Length == 0 ? [] : [ItemClassificationId.Parse("gear")],
        [new(ItemInventory.MassMetric, Mass), new(ItemInventory.SpaceMetric, Space)],
        Slots.Length == 0 ? null : new ItemEquipmentPolicy(checked((ushort)Slots.Length)));
}
internal sealed record PackDefinition(ulong Mass, ulong Space);
internal sealed record StartingItem(string Owner, string Definition, ulong Quantity, bool Equipped, string? Preset = null);
internal sealed record ItemDefinitions(PackDefinition Backpack, PackDefinition Container, PackDefinition Anchor,
    string[] EquipmentSlots, GearDefinition[] Items, StartingItem[] StartingItems, PackDefinition Party, int PartySlots)
{
    internal GearDefinition Item(string id) => Items.SingleOrDefault(i => i.Id == id)
        ?? throw new InvalidDataException("Unknown item: " + id);
    internal void Validate()
    {
        GameDefinitions.Require(Backpack.Mass > 0 && Backpack.Space > 0 && Container.Mass > 0 && Container.Space > 0
            && Anchor.Mass > 0 && Anchor.Space > 0 && Party.Mass > 0 && Party.Space > 0 && PartySlots > 0, "inventory capacities");
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
            GameDefinitions.Require(item.Weapon is null || item.Kind == ItemKind.Unique && item.Slots.SequenceEqual(["weapon"])
                && item.Use == ItemUse.None && (item.Weapon.IsMusket == !string.IsNullOrWhiteSpace(item.Ammunition)), "martial weapon item " + item.Id);
            item.Weapon?.Validate();
            _ = item.Mechanical;
        }
        foreach (StartingItem grant in StartingItems)
            GameDefinitions.Require(grant.Quantity > 0 && grant.Quantity <= Item(grant.Definition).MaximumQuantity
                && (!grant.Equipped || Item(grant.Definition).Slots.Length > 0), "starting item " + grant.Definition);
    }
}
