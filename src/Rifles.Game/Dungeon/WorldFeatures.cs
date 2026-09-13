using Rifles.Procgen.Generation;
using System.Numerics;
using Rifles.Game.Content;
using Rusty.Engine;
using Rusty.Engine.Interaction;
namespace Rifles.Game.Dungeon;

internal sealed record FeatureSnapshot(ulong LanternId, ulong ExitId, ulong LanternRevision, bool LanternOn, bool ExitUsed, RoomDressing Dressing);
internal sealed class WorldFeatures : IDisposable
{
    private readonly IEngineContext engine;
    private readonly DungeonScene scene;
    private readonly DungeonFloor floor;
    private readonly FeatureDefinition tuning;
    private readonly WorldArt art;
    private readonly WorldArtDefinition artDefinition;
    private readonly Light lanternLight;
    private readonly ulong lightId;
    private readonly InteractionFocus focus = new();
    private FeatureSnapshot state;
    private InteractionCandidate[] extraCandidates = [];
    internal void SetExtraCandidates(IEnumerable<InteractionCandidate> candidates) => extraCandidates = candidates.ToArray();
    private int lightPosition;
    internal string Style { get; private set; }
    internal int LightPosition => lightPosition;
    internal FeatureSnapshot Capture() => state;
    internal InteractionReadout? Readout { get; private set; }
    private Vector3 Ground(GridPoint cell) => scene.Eye(cell) with { Y = scene.GroundHeight };
    private Vector3 LanternPoint => Ground(floor.Entrance) + Vector(artDefinition.LanternOffset);
    private Vector3 LightPoint => Ground(floor.Entrance) + Vector(artDefinition.LightOffsets[lightPosition]);
    private static Vector3 Vector(float[] values) => new(values[0], values[1], values[2]);
    internal WorldFeatures(IEngineContext engine, GeneratedArt artResources, DungeonScene scene, DungeonFloor floor, FeatureDefinition tuning, WorldArtDefinition artDefinition, FeatureSnapshot state, ulong lightId, string? style = null)
    {
        this.engine = engine; this.scene = scene; this.floor = floor; this.tuning = tuning; this.state = state; this.lightId = lightId;
        this.artDefinition = artDefinition;
        Style = style ?? artDefinition.InitialStyle;
        art = new WorldArt(engine, artResources, artDefinition);
        try { lanternLight = engine.Graphics.CreateLight(LightRequest()); }
        catch { art.Dispose(); throw; }
    }
    private LightRequest LightRequest() => new(lightId, false, 0,
        new LightDescriptor(LightKind.Point, Vector(tuning.LanternColor), state.LanternOn ? tuning.LightIntensity : 0,
            true, LightPoint, Vector3.UnitY, true, tuning.LightRange, 1, 0, 0, LightShadowIntent.Disabled));
    internal void SetStyle(string style)
    {
        if (!artDefinition.Styles.Any(s => s.Id == style)) throw new InvalidDataException("Unknown art treatment.");
        Style = style;
    }
    internal void CycleLight()
    {
        lightPosition = (lightPosition + 1) % artDefinition.LightOffsets.Length;
        engine.Graphics.UpdateLight(new LightUpdateRequest(lanternLight, LightRequest()));
    }
    internal void Bind(MovementGrid grid, bool observerAlive = true) => state.Dressing.Bind(grid, observerAlive);
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
        .. extraCandidates,
        Candidate(new(state.Dressing.ObserverId, 1), "Sentry — inspect", Ground(state.Dressing.Observer) + Vector3.UnitY, party, true),
    ];
    private InteractionCandidate Candidate(InteractionTarget target, string label, Vector3 point, ExplorationState party, bool available) =>
        new(target, label, point, tuning.Reach, scene.Visibility(scene.Eye(party.Position), point),
            available && !party.Moving ? InteractionAvailability.Available : InteractionAvailability.Unavailable);
    internal void Observe(ExplorationState party, int cycle = 0) => Readout = focus.Update(Candidates(party), Query(party), cycle);
    internal string Use(ExplorationState party, InteractionTarget? target, Func<InteractionTarget, string>? extraUse = null)
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
        if (target.Value.Id == state.Dressing.BenchId) return "A workbench with a hand plane and folded cloth.";
        if (target.Value.Id == state.Dressing.CrateId) return "A strapped storage crate. Item containers come later.";
        if (target.Value.Id == state.Dressing.ObserverId) return "A garrison ally stands watch. Friendly bodies block shots.";
        if (target.Value.Id != state.ExitId) return extraUse?.Invoke(target.Value) ?? "Feature unavailable";
        state = state with { ExitUsed = true };
        return "Exit inspected. This is the starting floor; expedition travel comes later.";
    }
    internal void Reset()
    {
        state = state with { LanternOn = true, ExitUsed = false, LanternRevision = 1 };
        lightPosition = 0;
        engine.Graphics.UpdateLight(new LightUpdateRequest(lanternLight, LightRequest()));
        focus.Clear();
    }
    internal void Present(PatrolActor actor, ExplorationState party, IEnumerable<AppearanceFact>? additional = null, float actorScale = 1, float observerScale = 1)
    {
        string actorView = SentryView.Select(actor.Motion.VisualCell, actor.Motion.Facing, party.VisualCell);
        Vector2 observerCell = new(state.Dressing.Observer.X, state.Dressing.Observer.Y);
        string observerView = SentryView.Select(observerCell, artDefinition.ObserverFacing, party.VisualCell);
        engine.Graphics.PublishSnapshot(new AppearanceFact[]
        {
            Fact(state.LanternId, LanternPoint, "lantern", 1),
            Fact(actor.Id, scene.Eye(actor.Motion.VisualCell) with { Y = scene.GroundHeight }, actorView, actorScale),
            Fact(state.Dressing.BenchId, Ground(state.Dressing.Bench), "bench", 1),
            Fact(state.Dressing.CrateId, Ground(state.Dressing.Crate), "crate", 1),
            Fact(state.Dressing.ObserverId, Ground(state.Dressing.Observer), observerView, artDefinition.ObserverScale * observerScale),
        }.Concat(additional ?? []).ToArray());
    }
    private AppearanceFact Fact(ulong id, Vector3 point, string image, float scale) =>
        new(id, false, 0, new Transform(point, Quaternion.Identity, new Vector3(scale)), art.Image(Style, image), true, RenderLayer.Scene);
    public void Dispose() { lanternLight.Dispose(); art.Dispose(); }
}
