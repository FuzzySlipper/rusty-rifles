using System.Text;
using Rusty.Engine;
using Rifles.Game.Dungeon;
using Rifles.Game.Party;
using Rifles.Game.Presentation;

namespace Rifles.Game;

public sealed class RiflesProduct : IEngineProduct
{
    private const ulong InitialSeed = 29;
    private const double FieldOfView = 75, NearDistance = .05, FarDistance = 250, CameraDelay = .05;
    private readonly IEngineContext engine;
    private readonly DungeonFloor floor;
    private ExplorationState exploration;
    private PartyState party = new();
    private DungeonScene? scene;
    private Camera? camera;
    private SessionProjection? projection;
    private bool started, paused, shutdown;

    public RiflesProduct(ProductCreateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        engine = context.Engine;
        floor = DungeonFloor.Generate(InitialSeed);
        exploration = new ExplorationState(floor.Entrance);
    }

    public void Start()
    {
        if (started || shutdown) return;
        try
        {
            scene = new DungeonScene(engine, floor);
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
        exploration = new ExplorationState(floor.Entrance);
        party = new PartyState();
        paused = false;
        Publish();
    }

    private CameraDescriptor CameraDescriptor() => new(
        new CameraPose(DungeonScene.Eye(exploration.Position), 0, (int)exploration.Facing * 90d),
        CameraBasisMode.Derived, default,
        new CameraProjection(CameraProjectionKind.Perspective, FieldOfView, 0, NearDistance, FarDistance),
        new CameraViewport(0, 0, 1, 1));

    private void Publish()
    {
        engine.CameraView.UpdateCameraSample(new CameraSampleRequest(camera!, CameraDescriptor(),
            exploration.ElapsedSeconds, CameraDelay, CameraInterpolation.Latest, 1));
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
