namespace Rifles.Procgen.Generation;

internal static class TestShapes
{
    public static ShapeCatalog Default { get; } = new("builtin.rooms.v1", new[]
    {
        new CatalogShape("room.cross.5", Square(5), new[]
        {
            new CatalogExit("north", new GridPoint(2, 0), CardinalDirection.North), new CatalogExit("east", new GridPoint(4, 2), CardinalDirection.East),
            new CatalogExit("south", new GridPoint(2, 4), CardinalDirection.South), new CatalogExit("west", new GridPoint(0, 2), CardinalDirection.West),
        }, new[] { new CatalogSocket("center", new GridPoint(2, 2), "content") }, new[] { "room", "default" }),
    });

    private static IReadOnlyList<GridPoint> Square(int size) => Enumerable.Range(0, size).SelectMany(y => Enumerable.Range(0, size).Select(x => new GridPoint(x, y))).ToArray();
}
