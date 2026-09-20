using Rifles.Game.Audio;
using System.Numerics;
using Rifles.Game.Combat;
using Rifles.Game.Magic;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Items;
using Rifles.Game.Characters;
using Rifles.Game.Party;
using Rifles.Game.Presentation;
using Rifles.Procgen.Generation;
using Rusty.Engine;

namespace Rifles.Game;

public sealed partial class RiflesProduct
{
    // Set by Activate during Start; the engine never issues commands or reads projections before that,
    // the same guarantee the active.Scene/active.Grid sites rely on.
    private WorldArt? combatArt;
    private Appearance? boltAppearance;
    private CombatDefinition Combat => definitions.Combat;
    private Vector3 Aim(GridPoint cell) => AimOn(active.Scene, cell, Combat.AimHeight);
    private static Vector3 AimOn(DungeonScene scene, GridPoint cell, float height) => scene.Eye(cell) with { Y = scene.GroundHeight(cell) + height };
    private void CombatMessage(string message)
    {
        feedback = message;
        active.Combat.AddLog(message);
    }

    private void AdvanceCombat(double seconds)
    {
        if (seconds <= 0) return;
        AdvanceMagic(seconds);
        active.Combat.Advance(seconds);
        if (active.Combat.Defeated)
        {
            active.Magic.Clear(new PartyTarget()); CancelRest("Party defeated.");
            paused = true; controls.Clear(); feedback = definitions.Run.DefeatText;
            active.Exploration.Stop(); active.Grid.Remove(partyId); active.Exploration.Detach();
            active.Combat.CancelAll();
        }
    }
}
