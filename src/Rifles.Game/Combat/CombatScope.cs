using Rifles.Game.Audio;
using Rifles.Game.Dungeon;
using Rifles.Game.Generation;
using Rifles.Game.Items;
using Rifles.Procgen.Generation;

namespace Rifles.Game.Combat;

/// <summary>
/// Floor-scoped services one combat execution shares. Plain data for explicit
/// construction; the product root owns lifecycle and passes a fresh scope per
/// floor activation. Sound, messages, rest, and id allocation arrive as
/// explicit callables rather than ambient product access.
/// </summary>
internal sealed record CombatScope(
    DungeonScene Scene,
    MovementGrid Movement,
    ExplorationState Exploration,
    ExplorationItems ItemWorld,
    PatrolActor Actor,
    // Live features arrive after scene construction; snapshot builds pass a throwing provider.
    Func<WorldFeatures> Features,
    GeneratedFeatureState GeneratedFeatures,
    DungeonFloor Floor,
    ulong PartyId,
    double IncomingDamageMultiplier,
    Func<ulong> AllocateIds,
    Func<GridPoint, System.Numerics.Vector3> Aim,
    Action<string> Message,
    Action<SoundCue, System.Numerics.Vector3> Sound,
    Action<string> CancelRest);
