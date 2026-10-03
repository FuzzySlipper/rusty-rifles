using Rifles.Game.Content;
using Rifles.Procgen.Generation;
namespace Rifles.Game.Dungeon;

internal sealed record ExplorationTuning(double StepSeconds, double TurnSeconds, float CellSize,
    float EyeHeight, float CeilingHeight, uint ChunkSize, uint NavigationBudget, double FieldOfView,
    double NearDistance, double FarDistance, double CameraDelay, CardinalDirection InitialFacing)
{
    internal int CeilingBaseVoxels(int resolution) => checked(resolution + (int)MathF.Ceiling(CeilingHeight / (CellSize / resolution)));
    internal void Validate()
    {
        GameDefinitions.Require(double.IsFinite(StepSeconds) && StepSeconds > 0, nameof(StepSeconds));
        GameDefinitions.Require(double.IsFinite(TurnSeconds) && TurnSeconds > 0, nameof(TurnSeconds));
        GameDefinitions.Require(float.IsFinite(CellSize) && CellSize > 0, nameof(CellSize));
        GameDefinitions.Require(float.IsFinite(CeilingHeight) && CeilingHeight > 0
            && float.IsFinite(EyeHeight) && EyeHeight > 0 && EyeHeight < CeilingHeight, "EyeHeight/CeilingHeight");
        GameDefinitions.Require(ChunkSize > 0 && NavigationBudget > 0, "ChunkSize/NavigationBudget");
        GameDefinitions.Require(double.IsFinite(FieldOfView) && FieldOfView > 0 && FieldOfView < 180, nameof(FieldOfView));
        GameDefinitions.Require(double.IsFinite(NearDistance) && double.IsFinite(FarDistance) && NearDistance > 0 && FarDistance > NearDistance, "NearDistance/FarDistance");
        GameDefinitions.Require(double.IsFinite(CameraDelay) && CameraDelay >= 0, nameof(CameraDelay));
        GameDefinitions.Require(Enum.IsDefined(InitialFacing), nameof(InitialFacing));
    }
}
