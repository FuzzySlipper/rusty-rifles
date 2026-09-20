namespace Rifles.Game.Magic;

/// <summary>
/// Typed condition/spell target. Save DTOs keep the string form; gameplay
/// resolves through these. Member targets name durable character instance
/// ids; enemy targets name movement ids; party is the shared party target.
/// </summary>
internal abstract record MagicTarget
{
    internal abstract string Key { get; }

    internal static MagicTarget Parse(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidDataException("Condition target is required.");
        if (key.StartsWith("member:", StringComparison.Ordinal) && key.Length > "member:".Length)
            return new MemberTarget(key["member:".Length..]);
        if (key.StartsWith("enemy:", StringComparison.Ordinal) && key.Length > "enemy:".Length)
            return new EnemyTarget(key["enemy:".Length..]);
        if (key == "party") return new PartyTarget();
        throw new InvalidDataException($"Unknown condition target '{key}'.");
    }
}

internal sealed record MemberTarget(string Instance) : MagicTarget
{
    internal override string Key => "member:" + Instance;
}

internal sealed record EnemyTarget(string Id) : MagicTarget
{
    internal override string Key => "enemy:" + Id;
}

internal sealed record PartyTarget : MagicTarget
{
    internal override string Key => "party";
}
