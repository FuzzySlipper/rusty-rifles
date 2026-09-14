using Rifles.Game.Content;
using Rifles.Procgen;
using Rifles.Procgen.Generation;

namespace Rifles.Game.Generation;

internal sealed record RoomTemplate(string Id, string Title, string Function, string Landmark,
    NodeKind[] NodeKinds, string[] Plan, CatalogExit[] Exits, GridPoint Content)
{
    internal GridPoint[] Cells => Plan.SelectMany((row, y) => row.Select((value, x) => (value, x, y)))
        .Where(p => p.value == '.').Select(p => new GridPoint(p.x, p.y)).ToArray();
}
internal sealed record RoomCatalogue(string Id, RoomTemplate[] Rooms)
{
    internal void Validate()
    {
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(Id) && Rooms is { Length: > 0 and <= 64 }
            && Rooms.All(r => r is not null) && Rooms.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() == Rooms.Length, "room catalogue identity/count");
        foreach (RoomTemplate room in Rooms)
        {
            GameDefinitions.Require(!string.IsNullOrWhiteSpace(room.Id) && !string.IsNullOrWhiteSpace(room.Title)
                && !string.IsNullOrWhiteSpace(room.Function) && !string.IsNullOrWhiteSpace(room.Landmark), "room identity/function/landmark");
            GameDefinitions.Require(room.NodeKinds is { Length: > 0 } && room.NodeKinds.All(Enum.IsDefined), "room node kinds");
            GameDefinitions.Require(room.Plan is { Length: > 0 and <= 32 } && room.Plan.All(row => row is { Length: > 0 and <= 32 }
                && row.Length == room.Plan[0].Length && row.All(c => c is '.' or '#')), "room plan");
            var cells = room.Cells.ToHashSet();
            GameDefinitions.Require(cells.Count > 0 && cells.Contains(room.Content)
                && ResolvedGridValidation.IsConnected(cells, room.Content), "connected room/content socket");
            GameDefinitions.Require(room.Exits is { Length: > 0 } && room.Exits.All(e => e is not null
                && !string.IsNullOrWhiteSpace(e.Id) && Enum.IsDefined(e.Direction) && cells.Contains(e.Cell)
                && LeavesPlan(e.Cell + e.Direction.Offset(), room.Plan[0].Length, room.Plan.Length))
                && room.Exits.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() == room.Exits.Length
                && room.Exits.Select(e => e.Cell).Distinct().Count() == room.Exits.Length, "room thresholds");
        }
    }
    private static bool LeavesPlan(GridPoint cell, int width, int height) => cell.X < 0 || cell.Y < 0 || cell.X >= width || cell.Y >= height;
    internal ShapeCatalog Shapes => new(Id, Rooms.Select(r => new CatalogShape(r.Id, r.Cells, r.Exits,
        [new CatalogSocket("content", r.Content, "content")], [r.Function], r.NodeKinds)).ToArray(), ConstrainShapesToLayout: true);
}

internal sealed record ResolvedRoom(string RegionId, string NodeId, string TemplateId, string Title,
    string Function, string Landmark, GridPoint[] Cells, GridPoint[] Thresholds);
