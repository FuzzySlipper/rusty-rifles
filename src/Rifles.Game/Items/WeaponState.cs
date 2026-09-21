using Rifles.Game.Content;

namespace Rifles.Game.Items;

/// <summary>Saved state is keyed by the Engine unique-item identity, never by owner or weapon definition.</summary>
internal sealed record MusketWeaponSnapshot(ulong Item, bool Loaded, bool BayonetFixed, ulong ShotCount = 0);
internal sealed record WeaponStateSnapshot(MusketWeaponSnapshot[] Muskets);

/// <summary>Resolved live capability used by combat orders and their outcome reporting.</summary>
internal sealed record WeaponCapabilities(ulong Item, string WeaponId, string? MeleeReach, string? FireReach, long MeleeDamage, long FireDamage,
    double WindupSeconds, double RecoverySeconds, float Accuracy, double ReloadSeconds, bool ChargeEligible, bool Loaded, bool BayonetFixed);

/// <summary>
/// Owns the mutable state carried by unique muskets. Inventory remains the
/// identity/location owner; this class never copies or transfers an item.
/// </summary>
internal sealed class WeaponState
{
    private readonly ItemDefinitions definitions;
    private readonly Dictionary<ulong, MusketWeaponSnapshot> muskets = [];

    private WeaponState(ItemDefinitions definitions)
    {
        this.definitions = definitions;
    }

    internal static WeaponState Create(ItemDefinitions definitions, ItemInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(definitions); ArgumentNullException.ThrowIfNull(inventory);
        WeaponState result = new(definitions);
        result.Reconcile(inventory);
        return result;
    }

    internal static WeaponState Restore(ItemDefinitions definitions, WeaponStateSnapshot snapshot, ItemInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(definitions); ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(inventory);
        if (snapshot.Muskets is null || snapshot.Muskets.Any(state => state is null)
            || snapshot.Muskets.Select(state => state.Item).Distinct().Count() != snapshot.Muskets.Length)
            throw new InvalidDataException("Invalid saved musket state.");
        WeaponState result = new(definitions);
        foreach (MusketWeaponSnapshot saved in snapshot.Muskets)
        {
            GearDefinition item = definitions.Item(inventory.UniqueItem(saved.Item).Definition);
            if (item.Weapon is not { IsMusket: true }) throw new InvalidDataException("Saved musket state names a non-musket item.");
            result.muskets.Add(saved.Item, saved);
        }
        result.Reconcile(inventory);
        return result;
    }

    /// <summary>Capturing also retires destroyed muskets and admits new ones unloaded.</summary>
    internal WeaponStateSnapshot Capture(ItemInventory inventory)
    {
        Reconcile(inventory);
        return new(muskets.Values.OrderBy(state => state.Item).ToArray());
    }

    internal void SetLoaded(ulong item, ItemInventory inventory, bool loaded)
    {
        MusketWeaponSnapshot state = Musket(item, inventory);
        muskets[item] = state with { Loaded = loaded };
    }

    internal void SetBayonetFixed(ulong item, ItemInventory inventory, bool fixedBayonet)
    {
        MusketWeaponSnapshot state = Musket(item, inventory);
        muskets[item] = state with { BayonetFixed = fixedBayonet };
    }

    /// <summary>
    /// Rolls a repeatable but independent shot for this musket. The shot count
    /// is saved with the unique item, so save/load and travel cannot replay a
    /// favorable result or make one target a permanent hit/miss.
    /// </summary>
    internal bool RollFireHit(ulong item, ItemInventory inventory, ulong target)
    {
        MusketWeaponSnapshot state = Musket(item, inventory);
        WeaponCapabilities capabilities = Capabilities(item, inventory);
        ulong roll = unchecked((item * 1103515245UL) ^ (target * 12345UL) ^ (state.ShotCount * 6364136223846793005UL)) % 10_000;
        muskets[item] = state with { ShotCount = unchecked(state.ShotCount + 1) };
        return roll < (ulong)Math.Round(capabilities.Accuracy * 10_000, MidpointRounding.AwayFromZero);
    }

    internal WeaponCapabilities Capabilities(ulong item, ItemInventory inventory)
    {
        GearDefinition gear = definitions.Item(inventory.UniqueItem(item).Definition);
        MartialWeaponDefinition weapon = gear.Weapon ?? throw new InvalidDataException("That item has no martial capability.");
        MusketWeaponSnapshot? state = weapon.IsMusket ? Musket(item, inventory) : null;
        BayonetDefinition? bayonet = state is { BayonetFixed: true } ? weapon.Bayonet : null;
        return new(item, gear.Id, bayonet?.Reach ?? (weapon.MeleeDamage > 0 ? weapon.Reach : null),
            weapon.IsMusket ? weapon.Reach : null, bayonet?.MeleeDamage ?? weapon.MeleeDamage, weapon.FireDamage,
            weapon.WindupSeconds, weapon.RecoverySeconds, weapon.Accuracy * (bayonet?.AccuracyMultiplier ?? 1),
            weapon.ReloadSeconds * (bayonet?.ReloadSecondsMultiplier ?? 1), weapon.ChargeEligible || bayonet?.ChargeEligible == true,
            state?.Loaded ?? false, state?.BayonetFixed ?? false);
    }

    private MusketWeaponSnapshot Musket(ulong item, ItemInventory inventory)
    {
        if (muskets.TryGetValue(item, out MusketWeaponSnapshot? state)) return state;
        GearDefinition gear = definitions.Item(inventory.UniqueItem(item).Definition);
        if (gear.Weapon is not { IsMusket: true }) throw new InvalidDataException("That item is not a compatible musket.");
        state = new MusketWeaponSnapshot(item, false, false);
        muskets.Add(item, state);
        return state;
    }

    private void Reconcile(ItemInventory inventory)
    {
        Dictionary<ulong, GearDefinition> live = inventory.Owners.SelectMany(owner => inventory.Items(owner.Key))
            .Where(item => item.Entity != 0).ToDictionary(item => item.Entity, item => definitions.Item(item.Definition));
        foreach (ulong retired in muskets.Keys.Where(id => !live.ContainsKey(id)).ToArray()) muskets.Remove(retired);
        foreach ((ulong id, GearDefinition gear) in live)
        {
            if (gear.Weapon is { IsMusket: true } && !muskets.ContainsKey(id)) muskets.Add(id, new MusketWeaponSnapshot(id, false, false));
        }
    }
}
