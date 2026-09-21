using Rifles.Game.Combat;
using Rifles.Game.Party;
using Rifles.Game.Presentation;

namespace Rifles.Game;

public sealed partial class RiflesProduct
{
    private GameOutcome FormationCommand(SessionCommand command)
    {
        FormationPlanner planner = party.Formation;
        switch (command.Action)
        {
            case "formation-open":
                if (active.Combat.ChargeExecuting) return GameOutcome.Reject("Finish the charge before changing formation.");
                if (active.Exploration.Moving || progress.Completed || party.Defeated)
                    return GameOutcome.Reject("Finish moving before changing formation.");
                planner.Begin(paused);
                SetPaused(true);
                return GameOutcome.Accept("Plan formation; time is paused.");
            case "formation-place":
                planner.Place(command.Member ?? "", command.Position ?? "");
                return GameOutcome.Accept();
            case "formation-cancel":
                bool previous = planner.PreviouslyPaused;
                planner.Cancel();
                SetPaused(previous);
                return GameOutcome.Accept("Formation plan cancelled.");
            case "formation-execute":
                FormationMove[] moves = planner.PlannedMoves();
                foreach (FormationMove move in moves)
                {
                    var member = party.Members.Single(member => member.InstanceId == move.Member);
                    if (active.Combat.ActionOf(member).Current?.Phase == ActionPhase.Recovery)
                        return GameOutcome.Reject(member.Definition.Name + " must finish recovery. Cancel the draft to resume time.");
                }
                bool changed = planner.Execute(definitions.Formation.RepositionSeconds);
                if (!changed) { SetPaused(planner.PreviouslyPaused); return GameOutcome.Accept("Formation unchanged."); }
                foreach (FormationMove move in moves)
                    active.Combat.ActionOf(party.Members.Single(member => member.InstanceId == move.Member)).Cancel();
                CancelRest("Rest interrupted by repositioning.");
                controls.Clear();
                SetPaused(false);
                return GameOutcome.Accept("Repositioning: movement and affected soldiers are committed.");
            default:
                return GameOutcome.Reject("Unknown formation command.");
        }
    }
}
