using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Procgen;
using Rifles.Procgen.Expeditions;
using Rifles.Procgen.Generation;

internal static class RoomCatalogueChecks
{
    internal static void Run(GameDefinitions definitions)
    {
        static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
        HashSet<string> used = [];
        foreach (ulong seed in new ulong[] { 0, 1, 29, 83 })
        {
            var intent = new ExpeditionGenerator().Generate(definitions.Generation.Expedition, seed).Expedition!;
            foreach (var floor in intent.Floors)
            {
                var result = new DungeonGenerator().Generate(floor.Candidate, definitions.Generation.Policy, floor.Candidate.Seed, definitions.Rooms.Shapes);
                Require(result.Accepted, $"Generation rejected {seed}/{floor.Id}: {result.Attempts.Last().Detail}");
                var resolved = DungeonFloor.Generate(floor, definitions.Generation.Policy, definitions.Rooms, definitions.Generation.Elevation);
                resolved.Validate();
                FloorCompositionChecks.Check(resolved, definitions, result);
                Require(resolved.Rooms.Length == floor.Candidate.Graph.Nodes.Count, "All graph features receive a resolved functional room.");
                foreach (var room in resolved.Rooms)
                {
                    used.Add(room.TemplateId);
                    var template = definitions.Rooms.Rooms.Single(r => r.Id == room.TemplateId);
                    var node = floor.Candidate.Graph.Nodes.Single(n => n.Id == room.NodeId);
                    Require(template.NodeKinds.Contains(node.Kind), "Room function fits its graph role.");
                    Require(room.Thresholds.All(room.Cells.Contains), "Resolved thresholds stay in the room.");
                }
                var reordered = definitions.Rooms with { Rooms = definitions.Rooms.Rooms.Reverse().ToArray() };
                Require(resolved.GenerationIdentity == DungeonFloor.Generate(floor, definitions.Generation.Policy, reordered, definitions.Generation.Elevation).GenerationIdentity,
                    "Room selection does not depend on catalogue ordering.");
            }
        }
        var simple = new GraphCore().CreateInitial(new SeedIntent("room-fit", "Room fit", []), 29);
        foreach (var shape in definitions.Rooms.Shapes.Shapes)
        {
            var single = new ShapeCatalog("single-room-fit", [shape with { NodeKinds = null }]);
            var fit = new DungeonGenerator().Generate(simple, definitions.Generation.Policy, 29, single);
            Require(fit.Accepted && fit.Artifacts!.Pieces.All(p => p.ShapeId == shape.Id),
                "Each catalogue template must independently fit and connect: " + shape.Id);
        }
        var editable = definitions.Rooms.Shapes.Shapes.Single(s => s.Id == "records-alcove") with { NodeKinds = null };
        var beforeEdit = new DungeonGenerator().Generate(simple, definitions.Generation.Policy, 29, new ShapeCatalog("edit-fit", [editable]));
        var afterEdit = new DungeonGenerator().Generate(simple, definitions.Generation.Policy, 29,
            new ShapeCatalog("edit-fit", [editable with { WalkableCells = editable.WalkableCells.Append(new GridPoint(4, 0)).ToArray() }]));
        Require(beforeEdit.Accepted && afterEdit.Accepted && beforeEdit.Identity != afterEdit.Identity,
            "Editing a room's cells under the same ID changes the resolved geometry identity.");
        Require(used.Count >= 6, "Seed bank actually exercises different functional room templates.");
        var first = new ExpeditionGenerator().Generate(definitions.Generation.Expedition, 29).Expedition!.Floors[0];
        var policy = definitions.Generation.Policy;
        var noFit = new DungeonGenerator().Generate(first.Candidate, policy with { RoomFootprintCells = 1 }, first.Candidate.Seed, definitions.Rooms.Shapes);
        Require(!noFit.Accepted && noFit.RejectionStage == "matching", "Oversized room templates are rejected before placement.");
        var quota = new DungeonGenerator().Generate(first.Candidate, policy with { RoomFootprintCells = 1, MaxCatalogCandidatesPerRequirement = 1 }, first.Candidate.Seed, definitions.Rooms.Shapes);
        Require(!quota.Accepted && quota.RejectionCode == "catalog_candidate_quota_exhausted", "Matching has a real per-room work budget.");
        var templateWithBadExit = definitions.Rooms.Rooms[0] with { Exits = [new CatalogExit("inside", definitions.Rooms.Rooms[0].Content, CardinalDirection.North)] };
        try { (definitions.Rooms with { Rooms = [templateWithBadExit] }).Validate(); throw new Exception("Interior threshold accepted."); }
        catch (InvalidDataException) { }
        var pier = definitions.Rooms.Rooms.Single(r => r.Id == "drain-crossing");
        var inwardPier = pier with { Exits = [new CatalogExit("pier", new GridPoint(3, 2), CardinalDirection.South)] };
        try { (definitions.Rooms with { Rooms = [inwardPier] }).Validate(); throw new Exception("Internal solid threshold accepted."); }
        catch (InvalidDataException) { }
        var invalidShape = definitions.Rooms.Shapes.Shapes.Single(s => s.Id == pier.Id) with { Exits = inwardPier.Exits, NodeKinds = null };
        var rejectedPier = new DungeonGenerator().Generate(simple, policy, 29, new ShapeCatalog("pier-exit", [invalidShape]));
        Require(!rejectedPier.Accepted && rejectedPier.RejectionCode == "shape_exit_invalid", "Shared catalogue boundary rejects an exit into a solid pier.");
        var played = DungeonFloor.Generate(29, definitions.Generation, definitions.Rooms);
        var changedRoom = played.Rooms[0] with { Title = "Changed saved room", TemplateId = "changed" };
        try { (played with { Rooms = [changedRoom, .. played.Rooms.Skip(1)] }).Validate(); throw new Exception("Edited room manifest accepted."); }
        catch (InvalidDataException) { }
        Console.WriteLine($"Room catalogue checks passed: {used.Count} templates across four seeds, fit, thresholds, reorder and bounded failures.");
    }
}
