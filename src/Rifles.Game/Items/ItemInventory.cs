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
    internal void RegisterOwner(PackOwner owner)
    {
        GameDefinitions.Require(owner.Id > 0 && !string.IsNullOrWhiteSpace(owner.Key)
            && owner.MassCapacity > 0 && owner.SpaceCapacity > 0
            && !owners.ContainsKey(owner.Key) && owners.Values.All(existing => existing.Id != owner.Id), "pack owner");
        world.RegisterInventory(new InventoryState(new(owner.Id), [new(MassMetric, owner.MassCapacity), new(SpaceMetric, owner.SpaceCapacity)]));
        if (InventoryOwner.Parse(owner.Key) is MemberOwner) world.RegisterEquipment(new EquipmentState(new(owner.Id)));
        owners.Add(owner.Key, owner);
    }
    internal PackOwner Owner(string key) => owners.TryGetValue(key, out PackOwner? value) ? value : throw new InvalidDataException("Pack unavailable.");

    private readonly Dictionary<string, (InventoryComponent Inventory, EquipmentComponent? Equipment, RiflesCharacter? Character)> bound = new(StringComparer.Ordinal);

    /// <summary>
    /// Binds member and party packs to canonical entities: owner facades attach
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
            (InventoryComponent inventory, EquipmentComponent? equipment) =
                entities.BindInventory(member.Definition.Id, world, new(owner.Id));
            bound[owner.Key] = (inventory, equipment, member);
            foreach (CarriedItem worn in Items(owner.Key).Where(i => i.Slots.Length > 0))
            {
                GearDefinition gear = definitions.Item(worn.Definition);
                member.EquipContribution(worn.Token, gear.Power, gear.Defense);
            }
        }

        PackOwner party = Owner(PartyKey);
        bound[party.Key] = (entities.BindParty(world, new(party.Id)), null, null);
    }

    /// <summary>
    /// Binds every remaining registered pack (anchors, enemy packs, flights) to
    /// an entity facade: enemy packs attach to their live enemy entity, the
    /// rest get container entities keyed by pack key. Called once floor
    /// assembly has registered all packs; re-binding is idempotent.
    /// </summary>
    internal void BindRemaining(CharacterEntities entities)
    {
        ArgumentNullException.ThrowIfNull(entities);
        foreach (PackOwner owner in Owners)
        {
            if (bound.ContainsKey(owner.Key)) continue;
            EntityId ledger = new(owner.Id);
            InventoryComponent facade;
            if (InventoryOwner.Parse(owner.Key) is CombatOwner combat
                && combat.EnemyPackId is { } enemyPack
                && entities.TryGetEntity("enemy:" + enemyPack, out _))
            {
                (facade, _) = entities.BindInventory("enemy:" + enemyPack, world, ledger, equipment: false);
            }
            else
            {
                facade = entities.BindContainer(owner.Key, world, ledger);
            }

            bound[owner.Key] = (facade, null, null);
        }
    }

    internal bool IsBound(string key) => bound.ContainsKey(key);

    private bool BoundMember(string key, out RiflesCharacter member)
    {
        if (InventoryOwner.Parse(key) is MemberOwner
            && bound.TryGetValue(key, out var entry)
            && entry.Character is { } character)
        {
            member = character;
            return true;
        }

        member = null!;
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
    internal void GrantStarting(Func<ulong> allocate, string? preset = null, Func<string, bool>? includeOwner = null)
    {
        InventoryEdit candidate = world.Prepare();
        List<string> partyTokens = [];
        List<(string Owner, string Token, long Power, long Defense)> equipped = [];
        foreach (StartingItem grant in definitions.StartingItems.Where(g => g.Preset is null || g.Preset == preset))
        {
            // Travelling kits restore instead of granting again; floor-local
            // owners always grant. The re-granted member kit on a fresh travel
            // floor would otherwise duplicate the travelled equipment.
            if (includeOwner is not null && !includeOwner(grant.Owner)) continue;
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
                member.EquipContribution(token, power, defense);
        }
    }
    internal void Grant(InventoryOwner owner, string definition, ulong quantity, Func<ulong> allocate)
    {
        GearDefinition item = definitions.Item(definition);
        if (quantity == 0 || quantity > item.MaximumQuantity) throw new InvalidDataException("Choose an available quantity.");
        EntityId destination = new(Owner(owner.Key).Id);
        List<string> partyTokens = [];
        InventoryEdit candidate = world.Prepare();
        if (item.Kind == ItemKind.Fungible)
        {
            candidate.Grant(destination, item.Mechanical, quantity);
            if (owner is PartyOwner) partyTokens.Add("s:" + item.Id);
        }
        else
        {
            for (ulong count = 0; count < quantity; count++)
            {
                EntityId id = new(allocate());
                candidate.MaterializeUnique(new ItemState(id, item.Mechanical), destination);
                if (owner is PartyOwner) partyTokens.Add("i:" + id.Value);
            }
        }
        RequirePartyRoom(partyTokens);
        candidate.Publish();
        foreach (string token in partyTokens) TakeSlot(token);
    }
    internal void Consume(InventoryOwner owner, string definition, ulong quantity)
    {
        GearDefinition item = definitions.Item(definition);
        if (item.Kind != ItemKind.Fungible || quantity == 0) throw new InvalidDataException("Choose available ammunition.");
        InventoryEdit candidate = world.Prepare();
        candidate.Consume(new(Owner(owner.Key).Id), item.Mechanical, quantity);
        candidate.Publish();
        if (owner is PartyOwner) SyncSlots();
    }
    internal void Transfer(ItemRef item, InventoryOwner to, ulong quantity, ulong expectedRevision)
    {
        if (item.OwnerKey == to.Key) throw new InvalidDataException("Choose a different destination.");
        // Characters carry equipped gear only; loose items live in the party.
        // Equip (with its slot and power validation) is the way onto a member.
        if (to is MemberOwner) throw new InvalidDataException("Characters carry only equipped gear — send it to the party instead.");
        CarriedItem found = Find(item.OwnerKey, item.Token);
        if (quantity == 0 || quantity > found.Quantity) throw new InvalidDataException("Choose an available quantity.");
        // A floor quadrant/alcove/plate holds one item kind, with stack merging permitted.
        // The party pack is a real multi-kind inventory and skips this rule.
        if (to is AnchorOwner anchor && anchor.Anchor != "crate")
        {
            IReadOnlyList<CarriedItem> existing = Items(to.Key);
            if (existing.Any(i => i.Entity != 0 || found.Entity != 0 || i.Definition != found.Definition))
                throw new InvalidDataException("That anchor is occupied.");
        }
        InventoryEdit candidate = world.Prepare(expectedRevision);
        EntityId source = new(Owner(item.OwnerKey).Id), destination = new(Owner(to.Key).Id);
        if (found.Entity == 0) candidate.TransferFungible(source, destination, definitions.Item(found.Definition).Mechanical, quantity);
        else
        {
            EntityId id = new(found.Entity);
            if (found.Slots.Length > 0) candidate.Unequip(source, id);
            candidate.TransferUnique(id, source, destination);
        }
        if (to is PartyOwner) RequirePartyRoom([item.Token]);
        candidate.Publish();
        if (to is PartyOwner) TakeSlot(item.Token);
        if (item.Owner is PartyOwner) SyncSlots();
        if (found.Slots.Length > 0 && BoundMember(item.OwnerKey, out var member)) member.UnequipContribution(item.Token);
    }
    internal void Equip(ItemRef item, string slot, long wielderPower, ulong expectedRevision, MemberOwner destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CarriedItem found = Find(item.OwnerKey, item.Token);
        GearDefinition definition = definitions.Item(found.Definition);
        if (!definition.Slots.Contains(slot)) throw new InvalidDataException("This item cannot use that slot.");
        if (wielderPower < definition.MinimumPower) throw new InvalidDataException("This character needs more power to use that gear.");
        EntityId id = new(Owner(destination.Key).Id);
        InventoryEdit candidate = world.Prepare(expectedRevision);
        if (item.OwnerKey != destination.Key)
        {
            if (found.Slots.Length > 0) candidate.Unequip(new(Owner(item.OwnerKey).Id), new(found.Entity));
            candidate.TransferUnique(new(found.Entity), new(Owner(item.OwnerKey).Id), id);
        }
        // Swapping out of the party grid returns the displaced gear to the
        // grid, not into the member pack: equipping from anywhere else keeps
        // the old behavior. The incoming item vacates its own slot first.
        bool toParty = item.Owner is PartyOwner;
        List<string> displacedParty = [];
        List<string> displacedTokens = [];
        foreach (CarriedItem displaced in Items(destination.Key).Where(i => i.Slots.Intersect(definition.Slots).Any()))
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
        candidate.Equip(id, new(found.Entity), Slots(definition.Slots));
        candidate.Publish();
        if (toParty)
        {
            SyncSlots();
            foreach (string placed in displacedParty) TakeSlot(placed);
        }
        else if (item.Owner is PartyOwner) SyncSlots();
        // Per-source contributions mirror the ledger: displaced gear loses its
        // own contribution and the incoming item gains its own, so unequipping
        // later removes exactly what this equip added.
        if (BoundMember(destination.Key, out var wearer))
        {
            foreach (string displaced in displacedTokens) wearer.UnequipContribution(displaced);
            wearer.EquipContribution(item.Token, definition.Power, definition.Defense);
        }
    }
    internal void Unequip(ItemRef item, ulong expectedRevision)
    {
        CarriedItem found = Find(item.OwnerKey, item.Token);
        if (found.Slots.Length == 0) throw new InvalidDataException("That item is not equipped.");
        InventoryEdit candidate = world.Prepare(expectedRevision);
        candidate.Unequip(new(Owner(item.OwnerKey).Id), new(found.Entity));
        candidate.Publish();
        if (BoundMember(item.OwnerKey, out var member)) member.UnequipContribution(item.Token);
    }
    internal InventoryEdit PrepareUse(ItemRef item, ulong expectedRevision)
    {
        CarriedItem found = Find(item.OwnerKey, item.Token);
        GearDefinition definition = definitions.Item(found.Definition);
        InventoryEdit candidate = world.Prepare(expectedRevision);
        if (definition.Cost > 0)
        {
            if (found.Entity == 0) candidate.Consume(new(Owner(item.OwnerKey).Id), definition.Mechanical, definition.Cost);
            else if (definition.Cost == 1) candidate.DestroyUnique(new(found.Entity));
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
    internal void Destroy(ItemRef item)
    {
        CarriedItem found = Find(item.OwnerKey, item.Token);
        InventoryEdit candidate = world.Prepare();
        if (found.Entity == 0) candidate.Consume(new(Owner(item.OwnerKey).Id), definitions.Item(found.Definition).Mechanical, found.Quantity);
        else candidate.DestroyUnique(new(found.Entity));
        candidate.Publish();
        if (item.Owner is PartyOwner) SyncSlots();
        if (found.Slots.Length > 0 && BoundMember(item.OwnerKey, out var member)) member.UnequipContribution(item.Token);
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
        foreach (SavedPack pack in snapshot.Packs) result.RestorePack(candidate, pack);
        candidate.Publish();
        return result;
    }

    /// <summary>
    /// Restores one travelling pack into a live floor inventory, preserving
    /// item identity. The owner must already be registered. Same validation
    /// as snapshot restore: the merge of two live generations is checked,
    /// not trusted.
    /// </summary>
    internal void RestorePack(SavedPack pack)
    {
        Owner(pack.Owner.Key);
        InventoryEdit candidate = world.Prepare();
        RestorePack(candidate, pack);
        candidate.Publish();
    }

    private void RestorePack(InventoryEdit candidate, SavedPack pack)
    {
        {
            EntityId owner = new(pack.Owner.Id);
            GameDefinitions.Require(pack.Stacks.Select(s => s.Definition).Distinct().Count() == pack.Stacks.Length, "saved stack uniqueness");
            // Saves that predate the shared party inventory kept loose items
            // in member packs. They cannot load: reject loudly, never migrate
            // silently into a model the player did not choose.
            if (InventoryOwner.Parse(pack.Owner.Key) is MemberOwner && (pack.Stacks.Length > 0
                || pack.Items.Any(item => !pack.Equipment.Any(equipment => equipment.Item == item.Id))))
                throw new InvalidDataException("This save predates the shared party inventory; start a new expedition.");
            foreach (SavedStack stack in pack.Stacks) candidate.Grant(owner, definitions.Item(stack.Definition).Mechanical, stack.Quantity);
            foreach (SavedItem item in pack.Items) candidate.MaterializeUnique(new ItemState(new(item.Id), definitions.Item(item.Definition).Mechanical), owner);
            foreach (SavedEquipment equipment in pack.Equipment)
            {
                SavedItem item = pack.Items.Single(i => i.Id == equipment.Item);
                GameDefinitions.Require(equipment.Slots.ToHashSet().SetEquals(definitions.Item(item.Definition).Slots), "saved equipment slots");
                candidate.Equip(owner, new(equipment.Item), Slots(equipment.Slots));
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
                foreach (SavedSlot saved in savedSlots) slots[saved.Token] = saved.Slot;
                foreach (string token in live.Where(token => SlotOf(token) < 0)) TakeSlot(token);
            }
            if (InventoryOwner.Parse(pack.Owner.Key) is AnchorOwner anchor && anchor.Anchor != "crate")
                GameDefinitions.Require(pack.Items.Length + pack.Stacks.Length <= 1, "saved anchor occupancy");
        }
    }
}
