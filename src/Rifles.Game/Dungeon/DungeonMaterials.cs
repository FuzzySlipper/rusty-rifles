using Rusty.Engine;
using Rifles.Game.Content;

namespace Rifles.Game.Dungeon;

/// <summary>Owns one selected dungeon art treatment's Engine catalog and material resources.</summary>
internal sealed class DungeonMaterials : IDisposable
{
    private const uint CatalogVersion = 1;
    private readonly AuthoredCatalog catalog;
    private readonly Material[] materials;
    private bool disposed;

    internal DungeonMaterials(IEngineContext engine, GeneratedArt art, AppearanceStyleDefinition style, float voxelCellSize)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(art);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(voxelCellSize);

        AuthoredCatalog? admittedCatalog = null;
        List<Material> admittedMaterials = [];
        try
        {
            RenderResourceInfo[] textures = OpenTextures(art, style);
            admittedCatalog = engine.AuthoredContent.AdmitCatalogPayload(CreatePayload(style, voxelCellSize));
            ValidateCatalog(engine.AuthoredContent.ReadCatalog(admittedCatalog), style);
            admittedMaterials.Add(CreateMaterial(engine, admittedCatalog, style.Wall, Texture(style, textures, style.Wall.TextureId)));
            admittedMaterials.Add(CreateMaterial(engine, admittedCatalog, style.Ceiling, Texture(style, textures, style.Ceiling.TextureId)));
            admittedMaterials.Add(CreateMaterial(engine, admittedCatalog, style.Floor, Texture(style, textures, style.Floor.TextureId)));
            admittedMaterials.Add(CreateMaterial(engine, admittedCatalog, style.Exit, Texture(style, textures, style.Exit.TextureId)));
            catalog = admittedCatalog;
            materials = admittedMaterials.ToArray();
        }
        catch
        {
            for (int index = admittedMaterials.Count - 1; index >= 0; index--) admittedMaterials[index].Dispose();
            admittedCatalog?.Dispose();
            throw;
        }
    }

    internal Material Wall => MaterialAt(0);
    internal Material Ceiling => MaterialAt(1);
    internal Material Floor => MaterialAt(2);
    internal Material Exit => MaterialAt(3);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        for (int index = materials.Length - 1; index >= 0; index--) materials[index].Dispose();
        catalog.Dispose();
    }

    private static RenderResourceInfo[] OpenTextures(GeneratedArt art, AppearanceStyleDefinition style)
    {
        RenderResourceInfo[] opened = new RenderResourceInfo[style.Textures.Length];
        for (int index = 0; index < style.Textures.Length; index++)
        {
            TextureDefinition texture = style.Textures[index];
            RenderResourceInfo resource = art.Texture(texture.Path, TextureFilter.Linear, TextureWrap.Repeat);
            if (resource.Kind != RenderResourceKind.Texture || resource.ByteLength == 0 || resource.Handle.Handle.Value == 0)
                throw new InvalidOperationException($"Dungeon texture '{texture.Path}' must open as a non-empty Engine texture resource.");
            opened[index] = resource;
        }
        return opened;
    }

    private static Material CreateMaterial(IEngineContext engine, AuthoredCatalog catalog,
        SurfaceDefinition surface, RenderResourceInfo texture) => engine.Graphics.CreateAuthoredMaterial(
            new AuthoredMaterialAppearanceRequest(catalog, surface.MaterialId,
                new RenderResourceReference(texture.Handle.Handle.Value)));

    private static RenderResourceInfo Texture(AppearanceStyleDefinition style, RenderResourceInfo[] opened, string id)
    {
        int index = Array.FindIndex(style.Textures, texture => texture.Id == id);
        return index >= 0 ? opened[index] : throw new InvalidOperationException($"Unknown dungeon texture '{id}'.");
    }

    private static AuthoredCatalogPayloadAdmitRequest CreatePayload(AppearanceStyleDefinition style, float voxelCellSize) => new(
        Entries(style),
        Dependencies(style),
        Materials(style),
        style.Textures.Select(Texture).ToArray(),
        Array.Empty<AuthoredVoxelAtlasInput>(),
        Array.Empty<AuthoredAtlasRegionInput>(),
        Surfaces(style, voxelCellSize));

    private static AuthoredCatalogEntryInput[] Entries(AppearanceStyleDefinition style) =>
        style.Textures.Select(texture => new AuthoredCatalogEntryInput(texture.Id, CatalogVersion, true,
            texture.ContentHash, true, texture.Path, true, $"{style.Id} {texture.Id} texture"))
        .Concat(RoleSurfaces(style).Select(surface => new AuthoredCatalogEntryInput(surface.MaterialId,
            CatalogVersion, false, string.Empty, false, string.Empty, true, $"{style.Id} {surface.MaterialId}")))
        .ToArray();

    private static AuthoredCatalogDependencyInput[] Dependencies(AppearanceStyleDefinition style) =>
        RoleSurfaces(style).Select(surface => new AuthoredCatalogDependencyInput(surface.MaterialId, surface.TextureId,
            AssetVersionRequirementKind.Exact, CatalogVersion, true, style.Texture(surface.TextureId).ContentHash)).ToArray();

    private static AuthoredMaterialInput[] Materials(AppearanceStyleDefinition style) => RoleSurfaces(style)
        .Select(surface => new AuthoredMaterialInput(surface.MaterialId, true, true, true, AuthoredStructuralClass.Solid,
            Color(surface.Color), true, surface.TextureId, AssetVersionRequirementKind.Exact, CatalogVersion, true,
            style.Texture(surface.TextureId).ContentHash, surface.Roughness, new Color(1, 1, 1, 1),
            new Color(surface.Emission[0], surface.Emission[1], surface.Emission[2], 1), 0,
            AuthoredUvStrategy.Planar)).ToArray();

    private static AuthoredTextureInput Texture(TextureDefinition texture) => new(texture.Id, texture.PixelWidth,
        texture.PixelHeight, AuthoredTextureFilter.Linear, AuthoredTextureWrap.Repeat);

    // Engine surface coordinates are physical voxel coordinates. The authored values are
    // world-metre periods/origins, converted independently of logical cell size and detail.
    private static AuthoredVoxelSurfaceInput[] Surfaces(AppearanceStyleDefinition style, float voxelCellSize) => RoleSurfaces(style)
        .Select(surface => new AuthoredVoxelSurfaceInput(surface.MaterialId, CatalogVersion,
            AuthoredVoxelSurfaceMappingKind.Repeat, surface.TextureId, AssetVersionRequirementKind.Exact,
            CatalogVersion, true, style.Texture(surface.TextureId).ContentHash, string.Empty,
            AssetVersionRequirementKind.Any, 0, false, string.Empty, string.Empty,
            surface.WorldTileScaleX / voxelCellSize, surface.WorldTileScaleY / voxelCellSize,
            surface.WorldTileOriginX / voxelCellSize, surface.WorldTileOriginY / voxelCellSize,
            AuthoredVoxelAlphaModeKind.Opaque, 0)).ToArray();

    private static SurfaceDefinition[] RoleSurfaces(AppearanceStyleDefinition style) =>
        [style.Wall, style.Ceiling, style.Floor, style.Exit];

    private static Color Color(float[] values) => new(values[0], values[1], values[2], values[3]);

    private static void ValidateCatalog(AuthoredCatalogReadoutLeaseReceipt readout, AppearanceStyleDefinition style)
    {
        int materialCount = RoleSurfaces(style).Length;
        if (readout.Entries.Length != style.Textures.Length + materialCount || readout.Materials.Length != materialCount
            || readout.Textures.Length != style.Textures.Length || readout.VoxelSurfaces.Length != materialCount
            || readout.VoxelAtlases.Length != 0 || readout.AtlasRegions.Length != 0 || string.IsNullOrWhiteSpace(readout.CanonicalHash))
            throw new InvalidOperationException($"Engine did not retain the complete '{style.Id}' dungeon material catalog.");
    }

    private Material MaterialAt(int index)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return materials[index];
    }
}
