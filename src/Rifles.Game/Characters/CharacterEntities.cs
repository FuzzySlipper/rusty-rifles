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
    /// Binds inventory/equipment owner facades to a character entity. The
    /// facades address the member's ledger record (admitted pack id) while
    /// living and dying with the entity; the instance map stays the explicit
    /// durable relationship. Re-binding is idempotent.
    /// </summary>
    internal (InventoryComponent Inventory, EquipmentComponent Equipment) BindInventory(
        string instanceKey, InventoryStore world, EntityId ledgerOwner)
    {
        if (!instances.TryGetValue(instanceKey, out EntityId entity))
            throw new InvalidDataException($"Unknown character instance '{instanceKey}'.");
        ArgumentNullException.ThrowIfNull(world);
        Actor actor = new(store, entity);
        if (!actor.Has<InventoryComponent>())
            actor.Add(new InventoryComponent(world, ledgerOwner));
        if (!actor.Has<EquipmentComponent>())
            actor.Add(new EquipmentComponent(world, ledgerOwner));
        return (actor.Get<InventoryComponent>(), actor.Get<EquipmentComponent>());
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
