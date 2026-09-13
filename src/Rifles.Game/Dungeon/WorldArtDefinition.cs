using System.Numerics;
using Rifles.Game.Content;
using Rifles.Procgen.Generation;
using Rusty.Engine;

namespace Rifles.Game.Dungeon;

internal sealed record SpriteImageDefinition(string Id, string Path, float[] Size, float[] Pivot, BillboardMode Billboard)
{
    internal void Validate()
    {
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(Id) && Path.StartsWith("art/generated/", StringComparison.Ordinal)
            && !Path.Contains("..", StringComparison.Ordinal), "Sprite image identity/path");
        GameDefinitions.Require(Size is { Length: 2 } && Size.All(v => float.IsFinite(v) && v > 0), "Sprite.Size");
        GameDefinitions.Require(Pivot is { Length: 2 } && Pivot.All(v => float.IsFinite(v) && v >= 0 && v <= 1), "Sprite.Pivot");
        GameDefinitions.Require(Enum.IsDefined(Billboard), "Sprite.Billboard");
    }
}

internal sealed record ArtStyleDefinition(string Id, SpriteImageDefinition[] Images);
internal sealed record WorldArtDefinition(string InitialStyle, SpriteLightingMode Lighting, float NormalStrength,
    float AlphaCutoff, float ObserverScale, CardinalDirection ObserverFacing, int[] BenchOffset, int[] CrateOffset,
    int[] ObserverOffset, float[] LanternOffset, float[][] LightOffsets, ArtStyleDefinition[] Styles)
{
    internal void Validate()
    {
        GameDefinitions.Require(Styles is { Length: > 0 } && Styles.All(s => s is not null && !string.IsNullOrWhiteSpace(s.Id))
            && Styles.Select(s => s.Id).Distinct().Count() == Styles.Length && Styles.Any(s => s.Id == InitialStyle), "Art styles");
        foreach (ArtStyleDefinition style in Styles)
        {
            GameDefinitions.Require(style.Images is { Length: > 0 } && style.Images.All(i => i is not null)
                && style.Images.Select(i => i.Id).Distinct().Count() == style.Images.Length, "Style images");
            foreach (SpriteImageDefinition image in style.Images) image.Validate();
            foreach (string id in new[] { "bench", "crate", "lantern", "sentry-front", "sentry-right", "sentry-back", "sentry-left" })
                GameDefinitions.Require(style.Images.Any(i => i.Id == id), "Missing sprite " + id);
        }
        GameDefinitions.Require(Lighting is SpriteLightingMode.Synthetic or SpriteLightingMode.DerivedGradient, "Art.Lighting without authored maps");
        GameDefinitions.Require(float.IsFinite(NormalStrength) && NormalStrength >= 0, "Art.NormalStrength");
        GameDefinitions.Require(float.IsFinite(AlphaCutoff) && AlphaCutoff > 0 && AlphaCutoff < 1, "Art.AlphaCutoff");
        GameDefinitions.Require(float.IsFinite(ObserverScale) && ObserverScale > 0 && Enum.IsDefined(ObserverFacing), "Observer scale/facing");
        foreach (int[] offset in new[] { BenchOffset, CrateOffset, ObserverOffset })
            GameDefinitions.Require(offset is { Length: 2 } && offset.All(v => Math.Abs((long)v) <= GenerationPolicyValidation.MaxDimension), "Dressing offset");
        GameDefinitions.Require(LanternOffset is { Length: 3 } && LanternOffset.All(float.IsFinite), "Lantern anchor");
        GameDefinitions.Require(LightOffsets is { Length: > 0 } && LightOffsets.All(o => o is { Length: 3 } && o.All(float.IsFinite)), "Light positions");
    }
}

/// <summary>View selection is game policy; Engine owns the billboard and its shading.</summary>
internal static class SentryView
{
    internal static string Select(Vector2 actor, CardinalDirection facing, Vector2 viewer)
    {
        GridPoint forward = facing.Offset();
        Vector2 delta = viewer - actor;
        float front = delta.X * forward.X + delta.Y * forward.Y;
        float right = delta.X * -forward.Y + delta.Y * forward.X;
        if (Math.Abs(front) >= Math.Abs(right)) return front >= 0 ? "sentry-front" : "sentry-back";
        return right >= 0 ? "sentry-right" : "sentry-left";
    }
}
