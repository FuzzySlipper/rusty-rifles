using Rifles.Game.Content;
using Rusty.Engine;

namespace Rifles.Game.Dungeon;

/// <summary>Owns immutable dungeon material catalogs for this product lifetime.</summary>
internal sealed class DungeonMaterialCache : IDisposable
{
    private readonly IEngineContext engine;
    private readonly GeneratedArt art;
    private readonly Dictionary<(string Style, float VoxelCellSize), DungeonMaterials> entries = [];
    private bool disposed;

    internal DungeonMaterialCache(IEngineContext engine, GeneratedArt art)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.art = art ?? throw new ArgumentNullException(nameof(art));
    }

    internal DungeonMaterials Get(AppearanceStyleDefinition style, float voxelCellSize)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(voxelCellSize);
        var key = (style.Id, voxelCellSize);
        if (entries.TryGetValue(key, out DungeonMaterials? existing)) return existing;

        DungeonMaterials created = new(engine, art, style, voxelCellSize);
        entries.Add(key, created);
        return created;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (DungeonMaterials materials in entries.Values) materials.Dispose();
        entries.Clear();
    }
}
