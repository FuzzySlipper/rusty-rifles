using System.Numerics;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rusty.Engine;

namespace Rifles.Game.Items;

internal sealed record ItemSpriteDefinition(string Id, float[] UvMin, float[] UvMax, float[] Size, float[] Pivot);
internal sealed record ItemArtDefinition(string Path, ItemSpriteDefinition[] Images)
{
    internal void Validate()
    {
        GameDefinitions.Require(Path.StartsWith("art/generated/", StringComparison.Ordinal) && !Path.Contains(".."), "item art path");
        GameDefinitions.Require(Images.Length > 0 && Images.Select(i => i.Id).Distinct().Count() == Images.Length, "item image ids");
        foreach (ItemSpriteDefinition image in Images)
            GameDefinitions.Require(image.UvMin.Length == 2 && image.UvMax.Length == 2 && image.Size.Length == 2 && image.Pivot.Length == 2
                && image.Size.All(v => float.IsFinite(v) && v > 0) && image.Pivot.All(v => float.IsFinite(v) && v >= 0 && v <= 1)
                && Enumerable.Range(0, 2).All(i => float.IsFinite(image.UvMin[i]) && float.IsFinite(image.UvMax[i])
                    && image.UvMin[i] >= 0 && image.UvMax[i] <= 1 && image.UvMin[i] < image.UvMax[i]), "item sprite " + image.Id);
    }
}
internal sealed class ItemArt : IDisposable
{
    private readonly Dictionary<string, Appearance> images = [];
    private readonly Appearance plate;
    internal ItemArt(IEngineContext engine, ItemArtDefinition definitions, WorldArtDefinition lighting, ItemExplorationDefinition exploration)
    {
        try
        {
            RenderResourceInfo texture = engine.Graphics.OpenResource(new RenderResourceRequest(definitions.Path, TextureFilter.Linear, TextureWrap.Clamp));
            SpriteMaterialDescriptor material = new(lighting.Lighting, default, default, lighting.NormalStrength, 0,
                SpriteAlphaMode.Mask, lighting.AlphaCutoff, SpriteShadowPolicy.None);
            foreach (ItemSpriteDefinition image in definitions.Images)
                images.Add(image.Id, engine.Graphics.CreateSprite(new SpriteAppearanceRequest(texture.Handle,
                    new(image.UvMin[0], image.UvMin[1]), new(image.UvMax[0], image.UvMax[1]),
                    new(image.Pivot[0], image.Pivot[1]), new(image.Size[0], image.Size[1]),
                    BillboardMode.Cylindrical, SpriteSizeMode.World, 0, SpriteDepthPolicy.Default, new Color(1, 1, 1, 1), material)));
            float[] color = exploration.PlateColor;
            plate = engine.Graphics.CreatePrimitive(new PrimitiveAppearanceRequest(PrimitiveGeometry.Cube, false, new Color(color[0], color[1], color[2], color[3])));
        }
        catch { foreach (Appearance appearance in images.Values) appearance.Dispose(); throw; }
    }
    internal IEnumerable<AppearanceFact> Facts(ItemInventory inventory, ExplorationItems world, DungeonScene scene)
    {
        foreach (WorldAnchor anchor in world.Anchors.Where(a => a.Key != "crate"))
        {
            CarriedItem? item = inventory.Items(anchor.Key).SingleOrDefault();
            if (item is not null)
                yield return Fact(item.Entity == 0 ? anchor.Id : item.Entity, world.Point(anchor.Key, scene), images[inventory.Definitions.Item(item.Definition).Image], Vector3.One);
        }
        ItemExplorationSnapshot state = world.Capture();
        // The visible lever keeps a stable mechanism identity.
        yield return Fact(state.LeverId, scene.Eye(state.Lever) with { Y = scene.GroundHeight }, images["lever"], Vector3.One);
        WorldAnchor plateAnchor = world.Anchors.Single(a => world.AnchorDefinition(a.Key).Placement == AnchorPlacement.Plate);
        Vector3 position = world.Point(plateAnchor.Key, scene);
        // Plate and any fungible pile have distinct rendering identities.
        yield return Fact(state.PlateId, position with { Y = scene.GroundHeight + world.Definition.PlateHeight / 2 }, plate,
            new Vector3(scene.LogicalCellSize, world.Definition.PlateHeight, scene.LogicalCellSize));
    }
    internal AppearanceFact At(ulong id, Vector3 point, string image, float scale) => Fact(id, point, images[image], new Vector3(scale));
    private static AppearanceFact Fact(ulong id, Vector3 point, Appearance appearance, Vector3 scale) =>
        new(id, false, 0, new Transform(point, Quaternion.Identity, scale), appearance, true, RenderLayer.Scene);
    public void Dispose() { foreach (Appearance appearance in images.Values) appearance.Dispose(); plate.Dispose(); }
}
