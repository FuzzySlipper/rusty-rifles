using System.Numerics;
using Rifles.Game.Content;
using Rusty.Engine;

namespace Rifles.Game.Dungeon;

/// <summary>Owns sprite appearances. Retire published references before disposal.</summary>
internal sealed class WorldArt : IDisposable
{
    private readonly Dictionary<(string Style, string Image), Appearance> images = [];
    internal WorldArt(IEngineContext engine, GeneratedArt art, WorldArtDefinition definitions)
    {
        try
        {
            SpriteMaterialDescriptor material = new(definitions.Lighting, default, default,
                definitions.NormalStrength, 0, SpriteAlphaMode.Mask, definitions.AlphaCutoff, SpriteShadowPolicy.None);
            foreach (ArtStyleDefinition style in definitions.Styles)
                foreach (SpriteImageDefinition image in style.Images)
                {
                    RenderResourceInfo texture = art.Texture(image.Path, TextureFilter.Linear, TextureWrap.Clamp);
                    Appearance appearance = engine.Graphics.CreateSprite(new SpriteAppearanceRequest(texture.Handle,
                        Vector2.Zero, Vector2.One, new(image.Pivot[0], image.Pivot[1]), new(image.Size[0], image.Size[1]),
                        image.Billboard, SpriteSizeMode.World, 0, SpriteDepthPolicy.Default, new Color(1, 1, 1, 1), material));
                    images.Add((style.Id, image.Id), appearance);
                }
        }
        catch { Dispose(); throw; }
    }
    internal Appearance Image(string style, string id) => images[(style, id)];
    public void Dispose()
    {
        foreach (Appearance image in images.Values) image.Dispose();
        images.Clear();
        // GeneratedArt retains the shared texture resources until product shutdown.
    }
}
