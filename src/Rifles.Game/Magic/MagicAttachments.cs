using Rusty.Engine.Mechanics;

namespace Rifles.Game.Magic;

/// <summary>
/// Entity-lifetime record of magic contributions attached to one entity.
/// Effect instances and stat modifiers live on entity components shared
/// across magic-state generations (restores rebuild state, not entities),
/// so every attach checks these keys first: re-attaching would duplicate
/// bonuses and double-apply effect instances. Handles live here — not in
/// the per-generation condition objects — so a later generation expiring
/// the same spell still removes the original modifier. Keys clear on
/// detach/expire; entity rebuilds drop the whole record with the entity.
/// </summary>
internal sealed class MagicAttachments
{
    internal HashSet<string> Effects { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, StatModifierHandle?> Stats { get; } = new(StringComparer.Ordinal);
}
