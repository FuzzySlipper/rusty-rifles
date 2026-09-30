using System.Numerics;
using Rifles.Game.Audio;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Items;
using Rifles.Procgen.Generation;
using Rusty.Engine;

namespace Rifles.Game.Expedition;

/// <summary>Session services shared by every floor activation.</summary>
internal sealed record FloorServices(GameDefinitions Definitions, IEngineContext Engine,
    DungeonMaterialCache Materials, GeneratedArt Art, ItemArt ItemArt,
    Func<ulong> AllocateLightId, Action<string> Message, Action<SoundCue, Vector3> Sound,
    Action<string> CancelRest, Func<DungeonScene, GridPoint, Vector3> Aim);
