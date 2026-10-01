using Rifles.Game.Content;
using System.Numerics;

namespace Rifles.Game.Presentation;

internal sealed record MusketeerDefinition(string Enemy, string Body, string Weapon, string Joint,
    float HeightScale, float FacingOffsetDegrees, float[] GripPosition, float[] GripRotation, float[] GripScale,
    string IdleClip, string WalkClip, string FireClip, string DefeatClip, float FirePose,
    float[] MuzzleOffset, MusketBurstDefinition Flash, MusketBurstDefinition Smoke)
{
    internal void Validate()
    {
        GameDefinitions.Require(new[] { Enemy, Body, Weapon, Joint, IdleClip, WalkClip, FireClip, DefeatClip }.All(x => !string.IsNullOrWhiteSpace(x)), "musketeer paths, identity, joint and clips");
        GameDefinitions.Require(float.IsFinite(HeightScale) && HeightScale > 0 && float.IsFinite(FacingOffsetDegrees), "musketeer scale and facing");
        GameDefinitions.Require(GripPosition.Length == 3 && GripRotation.Length == 4 && GripScale.Length == 3 && MuzzleOffset.Length == 3
            && GripPosition.Concat(GripRotation).Concat(GripScale).Concat(MuzzleOffset).All(float.IsFinite)
            && GripScale.All(x => x > 0), "musketeer grip and muzzle transform");
        GameDefinitions.Require(MathF.Abs(new Quaternion(GripRotation[0], GripRotation[1], GripRotation[2], GripRotation[3]).Length() - 1) < .001f,
            "musketeer grip quaternion must be normalized");
        GameDefinitions.Require(float.IsFinite(FirePose) && FirePose >= 0 && FirePose <= 1, "musketeer fire pose");
        Flash.Validate(); Smoke.Validate();
    }
}

internal sealed record MusketBurstDefinition(uint Count, float LifetimeMin, float LifetimeMax,
    float[] VelocityMin, float[] VelocityMax, float[] Acceleration, float StartSize, float EndSize,
    float[] StartColor, float[] EndColor)
{
    internal void Validate()
    {
        GameDefinitions.Require(Count > 0 && float.IsFinite(LifetimeMin) && float.IsFinite(LifetimeMax)
            && LifetimeMin > 0 && LifetimeMax >= LifetimeMin, "musket particle count and lifetime");
        GameDefinitions.Require(VelocityMin.Length == 3 && VelocityMax.Length == 3 && Acceleration.Length == 3
            && StartColor.Length == 4 && EndColor.Length == 4
            && VelocityMin.Concat(VelocityMax).Concat(Acceleration).Concat(StartColor).Concat(EndColor).All(float.IsFinite)
            && Enumerable.Range(0, 3).All(i => VelocityMax[i] >= VelocityMin[i]), "musket particle vectors and colors");
        GameDefinitions.Require(float.IsFinite(StartSize) && float.IsFinite(EndSize) && StartSize > 0 && EndSize >= 0,
            "musket particle size");
    }
}
