using Rifles.Game.Expedition;
using Rifles.Game.Presentation;
using Rifles.Procgen.Generation;
using Rusty.Engine.Interaction;

namespace Rifles.Game;

public sealed partial class RiflesProduct
{
    private (ulong Floor, GridPoint Position, ulong Revision, bool Door)? mapObservation;

    private void ObserveMap()
    {
        var observation = (active.FloorId, active.Exploration.Position, active.GeneratedFeatures.Revision, active.ItemWorld.DoorOpen);
        if (mapObservation == observation) return;
        mapObservation = observation;
        var known = progress.Maps.FirstOrDefault(m => m.FloorKey == active.Floor.IntentFloorId)?.Cells.ToHashSet() ?? [];
        foreach (var cell in active.Floor.Cells.Where(c => c.ManhattanDistance(active.Exploration.Position) <= definitions.Run.MapRevealCells))
            if (cell == active.Exploration.Position || active.Scene.Visibility(active.Scene.Eye(active.Exploration.Position), active.Scene.Eye(cell)) == InteractionVisibility.Visible)
                known.Add(cell);
        progress = progress with { Maps = progress.Maps.Where(m => m.FloorKey != active.Floor.IntentFloorId)
            .Append(new MapMemory(active.Floor.IntentFloorId, known.OrderBy(c => c.Y).ThenBy(c => c.X).ToArray())).ToArray() };
    }

    private uint MapProjection(SessionValueBuilder value) => value.Object(progress.Maps.Select(memory =>
    {
        var saved = memory.FloorKey == active.Floor.IntentFloorId ? null : inactiveFloors[memory.FloorKey];
        var dungeon = saved?.Floor ?? active.Floor;
        var facts = saved?.GeneratedFeatures ?? active.GeneratedFeatures;
        var known = memory.Cells.ToHashSet();
        var markers = new List<(string, uint)>();
        void Marker(string id, GridPoint cell, string label)
        {
            if (known.Contains(cell)) markers.Add((id, value.Object(("x", value.Number(cell.X)),
                ("y", value.Number(cell.Y)), ("label", value.String(label)))));
        }
        Marker("entrance", dungeon.Entrance, "Arrival / return stair");
        Marker("exit", dungeon.Exit, dungeon.IntentFloorId == expedition.ObjectiveFloor ? definitions.Run.FinaleLabel : "Descent");
        foreach (var gate in facts.Gates.Where(g => g.Discovered)) Marker("gate:" + gate.Id, gate.Cell, gate.Open ? "Open route" : "Closed route");
        foreach (var hazard in facts.Hazards) Marker("hazard:" + hazard.Id, hazard.Cell, hazard.Disabled ? "Disabled hazard" : definitions.Hazards.Clue);
        foreach (var plate in facts.Plates) Marker("plate:" + plate.Id, plate.Cell, definitions.GeneratedFeatures.PlateClue);
        string notes = string.Join("\n", facts.Gates.Where(g => g.Discovered && known.Contains(g.Cell))
            .Select(g => g.Open ? "Opened route: " + g.RouteId : "Blocked route: " + g.RouteId + ". " + definitions.GeneratedFeatures.GateClue));
        return (memory.FloorKey, value.Object(("title", value.String(expedition.Floors.Single(f => f.Id == memory.FloorKey).Title)),
            ("cells", value.Object(memory.Cells.Select(c => (c.X + "," + c.Y, value.Object(
                ("x", value.Number(c.X)), ("y", value.Number(c.Y)), ("level", value.Number(dungeon.Level(c)))))).ToArray())),
            ("markers", value.Object(markers.ToArray())), ("notes", value.String(notes))));
    }).ToArray());
}
