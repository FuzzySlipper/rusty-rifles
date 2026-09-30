using System.Text.Json;
using Rifles.Game.Characters;
using Rifles.Game.Combat;
using Rifles.Game.Content;
using Rifles.Game.Debugging;
using Rifles.Game.Dungeon;
using Rifles.Game.Expedition;
using Rifles.Game.Generation;
using Rifles.Game.Items;
using Rifles.Game.Party;
using Rifles.Procgen.Generation;
using Rusty.Engine.Debugging;
using Rusty.Engine.Mechanics;

namespace Rifles.Game;

public sealed partial class RiflesProduct
{
    [DebugCommand("rifles.drill.list", Description = "List the authored martial-command drills. Does not alter the current run.")]
    public string ListMartialDrills() => JsonSerializer.Serialize(definitions.MartialDrills.Drills.Select(drill => new
    {
        drill.Id,
        drill.Name,
        drill.Description,
        enemies = drill.Enemies.Select(enemy => enemy.SpawnId).ToArray(),
        drill.Expected,
    }), new JsonSerializerOptions { WriteIndented = true });

    [DebugCommand("rifles.drill.read", Description = "Read one authored martial-command drill. Does not alter the current run.")]
    public string ReadMartialDrill(string id)
    {
        MartialDrillDefinition drill = definitions.MartialDrills.Drill(id);
        return JsonSerializer.Serialize(drill, new JsonSerializerOptions { WriteIndented = true });
    }

    [DebugCommand("rifles.drill.start", Description = "Reset into one authored martial drill, paused for ordinary game controls.")]
    public string StartMartialDrill(string id, bool replaceRun)
    {
        if (!replaceRun) return "This replaces the current run. Use rifles.drill.start <id> true to confirm.";
        MartialDrillDefinition drill;
        try { drill = definitions.MartialDrills.Drill(id); }
        catch (InvalidDataException error) { return error.Message; }

        try
        {
            StartNewRun(definitions.Generation.Seed);
            ExpeditionSnapshot fresh = Capture();
            ExpeditionSnapshot prepared = MartialDrillBuilder.PrepareMartialDrill(definitions, fresh, drill);
            Activate(prepared, progress);
            feedback = "Authored drill '" + drill.Name + "' ready. " + drill.Expected;
            Publish();
            return feedback;
        }
        catch (InvalidDataException error)
        {
            feedback = "Drill '" + drill.Name + "' unavailable: " + error.Message;
            Publish();
            return feedback;
        }
    }

}
