using System.Text;
using Rifles.Game.Content;
using Rusty.Engine;
using Rifles.Game.Dungeon;
using Rifles.Game.Party;
using Rifles.Game.Presentation;

namespace Rifles.Game;

public sealed class RiflesProduct : IEngineProduct
{
    private readonly GameDefinitions definitions;
    private readonly IEngineContext engine;
    private readonly DungeonFloor floor;
    private ExplorationState exploration;
    private PartyState party;
    private DungeonScene? scene;
    private Camera? camera;
    private SessionProjection? projection;
    private bool started, paused, shutdown;

    public RiflesProduct(ProductCreateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        engine = context.Engine;
        definitions = GameDefinitions.Load(engine);
        party = new PartyState(definitions.Party.Members);
        floor = DungeonFloor.Generate(definitions.Generation.Seed, definitions.Generation);
        exploration = new ExplorationState(floor.Entrance, definitions.Exploration);
    }

    public void Start()
    {
        if (started || shutdown) return;
        try
        {
            scene = new DungeonScene(engine, floor, definitions.Exploration, definitions.Appearance);
            camera = engine.CameraView.CreateCamera(CameraDescriptor());
            projection = new SessionProjection(engine.Ui);
            engine.CameraView.SetActiveCamera(camera);
            started = true;
            Publish();
        }
        catch { Shutdown(); throw; }
    }

    public void Attach()
    {
        if (!started || shutdown) return;
        scene!.Attach();
        engine.CameraView.SetActiveCamera(camera!);
        Publish();
    }

    public ProductUpdateResult Update(ProductUpdate update)
    {
        if (!started || paused || shutdown) return ProductUpdateResult.None;
        exploration.Advance(update.Facts.AdmittedStepCount * update.Facts.FixedDeltaSeconds);
        foreach (ProductInputEvent input in update.Input)
        {
            ExplorationAction? action = Encoding.UTF8.GetString(input.Intent.Span) switch
            {
                "rifles.forward" => ExplorationAction.Forward,
                "rifles.backward" => ExplorationAction.Backward,
                "rifles.strafe-left" => ExplorationAction.StrafeLeft,
                "rifles.strafe-right" => ExplorationAction.StrafeRight,
                "rifles.turn-left" => ExplorationAction.TurnLeft,
                "rifles.turn-right" => ExplorationAction.TurnRight,
                _ => null,
            };
            if (action is { } admitted) exploration.Act(admitted, scene!.AdmitStep);
        }
        Publish();
        return ProductUpdateResult.None;
    }

    public void Pause() { if (started && !shutdown) { paused = true; Publish(); } }
    public void Resume() { if (started && !shutdown) { paused = false; Publish(); } }
    public void Restart()
    {
        if (!started || shutdown) return;
        exploration = new ExplorationState(floor.Entrance, definitions.Exploration);
        party = new PartyState(definitions.Party.Members);
        paused = false;
        Publish();
    }

    private CameraDescriptor CameraDescriptor() => new(
        new CameraPose(scene!.Eye(exploration.Position), 0, (int)exploration.Facing * 90d),
        CameraBasisMode.Derived, default,
        new CameraProjection(CameraProjectionKind.Perspective, definitions.Exploration.FieldOfView, 0, definitions.Exploration.NearDistance, definitions.Exploration.FarDistance),
        new CameraViewport(0, 0, 1, 1));

    private void Publish()
    {
        engine.CameraView.UpdateCameraSample(new CameraSampleRequest(camera!, CameraDescriptor(),
            exploration.ElapsedSeconds, definitions.Exploration.CameraDelay, CameraInterpolation.Latest, 1));
        projection!.Publish(floor, exploration, party, paused);
    }

    public void Shutdown()
    {
        if (shutdown) return;
        shutdown = true;
        if (camera is not null) engine.CameraView.ClearActiveCamera(new ClearActiveCameraRequest(0));
        projection?.Dispose();
        camera?.Dispose();
        scene?.Dispose();
    }
    public void Dispose() => Shutdown();
}
