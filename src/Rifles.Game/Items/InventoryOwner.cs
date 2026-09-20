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
}
