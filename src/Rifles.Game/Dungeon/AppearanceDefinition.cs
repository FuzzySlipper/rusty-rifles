using System.Numerics;
using Rusty.Engine;
using Rifles.Game.Content;
namespace Rifles.Game.Dungeon;

internal sealed record SurfaceDefinition(float[] Color, float Roughness, float[] Emission)
{
    internal MaterialRequest Request() => new(new Color(Color[0], Color[1], Color[2], Color[3]),
        new RenderResourceHandle(0), Roughness, new Color(1, 1, 1, 1),
        new Vector3(Emission[0], Emission[1], Emission[2]), 0, false);
    internal void Validate(string name)
    {
        GameDefinitions.Require(Color is { Length: 4 } && Color.All(v => float.IsFinite(v) && v >= 0 && v <= 1), name + ".Color");
        GameDefinitions.Require(float.IsFinite(Roughness) && Roughness >= 0 && Roughness <= 1, name + ".Roughness");
        GameDefinitions.Require(Emission is { Length: 3 } && Emission.All(v => float.IsFinite(v) && v >= 0), name + ".Emission");
    }
}
internal sealed record AppearanceDefinition(SurfaceDefinition Stone, SurfaceDefinition Exit,
    float[] LightColor, float LightIntensity, float LightRange)
{
    internal void Validate()
    {
        GameDefinitions.Require(Stone is not null && Exit is not null, "Stone/Exit");
        Stone!.Validate(nameof(Stone)); Exit!.Validate(nameof(Exit));
        GameDefinitions.Require(LightColor is { Length: 3 } && LightColor.All(v => float.IsFinite(v) && v >= 0), nameof(LightColor));
        GameDefinitions.Require(float.IsFinite(LightIntensity) && LightIntensity >= 0, nameof(LightIntensity));
        GameDefinitions.Require(float.IsFinite(LightRange) && LightRange > 0, nameof(LightRange));
    }
}
