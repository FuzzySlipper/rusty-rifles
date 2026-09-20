using Rusty.Engine.Mechanics;

namespace Rifles.Game.Characters;

/// <summary>
/// Authored stat/track ids for character stats. Adding a real stat means
/// adding an id here and wiring it into <see cref="RiflesStats"/> plus a
/// named accessor on <see cref="RiflesCharacter"/> — never another scalar
/// property scattered across game logic.
/// </summary>
internal static class RiflesStatIds
{
    internal static readonly StatId Power = StatId.Parse("rifles.power");
    internal static readonly StatId Defense = StatId.Parse("rifles.defense");
    internal static readonly StatId Speed = StatId.Parse("rifles.speed");
    internal static readonly StatId VitalityMax = StatId.Parse("rifles.vitality-max");
    internal static readonly TrackId Vitality = TrackId.Parse("rifles.vitality");
    internal static readonly StatId ResourceMax = StatId.Parse("rifles.resource-max");
    internal static readonly TrackId Resource = TrackId.Parse("rifles.resource");
}
