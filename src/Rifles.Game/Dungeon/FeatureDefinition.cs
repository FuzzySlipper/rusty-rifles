using Rifles.Game.Content;
namespace Rifles.Game.Dungeon;
internal sealed record FeatureDefinition(string ActorLabel, int ActorOffsetCells, string LanternLabel, float[] LanternColor,
    float Reach, float QueryDistance, float AcquireAngle, float ReleaseAngle, float LightIntensity, float LightRange, double ActorStepSeconds)
{
    internal void Validate()
    {
        GameDefinitions.Require(!string.IsNullOrWhiteSpace(ActorLabel) && !string.IsNullOrWhiteSpace(LanternLabel), "Feature labels");
        foreach (float[] color in new[] { LanternColor })
            GameDefinitions.Require(color is { Length: 4 } && color.All(x => float.IsFinite(x) && x >= 0 && x <= 1), "Feature colors");
        GameDefinitions.Require(double.IsFinite(ActorStepSeconds) && ActorStepSeconds > 0, nameof(ActorStepSeconds));
        GameDefinitions.Require(ActorOffsetCells > 0, nameof(ActorOffsetCells));
        GameDefinitions.Require(float.IsFinite(Reach) && Reach > 0 && float.IsFinite(QueryDistance) && QueryDistance >= Reach, "Feature reach/query distance");
        GameDefinitions.Require(float.IsFinite(AcquireAngle) && AcquireAngle > 0 && float.IsFinite(ReleaseAngle) && ReleaseAngle >= AcquireAngle && ReleaseAngle <= MathF.PI, "Feature query angles");
        GameDefinitions.Require(float.IsFinite(LightIntensity) && LightIntensity >= 0 && float.IsFinite(LightRange) && LightRange > 0, "Feature lighting");
    }
}
