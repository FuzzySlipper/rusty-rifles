using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;

namespace Rifles.Game.Characters;

/// <summary>
/// Single owner of character entity lifetime and composition over one Engine
/// <see cref="EntityStore"/>. Factories assemble entities from admitted
/// definitions; wrapping an entity afterwards attaches nothing. Durable
/// instance ids map to runtime <see cref="EntityId"/> values exactly once per
/// construction — never manufactured from saved integers.
/// </summary>
internal sealed class CharacterEntities
{
    private readonly EntityStore store = new([]);
    private readonly Dictionary<string, EntityId> instances = new(StringComparer.Ordinal);

    internal EntityStore Store => store;

    /// <summary>
    /// Attaches a freshly built <see cref="StatsComponent"/> to a new entity
    /// of the given kind and maps the durable instance key. One construction,
    /// one mapping: duplicates are rejected.
    /// </summary>
    internal (EntityId Entity, StatsComponent Stats) AttachStats(string instanceKey, string typeValue, Func<StatsComponent> build)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeValue);
        ArgumentNullException.ThrowIfNull(build);
        if (instances.ContainsKey(instanceKey))
            throw new InvalidDataException($"Duplicate character instance '{instanceKey}'.");
        EntityId entity = store.Create(new EntityTypeId(typeValue), EntityLifecycle.Active);
        StatsComponent stats = build();
        store.Add(entity, stats);
        instances.Add(instanceKey, entity);
        return (entity, stats);
    }

    /// <summary>
    /// Destroys the previous entity for an instance key, if any. Restore paths
    /// use this so rebuilt characters start modifier-free, matching fresh
    /// construction; callers re-apply equipment after restore.
    /// </summary>
    internal void Detach(string instanceKey)
    {
        if (instances.Remove(instanceKey, out EntityId entity))
            store.Destroy(entity, null);
    }

    /// <summary>
    /// Rebuilds the entity for an instance key, destroying any previous one.
    /// Enemy construction always rebuilds: validate/restore paths re-run on
    /// live parties (saves, fixtures), and enemy keys are unique per enemy, so
    /// a collision can only be a rebuild — never two live claimants.
    /// </summary>
    internal (EntityId Entity, StatsComponent Stats) ReplaceStats(string instanceKey, string typeValue, Func<StatsComponent> build)
    {
        Detach(instanceKey);
        return AttachStats(instanceKey, typeValue, build);
    }

    internal bool TryGetEntity(string instanceKey, out EntityId entity) => instances.TryGetValue(instanceKey, out entity);

    /// <summary>
    /// Binds the travelling party pack to its own entity. The party has
    /// inventory but never equipment or stats; the entity gives it the same
    /// lifetime and discovery surface as character packs. Idempotent.
    /// </summary>
    internal InventoryComponent BindParty(InventoryStore world, EntityId ledgerOwner)
    {
        ArgumentNullException.ThrowIfNull(world);
        const string key = "party";
        if (!instances.TryGetValue(key, out EntityId entity))
        {
            entity = store.Create(new EntityTypeId("rifles:party"), EntityLifecycle.Active);
            instances.Add(key, entity);
        }

        Actor actor = new(store, entity);
        if (!actor.Has<InventoryComponent>())
            actor.Add(new InventoryComponent(world, ledgerOwner));
        return actor.Get<InventoryComponent>();
    }

    /// <summary>
    /// Binds a floor container or transient combat pack to its own entity.
    /// Containers have no durable character counterpart; the pack key is the
    /// durable identity and the entity gives it lifetime and discovery.
    /// Idempotent per key.
    /// </summary>
    internal InventoryComponent BindContainer(string key, InventoryStore world, EntityId ledgerOwner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(world);
        if (!instances.TryGetValue(key, out EntityId entity))
        {
            entity = store.Create(new EntityTypeId("rifles:container:" + key), EntityLifecycle.Active);
            instances.Add(key, entity);
        }

        Actor actor = new(store, entity);
        if (!actor.Has<InventoryComponent>())
            actor.Add(new InventoryComponent(world, ledgerOwner));
        return actor.Get<InventoryComponent>();
    }

    /// <summary>
    /// Binds inventory/equipment owner facades to a character entity. The
    /// facades address the member's ledger record (admitted pack id) while
    /// living and dying with the entity; the instance map stays the explicit
    /// durable relationship. Re-binding is idempotent.
    /// </summary>
    internal (InventoryComponent Inventory, EquipmentComponent? Equipment) BindInventory(
        string instanceKey, InventoryStore world, EntityId ledgerOwner, bool equipment = true)
    {
        if (!instances.TryGetValue(instanceKey, out EntityId entity))
            throw new InvalidDataException($"Unknown character instance '{instanceKey}'.");
        ArgumentNullException.ThrowIfNull(world);
        Actor actor = new(store, entity);
        if (!actor.Has<InventoryComponent>())
            actor.Add(new InventoryComponent(world, ledgerOwner));
        // Equipment facades attach only where the ledger holds equipment
        // state (member packs); enemy packs never equip.
        if (equipment && !actor.Has<EquipmentComponent>())
            actor.Add(new EquipmentComponent(world, ledgerOwner));
        return (actor.Get<InventoryComponent>(), equipment ? actor.Get<EquipmentComponent>() : null);
    }

    /// <summary>
    /// Attaches an arbitrary component to an established entity, returning
    /// the same attached instance. Used for action state and other
    /// per-character attachments that follow entity lifetime.
    /// </summary>
    internal T AttachComponent<T>(string instanceKey, Func<T> build) where T : class
    {
        if (!instances.TryGetValue(instanceKey, out EntityId entity))
            throw new InvalidDataException($"Unknown character instance '{instanceKey}'.");
        ArgumentNullException.ThrowIfNull(build);
        Actor actor = new(store, entity);
        if (!actor.Has<T>())
            actor.Add(build());
        return actor.Get<T>();
    }

    internal string? TryGetInstance(EntityId entity)
    {
        foreach ((string key, EntityId id) in instances)
        {
            if (id == entity) return key;
        }

        return null;
    }
}
