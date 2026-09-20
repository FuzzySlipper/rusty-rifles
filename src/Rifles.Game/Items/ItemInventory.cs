using Rifles.Game.Characters;
using Rifles.Game.Content;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;

namespace Rifles.Game.Items;

internal sealed record PackOwner(ulong Id, string Key, ulong MassCapacity, ulong SpaceCapacity, string? Label = null);
internal sealed record SavedStack(string Definition, ulong Quantity);
internal sealed record SavedItem(ulong Id, string Definition);
internal sealed record SavedEquipment(ulong Item, string[] Slots);
internal sealed record SavedPack(PackOwner Owner, SavedStack[] Stacks, SavedItem[] Items, SavedEquipment[] Equipment, SavedSlot[] Slots);
internal sealed record SavedSlot(string Token, int Slot);
internal sealed record InventorySnapshot(SavedPack[] Packs);
internal sealed record CarriedItem(string Token, ulong Entity, string Definition, ulong Quantity, string[] Slots);

/// <summary>Rifles policy over one Engine inventory world. Views and saves are copies, never another live ledger.</summary>
internal sealed class ItemInventory
{
    internal static readonly CapacityMetricId MassMetric = CapacityMetricId.Parse("mass");
    internal static readonly CapacityMetricId SpaceMetric = CapacityMetricId.Parse("space");
    private readonly InventoryStore world = new();
    private readonly ItemDefinitions definitions;
    private readonly Dictionary<string, PackOwner> owners;
    // Grid slots for the shared party pack only: token ("s:<def>" or
    // "i:<entity>") to slot index. Anchors, drops, and member packs have no
    // positions; equipped items live in equipment state, not in slots.
    private readonly Dictionary<string, int> slots = new(StringComparer.Ordinal);
    internal const string PartyKey = "party";
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
    internal static bool IsParty(string key) => key == PartyKey;
    internal int SlotOf(string token) => slots.TryGetValue(token, out int slot) ? slot : -1;
    // Lowest free grid slot, or -1 when the party inventory is full.
    private int FreeSlot()
    {
        HashSet<int> taken = new(slots.Values);
        for (int slot = 0; slot < definitions.PartySlots; slot++)
            if (!taken.Contains(slot)) return slot;
        return -1;
    }
    private void TakeSlot(string token)
    {
        if (slots.ContainsKey(token)) return;
        int free = FreeSlot();
        if (free < 0) throw new InvalidDataException("The party inventory is full.");
        slots[token] = free;
    }
    // Slots for tokens that left the party pack (equipped, moved, consumed,
    // destroyed) retire; anything else is a corrupt map, never silently kept.
    private void SyncSlots()
    {
        HashSet<string> live = new(Items(PartyKey).Select(i => i.Token), StringComparer.Ordinal);
        foreach (string token in slots.Keys.Where(token => !live.Contains(token)).ToArray()) slots.Remove(token);
    }
    private void RequirePartyRoom(IEnumerable<string> tokens)
    {
        int fresh = tokens.Distinct(StringComparer.Ordinal).Count(token => !slots.ContainsKey(token));
        int free = definitions.PartySlots - slots.Count;
        if (fresh > free) throw new InvalidDataException("The party inventory is full.");
    }
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

    private readonly Dictionary<string, (EntityId Ledger, InventoryComponent Inventory, EquipmentComponent Equipment, RiflesCharacter Character)> bound = new(StringComparer.Ordinal);

    /// <summary>
    /// Binds member packs to canonical character entities: owner facades attach
    /// to the entity while addressing its ledger record, and currently equipped
    /// items attach per-source contributions. Re-binding is idempotent.
    /// </summary>
    internal void BindMembers(CharacterEntities entities, IEnumerable<RiflesCharacter> members)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(members);
        foreach (RiflesCharacter member in members)
        {
            PackOwner owner = Owner("member:" + member.Definition.Id);
            (InventoryComponent inventory, EquipmentComponent equipment) =
                entities.BindInventory(member.Definition.Id, world, new(owner.Id));
            bound[member.Definition.Id] = (new(owner.Id), inventory, equipment, member);
            foreach (CarriedItem worn in Items(owner.Key).Where(i => i.Slots.Length > 0))
            {
                GearDefinition gear = definitions.Item(worn.Definition);
                member.EquipContribution(worn.Token, gear.Power, gear.Defense);
            }
        }
    }

    private bool BoundMember(string key, out (EntityId Ledger, InventoryComponent Inventory, EquipmentComponent Equipment, RiflesCharacter Character) member)
    {
        if (key.StartsWith("member:", StringComparison.Ordinal)
            && bound.TryGetValue(key["member:".Length..], out member))
            return true;
        member = default;
        return false;
    }
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
        InventoryEdit candidate = world.Prepare();
        List<string> partyTokens = [];
        List<(string Owner, string Token, long Power, long Defense)> equipped = [];
        foreach (StartingItem grant in definitions.StartingItems.Where(g => g.Preset is null || g.Preset == preset))
        {
            GearDefinition definition = definitions.Item(grant.Definition);
            EntityId owner = new(Owner(grant.Owner).Id);
            if (definition.Kind == ItemKind.Fungible)
            {
                candidate.Grant(owner, definition.Mechanical, grant.Quantity);
                if (grant.Owner == PartyKey) partyTokens.Add("s:" + definition.Id);
            }
            else
            {
                EntityId id = new(allocate());
                candidate.MaterializeUnique(new ItemState(id, definition.Mechanical), owner);
                if (grant.Equipped)
                {
                    candidate.Equip(owner, id, Slots(definition.Slots));
                    equipped.Add((grant.Owner, "i:" + id.Value, definition.Power, definition.Defense));
                }
                else if (grant.Owner == PartyKey) partyTokens.Add("i:" + id.Value);
            }
        }
        RequirePartyRoom(partyTokens);
        candidate.Publish();
        foreach (string token in partyTokens) TakeSlot(token);
        // Contributions attach only once the ledger edit publishes: a failed
        // grant must leave both the ledger and the stats untouched.
        foreach ((string owner, string token, long power, long defense) in equipped)
        {
            if (BoundMember(owner, out var member))
                member.Character.EquipContribution(token, power, defense);
        }
    }
    internal void Grant(string owner, string definition, ulong quantity, Func<ulong> allocate)
    {
        GearDefinition item = definitions.Item(definition);
        if (quantity == 0 || quantity > item.MaximumQuantity) throw new InvalidDataException("Choose an available quantity.");
        EntityId destination = new(Owner(owner).Id);
        List<string> partyTokens = [];
        InventoryEdit candidate = world.Prepare();
        if (item.Kind == ItemKind.Fungible)
        {
            candidate.Grant(destination, item.Mechanical, quantity);
            if (owner == PartyKey) partyTokens.Add("s:" + item.Id);
        }
        else
        {
            for (ulong count = 0; count < quantity; count++)
            {
                EntityId id = new(allocate());
                candidate.MaterializeUnique(new ItemState(id, item.Mechanical), destination);
                if (owner == PartyKey) partyTokens.Add("i:" + id.Value);
            }
        }
        RequirePartyRoom(partyTokens);
        candidate.Publish();
        foreach (string token in partyTokens) TakeSlot(token);
    }
    internal void Consume(string owner, string definition, ulong quantity)
    {
        GearDefinition item = definitions.Item(definition);
        if (item.Kind != ItemKind.Fungible || quantity == 0) throw new InvalidDataException("Choose available ammunition.");
        InventoryEdit candidate = world.Prepare();
        candidate.Consume(new(Owner(owner).Id), item.Mechanical, quantity);
        candidate.Publish();
        if (owner == PartyKey) SyncSlots();
    }
    internal void Transfer(string from, string to, string token, ulong quantity, ulong expectedRevision)
    {
        if (from == to) throw new InvalidDataException("Choose a different destination.");
        // Characters carry equipped gear only; loose items live in the party.
        // Equip (with its slot and power validation) is the way onto a member.
        if (IsMember(to)) throw new InvalidDataException("Characters carry only equipped gear — send it to the party instead.");
        CarriedItem item = Find(from, token);
        if (quantity == 0 || quantity > item.Quantity) throw new InvalidDataException("Choose an available quantity.");
        // A floor quadrant/alcove/plate holds one item kind, with stack merging permitted.
        // The party pack is a real multi-kind inventory and skips this rule.
        if (!IsMember(to) && !IsParty(to) && to != "crate" && !IsCombatOwner(to))
        {
            IReadOnlyList<CarriedItem> existing = Items(to);
            if (existing.Any(i => i.Entity != 0 || item.Entity != 0 || i.Definition != item.Definition))
                throw new InvalidDataException("That anchor is occupied.");
        }
        InventoryEdit candidate = world.Prepare(expectedRevision);
        EntityId source = new(Owner(from).Id), destination = new(Owner(to).Id);
        if (item.Entity == 0) candidate.TransferFungible(source, destination, definitions.Item(item.Definition).Mechanical, quantity);
        else
        {
            EntityId id = new(item.Entity);
            if (item.Slots.Length > 0) candidate.Unequip(source, id);
            candidate.TransferUnique(id, source, destination);
        }
        if (to == PartyKey) RequirePartyRoom([token]);
        candidate.Publish();
        if (to == PartyKey) TakeSlot(token);
        if (from == PartyKey) SyncSlots();
        if (item.Slots.Length > 0 && BoundMember(from, out var member)) member.Character.UnequipContribution(token);
    }
    internal void Equip(string owner, string token, string slot, long wielderPower, ulong expectedRevision, string? destination = null)
    {
        destination ??= owner;
        if (InventoryOwner.Parse(destination) is not MemberOwner) throw new InvalidDataException("Only characters can equip gear.");
        CarriedItem item = Find(owner, token);
        GearDefinition definition = definitions.Item(item.Definition);
        if (!definition.Slots.Contains(slot)) throw new InvalidDataException("This item cannot use that slot.");
        if (wielderPower < definition.MinimumPower) throw new InvalidDataException("This character needs more power to use that gear.");
        EntityId id = new(Owner(destination).Id);
        InventoryEdit candidate = world.Prepare(expectedRevision);
        if (owner != destination)
        {
            if (item.Slots.Length > 0) candidate.Unequip(new(Owner(owner).Id), new(item.Entity));
            candidate.TransferUnique(new(item.Entity), new(Owner(owner).Id), id);
        }
        // Swapping out of the party grid returns the displaced gear to the
        // grid, not into the member pack: equipping from anywhere else keeps
        // the old behavior. The incoming item vacates its own slot first.
        bool toParty = owner == PartyKey;
        List<string> displacedParty = [];
        List<string> displacedTokens = [];
        foreach (CarriedItem displaced in Items(destination).Where(i => i.Slots.Intersect(definition.Slots).Any()))
        {
            candidate.Unequip(id, new(displaced.Entity));
            displacedTokens.Add(displaced.Token);
            if (toParty)
            {
                candidate.TransferUnique(new(displaced.Entity), id, new(Owner(PartyKey).Id));
                displacedParty.Add("i:" + displaced.Entity);
            }
        }
        if (toParty)
        {
            int spare = definitions.PartySlots - slots.Count + 1;
            if (displacedParty.Distinct(StringComparer.Ordinal).Count() > spare)
                throw new InvalidDataException("The party inventory is full.");
        }
        candidate.Equip(id, new(item.Entity), Slots(definition.Slots));
        candidate.Publish();
        if (toParty)
        {
            SyncSlots();
            foreach (string placed in displacedParty) TakeSlot(placed);
        }
        else if (owner == PartyKey) SyncSlots();
        // Per-source contributions mirror the ledger: displaced gear loses its
        // own contribution and the incoming item gains its own, so unequipping
        // later removes exactly what this equip added.
        if (BoundMember(destination, out var wearer))
        {
            foreach (string displaced in displacedTokens) wearer.Character.UnequipContribution(displaced);
            wearer.Character.EquipContribution(token, definition.Power, definition.Defense);
        }
    }
    internal void Unequip(string owner, string token, ulong expectedRevision)
    {
        CarriedItem item = Find(owner, token);
        if (item.Slots.Length == 0) throw new InvalidDataException("That item is not equipped.");
        InventoryEdit candidate = world.Prepare(expectedRevision);
        candidate.Unequip(new(Owner(owner).Id), new(item.Entity));
        candidate.Publish();
        if (BoundMember(owner, out var member)) member.Character.UnequipContribution(token);
    }
    internal InventoryEdit PrepareUse(string owner, string token, ulong expectedRevision)
    {
        CarriedItem item = Find(owner, token);
        GearDefinition definition = definitions.Item(item.Definition);
        InventoryEdit candidate = world.Prepare(expectedRevision);
        if (definition.Cost > 0)
        {
            if (item.Entity == 0) candidate.Consume(new(Owner(owner).Id), definition.Mechanical, definition.Cost);
            else if (definition.Cost == 1) candidate.DestroyUnique(new(item.Entity));
            else throw new InvalidDataException("Invalid unique item cost.");
        }
        candidate.Validate();
        return candidate;
    }
    // Rearrange the party grid: move a token to a slot, swapping with any
    // occupant. Slots are C#-side presentation state (the Engine ledger has
    // no positions), so no ledger edit is needed — but the revision gate
    // still applies so stale grids cannot overwrite fresh ones.
    internal void Arrange(string token, int slot, ulong expectedRevision)
    {
        if (expectedRevision != world.Revision) throw new InvalidDataException("Inventory changed; select the item again.");
        if (slot < 0 || slot >= definitions.PartySlots) throw new InvalidDataException("Choose a slot inside the party inventory.");
        if (!Items(PartyKey).Any(i => i.Token == token)) throw new InvalidDataException("That item is no longer in the party inventory.");
        if (SlotOf(token) < 0) TakeSlot(token); // Repair path; every entry assigns, so this should not happen.
        string? occupant = slots.SingleOrDefault(pair => pair.Value == slot && pair.Key != token).Key;
        if (occupant is not null) slots[occupant] = SlotOf(token);
        slots[token] = slot;
    }
    internal void Destroy(string owner, string token)
    {
        CarriedItem item = Find(owner, token);
        InventoryEdit candidate = world.Prepare();
        if (item.Entity == 0) candidate.Consume(new(Owner(owner).Id), definitions.Item(item.Definition).Mechanical, item.Quantity);
        else candidate.DestroyUnique(new(item.Entity));
        candidate.Publish();
        if (owner == PartyKey) SyncSlots();
        if (item.Slots.Length > 0 && BoundMember(owner, out var member)) member.Character.UnequipContribution(token);
    }
    internal InventorySnapshot Capture() => new(Owners.Select(owner =>
    {
        IReadOnlyList<CarriedItem> contents = Items(owner.Key);
        return new SavedPack(owner,
            contents.Where(i => i.Entity == 0).Select(i => new SavedStack(i.Definition, i.Quantity)).ToArray(),
            contents.Where(i => i.Entity != 0).Select(i => new SavedItem(i.Entity, i.Definition)).ToArray(),
            contents.Where(i => i.Slots.Length > 0).Select(i => new SavedEquipment(i.Entity, i.Slots)).ToArray(),
            owner.Key == PartyKey ? contents.Select(i => new SavedSlot(i.Token, SlotOf(i.Token))).ToArray() : []);
    }).ToArray());
    internal static ItemInventory Restore(ItemDefinitions definitions, InventorySnapshot snapshot)
    {
        ItemInventory result = new(definitions, snapshot.Packs.Select(p => p.Owner));
        InventoryEdit candidate = result.world.Prepare();
        foreach (SavedPack pack in snapshot.Packs)
        {
            EntityId owner = new(pack.Owner.Id);
            GameDefinitions.Require(pack.Stacks.Select(s => s.Definition).Distinct().Count() == pack.Stacks.Length, "saved stack uniqueness");
            // Saves that predate the shared party inventory kept loose items
            // in member packs. They cannot load: reject loudly, never migrate
            // silently into a model the player did not choose.
            if (IsMember(pack.Owner.Key) && (pack.Stacks.Length > 0
                || pack.Items.Any(item => !pack.Equipment.Any(equipment => equipment.Item == item.Id))))
                throw new InvalidDataException("This save predates the shared party inventory; start a new expedition.");
            foreach (SavedStack stack in pack.Stacks) candidate.Grant(owner, definitions.Item(stack.Definition).Mechanical, stack.Quantity);
            foreach (SavedItem item in pack.Items) candidate.MaterializeUnique(new ItemState(new(item.Id), definitions.Item(item.Definition).Mechanical), owner);
            foreach (SavedEquipment equipment in pack.Equipment)
            {
                SavedItem item = pack.Items.Single(i => i.Id == equipment.Item);
                GameDefinitions.Require(equipment.Slots.ToHashSet().SetEquals(definitions.Item(item.Definition).Slots), "saved equipment slots");
                candidate.Equip(owner, new(equipment.Item), result.Slots(equipment.Slots));
            }
            if (pack.Owner.Key == PartyKey)
            {
                SavedSlot[] savedSlots = pack.Slots ?? [];
                GameDefinitions.Require(savedSlots.All(s => s.Slot >= 0 && s.Slot < definitions.PartySlots)
                    && savedSlots.Select(s => s.Slot).Distinct().Count() == savedSlots.Length
                    && savedSlots.Select(s => s.Token).Distinct(StringComparer.Ordinal).Count() == savedSlots.Length, "saved party slots");
                List<string> live = [
                    .. pack.Stacks.Select(s => "s:" + s.Definition),
                    .. pack.Items.Select(i => "i:" + i.Id)];
                GameDefinitions.Require(savedSlots.All(s => live.Contains(s.Token, StringComparer.Ordinal)), "saved party slot contents");
                foreach (SavedSlot saved in savedSlots) result.slots[saved.Token] = saved.Slot;
                foreach (string token in live.Where(token => result.SlotOf(token) < 0)) result.TakeSlot(token);
            }
            if (!IsMember(pack.Owner.Key) && pack.Owner.Key != "crate" && !IsCombatOwner(pack.Owner.Key) && pack.Owner.Key != PartyKey)
                GameDefinitions.Require(pack.Items.Length + pack.Stacks.Length <= 1, "saved anchor occupancy");
        }
        candidate.Publish();
        return result;
    }
}
