using Rifles.Game.Content;
namespace Rifles.Game.Dungeon;

/// <summary>One PNG admitted for a named dungeon appearance treatment.</summary>
internal sealed record TextureDefinition(string Id, string Path, string ContentHash,
    uint PixelWidth, uint PixelHeight)
{
    internal void Validate(string name)
    {
        GameDefinitions.Require(IsId(Id), name + ".Id");
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(Path) && !Path.StartsWith('/')
            && !Path.Contains("..", StringComparison.Ordinal), name + ".Path");
        GameDefinitions.Require(ContentHash is { Length: 64 }
            && ContentHash.All(Uri.IsHexDigit), name + ".ContentHash");
        GameDefinitions.Require(PixelWidth > 0 && PixelHeight > 0, name + ".PixelSize");
    }

    private static bool IsId(string? value) => !string.IsNullOrWhiteSpace(value)
        && value.StartsWith("texture/", StringComparison.Ordinal)
        && value["texture/".Length..].All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}

/// <summary>
/// One concrete voxel material and its repeat mapping. Tile scale and origin are
/// in logical dungeon cells, each two metres in the initial exploration tuning.
/// They are independent of <see cref="AppearanceDefinition.VoxelsPerCell"/>.
/// </summary>
internal sealed record SurfaceDefinition(string MaterialId, string TextureId, float[] Color,
    float Roughness, float[] Emission, float WorldTileScaleX, float WorldTileScaleY,
    float WorldTileOriginX, float WorldTileOriginY)
{
    internal void Validate(string name, IReadOnlySet<string> textureIds)
    {
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(MaterialId), name + ".MaterialId");
        GameDefinitions.Require(textureIds.Contains(TextureId), name + ".TextureId");
        GameDefinitions.Require(Color is { Length: 4 }
            && Color.All(value => float.IsFinite(value) && value >= 0 && value <= 1), name + ".Color");
        GameDefinitions.Require(float.IsFinite(Roughness) && Roughness >= 0 && Roughness <= 1, name + ".Roughness");
        GameDefinitions.Require(Emission is { Length: 3 }
            && Emission.All(value => float.IsFinite(value) && value >= 0), name + ".Emission");
        GameDefinitions.Require(float.IsFinite(WorldTileScaleX) && WorldTileScaleX > 0
            && float.IsFinite(WorldTileScaleY) && WorldTileScaleY > 0, name + ".WorldTileScale");
        GameDefinitions.Require(float.IsFinite(WorldTileOriginX) && float.IsFinite(WorldTileOriginY), name + ".WorldTileOrigin");
    }
}

/// <summary>One art treatment for the dungeon's fixed wall, ceiling, floor, and exit roles.</summary>
internal sealed record AppearanceStyleDefinition(string Id, TextureDefinition[] Textures,
    SurfaceDefinition Wall, SurfaceDefinition Ceiling, SurfaceDefinition Floor, SurfaceDefinition Exit)
{
    internal void Validate()
    {
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(Id), nameof(Id));
        GameDefinitions.Require(Textures is { Length: > 0 }, nameof(Textures));
        GameDefinitions.Require(Textures.All(texture => texture is not null), nameof(Textures));
        for (int index = 0; index < Textures.Length; index++) Textures[index].Validate($"{Id}.Textures[{index}]");
        GameDefinitions.Require(Textures.Select(texture => texture.Id).Distinct(StringComparer.Ordinal).Count() == Textures.Length,
            Id + ".Textures.Id uniqueness");

        IReadOnlySet<string> textureIds = Textures.Select(texture => texture.Id).ToHashSet(StringComparer.Ordinal);
        GameDefinitions.Require(Wall is not null && Ceiling is not null && Floor is not null && Exit is not null, Id + ".Surfaces");
        Wall!.Validate(Id + ".Wall", textureIds);
        Ceiling!.Validate(Id + ".Ceiling", textureIds);
        Floor!.Validate(Id + ".Floor", textureIds);
        Exit!.Validate(Id + ".Exit", textureIds);
        string[] materialIds = [Wall.MaterialId, Ceiling.MaterialId, Floor.MaterialId, Exit.MaterialId];
        GameDefinitions.Require(materialIds.Distinct(StringComparer.Ordinal).Count() == materialIds.Length, Id + ".MaterialId uniqueness");
    }

    internal TextureDefinition Texture(string id) => Textures.Single(texture => texture.Id == id);
}

internal sealed record AppearanceDefinition(string InitialStyle, int VoxelsPerCell, AppearanceStyleDefinition[] Styles,
    float[] LightColor, float LightIntensity, float LightRange)
{
    internal void Validate()
    {
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(InitialStyle), nameof(InitialStyle));
        GameDefinitions.Require(VoxelsPerCell is >= 1 and <= 4, nameof(VoxelsPerCell));
        GameDefinitions.Require(Styles is { Length: > 0 } && Styles.All(style => style is not null), nameof(Styles));
        foreach (AppearanceStyleDefinition style in Styles) style.Validate();
        GameDefinitions.Require(Styles.Select(style => style.Id).Distinct(StringComparer.Ordinal).Count() == Styles.Length,
            "Styles.Id uniqueness");
        GameDefinitions.Require(Styles.Any(style => style.Id == InitialStyle), nameof(InitialStyle));
        GameDefinitions.Require(LightColor is { Length: 3 } && LightColor.All(value => float.IsFinite(value) && value >= 0), nameof(LightColor));
        GameDefinitions.Require(float.IsFinite(LightIntensity) && LightIntensity >= 0, nameof(LightIntensity));
        GameDefinitions.Require(float.IsFinite(LightRange) && LightRange > 0, nameof(LightRange));
    }

    internal AppearanceStyleDefinition Style(string id) => Styles.Single(style => style.Id == id);
}
