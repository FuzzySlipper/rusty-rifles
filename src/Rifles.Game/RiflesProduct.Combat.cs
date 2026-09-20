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
    // the same guarantee the scene!/movement! sites rely on.
    private RiflesCombat combat = null!;
    private EncounterPlacementResult? encounterPlacement;
    private WorldArt? combatArt;
    private Appearance? boltAppearance;
    private CombatDefinition Combat => definitions.Combat;
    private Vector3 Aim(GridPoint cell) => scene!.Eye(cell) with { Y = scene.GroundHeight(cell) + Combat.AimHeight };
    private void CombatMessage(string message)
    {
        feedback = message;
        combat.AddLog(message);
    }

    private void AdvanceCombat(double seconds)
    {
        if (seconds <= 0) return;
        AdvanceMagic(seconds);
        combat.Advance(seconds);
        if (combat.Defeated)
        {
            magic!.Clear("party"); CancelRest("Party defeated.");
            paused = true; controls.Clear(); feedback = definitions.Run.DefeatText;
            exploration.Stop(); movement!.Remove(partyId); exploration.Detach();
            combat.CancelAll();
        }
    }
}
