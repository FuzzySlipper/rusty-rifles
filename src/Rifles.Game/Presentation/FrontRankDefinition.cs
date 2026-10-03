using System.Numerics;
using Rifles.Game.Content;

namespace Rifles.Game.Presentation;

internal sealed record FrontRankDefinition(bool Enabled, string Body, string Weapon, string Joint,
    float HeightScale, float FacingOffsetDegrees, float ForwardOffset, float RankSpacing, float LaneSpacing,
    float[] GripPosition, float[] GripRotation, float[] GripScale,
    string IdleClip, string WalkClip, string FireClip, float FirePose)
{
    internal void Validate()
    {
        GameDefinitions.Require(new[] { Body, Weapon, Joint, IdleClip, WalkClip, FireClip }
            .All(value => !string.IsNullOrWhiteSpace(value)), "front-rank model paths, joint and clips");
        GameDefinitions.Require(float.IsFinite(HeightScale) && HeightScale > 0
            && float.IsFinite(FacingOffsetDegrees) && float.IsFinite(ForwardOffset) && ForwardOffset > 0
            && float.IsFinite(RankSpacing) && RankSpacing > 0
            && float.IsFinite(LaneSpacing) && LaneSpacing > 0, "front-rank scale and placement");
        GameDefinitions.Require(GripPosition is { Length: 3 } && GripRotation is { Length: 4 }
            && GripScale is { Length: 3 } && GripPosition.Concat(GripRotation).Concat(GripScale).All(float.IsFinite)
            && GripScale.All(value => value > 0), "front-rank musket grip transform");
        GameDefinitions.Require(MathF.Abs(new Quaternion(GripRotation[0], GripRotation[1], GripRotation[2], GripRotation[3]).Length() - 1) < .001f,
            "front-rank grip quaternion must be normalized");
        GameDefinitions.Require(float.IsFinite(FirePose) && FirePose is >= 0 and <= 1, "front-rank fire pose");
    }
}
