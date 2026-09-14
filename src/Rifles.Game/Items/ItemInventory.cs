using Rifles.Game.Content;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;

namespace Rifles.Game.Items;

internal sealed record PackOwner(ulong Id, string Key, ulong MassCapacity, ulong SpaceCapacity, string? Label = null);
internal sealed record SavedStack(string Definition, ulong Quantity);
internal sealed record SavedItem(ulong Id, string Definition);
internal sealed record SavedEquipment(ulong Item, string[] Slots);
internal sealed record SavedPack(PackOwner Owner, SavedStack[] Stacks, SavedItem[] Items, SavedEquipment[] Equipment);
internal sealed record InventorySnapshot(SavedPack[] Packs);
internal sealed record CarriedItem(string Token, ulong Entity, string Definition, ulong Quantity, string[] Slots);

/// <summary>Rifles policy over one Engine inventory world. Views and saves are copies, never another live ledger.</summary>
internal sealed class ItemInventory
{
    internal static readonly CapacityMetricId MassMetric = CapacityMetricId.Parse("mass");
    internal static readonly CapacityMetricId SpaceMetric = CapacityMetricId.Parse("space");
    private readonly InventoryWorld world = new();
    private readonly ItemDefinitions definitions;
    private readonly Dictionary<string, PackOwner> owners;
    internal ulong Revision => world.Revision;
    internal IReadOnlyCollection<PackOwner> Owners => owners.Values;
    internal ItemDefinitions Definitions => definitions;
    internal ItemInventory(ItemDefinitions definitions, IEnumerable<PackOwner> owners)
    {
        this.definitions = definitions;
        this.owners = new Dictionary<string, PackOwner>(StringComparer.Ordinal);
        foreach (PackOwner owner in owners) RegisterOwner(owner);
    }
    internal static bool IsMember(string key) => key.StartsWith("member:", StringComparison.Ordinal);
    private static bool IsCombatOwner(string key) => key.StartsWith("combat:", StringComparison.Ordinal);
    internal void RegisterOwner(PackOwner owner)
    {
        GameDefinitions.Require(owner.Id > 0 && !string.IsNullOrWhiteSpace(owner.Key)
            && owner.MassCapacity > 0 && owner.SpaceCapacity > 0
            && !owners.ContainsKey(owner.Key) && owners.Values.All(existing => existing.Id != owner.Id), "pack owner");
        world.RegisterInventory(new InventoryState(new(owner.Id), [new(MassMetric, owner.MassCapacity), new(SpaceMetric, owner.SpaceCapacity)]));
        if (IsMember(owner.Key)) world.RegisterEquipment(new EquipmentState(new(owner.Id)));
        owners.Add(owner.Key, owner);
    }
    internal PackOwner Owner(string key) => owners.TryGetValue(key, out PackOwner? value) ? value : throw new InvalidDataException("Pack unavailable.");
    internal InventoryView View(string owner) => world.View(new(Owner(owner).Id));
    internal IReadOnlyList<CarriedItem> Items(string key)
    {
        InventoryView view = View(key);
        EquipmentAssignment[] equipped = world.TryGetEquipment(view.Owner, out EquipmentState? equipment) ? equipment!.Assignments.ToArray() : [];
        return view.Stacks.Select(s => new CarriedItem("s:" + s.Definition.Value, 0, s.Definition.Value, s.Quantity, []))
            .Concat(view.UniqueItems.Select(i => new CarriedItem("i:" + i.Entity.Value, i.Entity.Value, i.Definition.Value, 1,
                equipped.Where(e => e.Item == i.Entity).Select(e => e.Slot.Value).ToArray()))).ToArray();
    }
    internal CarriedItem Find(string owner, string token) => Items(owner).SingleOrDefault(i => i.Token == token)
        ?? throw new InvalidDataException("That item is no longer in this pack.");
    internal ulong Mass(string owner) => View(owner).Capacity.Single(c => c.Metric == MassMetric).Used;
    private EquipmentSlotDefinition[] Slots(IEnumerable<string> slots) => slots.Select(s =>
        new EquipmentSlotDefinition(EquipmentSlotId.Parse(s), [ItemClassificationId.Parse("gear")])).ToArray();
    internal void GrantStarting(Func<ulong> allocate, string? preset = null)
    {
        InventoryWorldCandidate candidate = world.Prepare();
        foreach (StartingItem grant in definitions.StartingItems.Where(g => g.Preset is null || g.Preset == preset))
        {
            GearDefinition definition = definitions.Item(grant.Definition);
            EntityId owner = new(Owner(grant.Owner).Id);
            if (definition.Kind == ItemKind.Fungible) candidate.Grant(owner, definition.Mechanical, grant.Quantity);
            else
            {
                EntityId id = new(allocate());
                candidate.MaterializeUnique(new ItemState(id, definition.Mechanical), owner);
                if (grant.Equipped) candidate.Equip(owner, id, Slots(definition.Slots));
            }
        }
        candidate.Publish();
    }
    internal void Grant(string owner, string definition, ulong quantity, Func<ulong> allocate)
    {
        GearDefinition item = definitions.Item(definition);
        if (quantity == 0 || quantity > item.MaximumQuantity) throw new InvalidDataException("Choose an available quantity.");
        EntityId destination = new(Owner(owner).Id);
        InventoryWorldCandidate candidate = world.Prepare();
        if (item.Kind == ItemKind.Fungible) candidate.Grant(destination, item.Mechanical, quantity);
        else
        {
            for (ulong count = 0; count < quantity; count++)
                candidate.MaterializeUnique(new ItemState(new(allocate()), item.Mechanical), destination);
        }
        candidate.Publish();
    }
    internal void Consume(string owner, string definition, ulong quantity)
    {
        GearDefinition item = definitions.Item(definition);
        if (item.Kind != ItemKind.Fungible || quantity == 0) throw new InvalidDataException("Choose available ammunition.");
        InventoryWorldCandidate candidate = world.Prepare();
        candidate.Consume(new(Owner(owner).Id), item.Mechanical, quantity);
        candidate.Publish();
    }
    internal void Transfer(string from, string to, string token, ulong quantity, ulong expectedRevision)
    {
        if (from == to) throw new InvalidDataException("Choose a different destination.");
        CarriedItem item = Find(from, token);
        if (quantity == 0 || quantity > item.Quantity) throw new InvalidDataException("Choose an available quantity.");
        // A floor quadrant/alcove/plate holds one item kind, with stack merging permitted.
        if (!IsMember(to) && to != "crate" && !IsCombatOwner(to))
        {
            IReadOnlyList<CarriedItem> existing = Items(to);
            if (existing.Any(i => i.Entity != 0 || item.Entity != 0 || i.Definition != item.Definition))
                throw new InvalidDataException("That anchor is occupied.");
        }
        InventoryWorldCandidate candidate = world.Prepare(expectedRevision);
        EntityId source = new(Owner(from).Id), destination = new(Owner(to).Id);
        if (item.Entity == 0) candidate.TransferFungible(source, destination, definitions.Item(item.Definition).Mechanical, quantity);
        else
        {
            EntityId id = new(item.Entity);
            if (item.Slots.Length > 0) candidate.Unequip(source, id);
            candidate.TransferUnique(id, source, destination);
        }
        candidate.Publish();
    }
    internal void Equip(string owner, string token, string slot, long basePower, ulong expectedRevision, string? destination = null)
    {
        destination ??= owner;
        if (!IsMember(destination)) throw new InvalidDataException("Only characters can equip gear.");
        CarriedItem item = Find(owner, token);
        GearDefinition definition = definitions.Item(item.Definition);
        if (!definition.Slots.Contains(slot)) throw new InvalidDataException("This item cannot use that slot.");
        if (basePower < definition.MinimumPower) throw new InvalidDataException("This character needs more power to use that gear.");
        EntityId id = new(Owner(destination).Id);
        InventoryWorldCandidate candidate = world.Prepare(expectedRevision);
        if (owner != destination)
        {
            if (item.Slots.Length > 0) candidate.Unequip(new(Owner(owner).Id), new(item.Entity));
            candidate.TransferUnique(new(item.Entity), new(Owner(owner).Id), id);
        }
        foreach (CarriedItem displaced in Items(destination).Where(i => i.Slots.Intersect(definition.Slots).Any()))
            candidate.Unequip(id, new(displaced.Entity));
        candidate.Equip(id, new(item.Entity), Slots(definition.Slots));
        candidate.Publish();
    }
    internal void Unequip(string owner, string token, ulong expectedRevision)
    {
        CarriedItem item = Find(owner, token);
        if (item.Slots.Length == 0) throw new InvalidDataException("That item is not equipped.");
        InventoryWorldCandidate candidate = world.Prepare(expectedRevision);
        candidate.Unequip(new(Owner(owner).Id), new(item.Entity));
        candidate.Publish();
    }
    internal InventoryWorldCandidate PrepareUse(string owner, string token, ulong expectedRevision)
    {
        CarriedItem item = Find(owner, token);
        GearDefinition definition = definitions.Item(item.Definition);
        InventoryWorldCandidate candidate = world.Prepare(expectedRevision);
        if (definition.Cost > 0)
        {
            if (item.Entity == 0) candidate.Consume(new(Owner(owner).Id), definition.Mechanical, definition.Cost);
            else if (definition.Cost == 1) candidate.DestroyUnique(new(item.Entity));
            else throw new InvalidDataException("Invalid unique item cost.");
        }
        candidate.Validate();
        return candidate;
    }
    internal void Destroy(string owner, string token)
    {
        CarriedItem item = Find(owner, token);
        InventoryWorldCandidate candidate = world.Prepare();
        if (item.Entity == 0) candidate.Consume(new(Owner(owner).Id), definitions.Item(item.Definition).Mechanical, item.Quantity);
        else candidate.DestroyUnique(new(item.Entity));
        candidate.Publish();
    }
    internal (long Power, long Defense) Bonuses(string owner)
    {
        GearDefinition[] equipped = Items(owner).Where(i => i.Slots.Length > 0).Select(i => definitions.Item(i.Definition)).ToArray();
        return (equipped.Sum(i => i.Power), equipped.Sum(i => i.Defense));
    }
    internal InventorySnapshot Capture() => new(Owners.Select(owner =>
    {
        IReadOnlyList<CarriedItem> contents = Items(owner.Key);
        return new SavedPack(owner,
            contents.Where(i => i.Entity == 0).Select(i => new SavedStack(i.Definition, i.Quantity)).ToArray(),
            contents.Where(i => i.Entity != 0).Select(i => new SavedItem(i.Entity, i.Definition)).ToArray(),
            contents.Where(i => i.Slots.Length > 0).Select(i => new SavedEquipment(i.Entity, i.Slots)).ToArray());
    }).ToArray());
    internal static ItemInventory Restore(ItemDefinitions definitions, InventorySnapshot snapshot)
    {
        ItemInventory result = new(definitions, snapshot.Packs.Select(p => p.Owner));
        InventoryWorldCandidate candidate = result.world.Prepare();
        foreach (SavedPack pack in snapshot.Packs)
        {
            EntityId owner = new(pack.Owner.Id);
            GameDefinitions.Require(pack.Stacks.Select(s => s.Definition).Distinct().Count() == pack.Stacks.Length, "saved stack uniqueness");
            foreach (SavedStack stack in pack.Stacks) candidate.Grant(owner, definitions.Item(stack.Definition).Mechanical, stack.Quantity);
            foreach (SavedItem item in pack.Items) candidate.MaterializeUnique(new ItemState(new(item.Id), definitions.Item(item.Definition).Mechanical), owner);
            foreach (SavedEquipment equipment in pack.Equipment)
            {
                SavedItem item = pack.Items.Single(i => i.Id == equipment.Item);
                GameDefinitions.Require(equipment.Slots.ToHashSet().SetEquals(definitions.Item(item.Definition).Slots), "saved equipment slots");
                candidate.Equip(owner, new(equipment.Item), result.Slots(equipment.Slots));
            }
            if (!IsMember(pack.Owner.Key) && pack.Owner.Key != "crate" && !IsCombatOwner(pack.Owner.Key))
                GameDefinitions.Require(pack.Items.Length + pack.Stacks.Length <= 1, "saved anchor occupancy");
        }
        candidate.Publish();
        return result;
    }
}
