using Rifles.Procgen.Generation;
using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Interaction;
namespace Rifles.Game.Dungeon;

internal sealed record FeatureSnapshot(ulong LanternId, ulong ExitId, ulong LanternRevision, bool LanternOn, bool ExitUsed);
internal sealed class WorldFeatures : IDisposable
{
    private readonly IEngineContext engine;
    private readonly DungeonScene scene;
    private readonly DungeonFloor floor;
    private readonly FeatureDefinition tuning;
    private readonly Appearance lanternAppearance, actorAppearance;
    private readonly Light lanternLight;
    private readonly ulong lightId;
    private readonly InteractionFocus focus = new();
    private FeatureSnapshot state;
    internal FeatureSnapshot Capture() => state;
    internal InteractionReadout? Readout { get; private set; }
    private Vector3 LanternPoint => scene.Eye(floor.Entrance) + Vector(tuning.LanternOffset);
    private static Vector3 Vector(float[] values) => new(values[0], values[1], values[2]);
    private static Color Color(float[] values) => new(values[0], values[1], values[2], values[3]);
    internal WorldFeatures(IEngineContext engine, DungeonScene scene, DungeonFloor floor, FeatureDefinition tuning, FeatureSnapshot state, ulong lightId)
    {
        this.engine = engine; this.scene = scene; this.floor = floor; this.tuning = tuning; this.state = state; this.lightId = lightId;
        lanternAppearance = engine.Graphics.CreatePrimitive(new PrimitiveAppearanceRequest(PrimitiveGeometry.Cube, false, Color(tuning.LanternColor)));
        try
        {
            actorAppearance = engine.Graphics.CreatePrimitive(new PrimitiveAppearanceRequest(PrimitiveGeometry.Cube, false, Color(tuning.ActorColor)));
            try { lanternLight = engine.Graphics.CreateLight(LightRequest()); }
            catch { actorAppearance.Dispose(); throw; }
        }
        catch { lanternAppearance.Dispose(); throw; }
    }
    private LightRequest LightRequest() => new(lightId, false, 0,
        new LightDescriptor(LightKind.Point, Vector(tuning.LanternColor), state.LanternOn ? tuning.LightIntensity : 0,
            true, LanternPoint, Vector3.UnitY, true, tuning.LightRange, 1, 0, 0, LightShadowIntent.Disabled));
    private InteractionQuery Query(ExplorationState party)
    {
        var direction = party.Facing.Offset();
        return new(scene.Eye(party.Position), new Vector3(direction.X, 0, direction.Y), tuning.AcquireAngle,
            tuning.ReleaseAngle, tuning.QueryDistance, tuning.QueryDistance);
    }
    private InteractionCandidate[] Candidates(ExplorationState party) =>
    [
        Candidate(new(state.LanternId, state.LanternRevision), tuning.LanternLabel + (state.LanternOn ? " — extinguish" : " — light"), LanternPoint, party, true),
        Candidate(new(state.ExitId, state.ExitUsed ? 2UL : 1UL), state.ExitUsed ? "Floor exit — inspected" : "Floor exit — inspect", scene.Eye(floor.Exit), party, true),
    ];
    private InteractionCandidate Candidate(InteractionTarget target, string label, Vector3 point, ExplorationState party, bool available) =>
        new(target, label, point, tuning.Reach, scene.Visibility(scene.Eye(party.Position), point),
            available && !party.Moving ? InteractionAvailability.Available : InteractionAvailability.Unavailable);
    internal void Observe(ExplorationState party, int cycle = 0) => Readout = focus.Update(Candidates(party), Query(party), cycle);
    internal string Use(ExplorationState party, InteractionTarget? target)
    {
        if (target is null) return "No reachable feature selected";
        InteractionReason reason = InteractionFocus.Revalidate(target.Value, Candidates(party), Query(party));
        if (reason != InteractionReason.Ready) return "Cannot use: " + reason;
        if (target.Value.Id == state.LanternId)
        {
            if (state.LanternRevision == uint.MaxValue) return "Lantern revision exhausted; restart the expedition";
            FeatureSnapshot previous = state;
            state = state with { LanternOn = !state.LanternOn, LanternRevision = checked(state.LanternRevision + 1) };
            try { engine.Graphics.UpdateLight(new LightUpdateRequest(lanternLight, LightRequest())); }
            catch { state = previous; throw; }
            return state.LanternOn ? "Lantern lit" : "Lantern extinguished";
        }
        state = state with { ExitUsed = true };
        return "Exit inspected. This is the starting floor; expedition travel comes later.";
    }
    internal void Reset()
    {
        state = state with { LanternOn = true, ExitUsed = false, LanternRevision = 1 };
        engine.Graphics.UpdateLight(new LightUpdateRequest(lanternLight, LightRequest()));
        focus.Clear();
    }
    internal void Present(PatrolActor actor)
    {
        engine.Graphics.PublishSnapshot(new AppearanceFact[]
        {
            new(state.LanternId, false, 0, new Transform(LanternPoint, Quaternion.Identity, Vector(tuning.LanternScale)), lanternAppearance, true, RenderLayer.Scene),
            new(actor.Id, false, 0, new Transform(scene.Eye(actor.Motion.VisualCell) with { Y = scene.GroundHeight + tuning.ActorScale[1] / 2 }, Quaternion.Identity, Vector(tuning.ActorScale)), actorAppearance, true, RenderLayer.Scene),
        });
    }
    public void Dispose() { lanternLight.Dispose(); actorAppearance.Dispose(); lanternAppearance.Dispose(); }
}
