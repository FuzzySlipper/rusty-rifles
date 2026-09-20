namespace Rifles.Game.Items;

/// <summary>
/// Typed inventory owner reference. Gameplay resolves owner strings to these
/// once at the boundary and dispatches on kind; UI tokens stay strings at the
/// serialization edge. Member owners name durable character instance ids.
/// </summary>
internal abstract record InventoryOwner
{
    internal abstract string Key { get; }

    internal static InventoryOwner Parse(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidDataException("Inventory owner is required.");
        if (key.StartsWith("member:", StringComparison.Ordinal) && key.Length > "member:".Length)
            return new MemberOwner(key["member:".Length..]);
        if (key == ItemInventory.PartyKey) return new PartyOwner();
        if (key.StartsWith("combat:", StringComparison.Ordinal)) return new CombatOwner(key);
        return new AnchorOwner(key);
    }
}

internal sealed record MemberOwner(string Instance) : InventoryOwner
{
    internal override string Key => "member:" + Instance;
}

internal sealed record PartyOwner : InventoryOwner
{
    internal override string Key => ItemInventory.PartyKey;
}

internal sealed record AnchorOwner(string Anchor) : InventoryOwner
{
    internal override string Key => Anchor;
}

internal sealed record CombatOwner(string Scope) : InventoryOwner
{
    internal override string Key => Scope;

    /// <summary>Enemy ulong id when this is an enemy pack, otherwise null.</summary>
    internal string? EnemyPackId => Scope.StartsWith("combat:enemy:", StringComparison.Ordinal)
        ? Scope["combat:enemy:".Length..]
        : null;
}

/// <summary>
/// Typed item reference: a classified owner plus the item token. Tokens stay
/// strings (the UI/ledger token format); the owner kind drives dispatch.
/// </summary>
internal sealed record ItemRef(InventoryOwner Owner, string Token)
{
    internal string OwnerKey => Owner.Key;

    internal static ItemRef Parse(string owner, string token)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidDataException("Select an item first.");
        return new ItemRef(InventoryOwner.Parse(owner), token);
    }
}
