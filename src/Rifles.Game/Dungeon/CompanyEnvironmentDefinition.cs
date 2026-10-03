using Rifles.Game.Content;

namespace Rifles.Game.Dungeon;

/// <summary>Physical room/readability tuning for the larger company cells, using the existing art.</summary>
internal sealed record CompanyEnvironmentDefinition(int VoxelsPerCell, float LightRange, float LightIntensity,
    float AmbientIntensity, float InteractionReach, float QueryDistance, double ActorStepSeconds)
{
    internal void Validate()
    {
        GameDefinitions.Require(VoxelsPerCell is >= 1 and <= 4, "company voxel resolution");
        GameDefinitions.Require(new[] { LightRange, LightIntensity, AmbientIntensity, InteractionReach, QueryDistance }
            .All(value => float.IsFinite(value) && value > 0) && QueryDistance >= InteractionReach,
            "company lighting and interaction distances");
        GameDefinitions.Require(double.IsFinite(ActorStepSeconds) && ActorStepSeconds > 0, "company actor step time");
    }
}
