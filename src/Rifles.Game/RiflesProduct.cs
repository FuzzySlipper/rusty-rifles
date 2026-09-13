using System.Text;
using Rifles.Game.Content;
using Rifles.Game.Expedition;
using Rusty.Engine.Persistence;
using Rusty.Engine.Interaction;
using Rusty.Engine;
using Rifles.Game.Dungeon;
using Rifles.Game.Party;
using Rifles.Game.Presentation;

namespace Rifles.Game;

public sealed class RiflesProduct : IEngineProduct
{
    private readonly GameDefinitions definitions;
    private readonly IEngineContext engine;
    private DungeonFloor floor;
    private ProductStateStore<ExpeditionSnapshot>? saves;
    private Guid expeditionId = Guid.NewGuid();
    private ulong nextLightId = 1;
    private ulong floorId = 1, partyId = 2, nextObjectId = 3;
    private readonly ExplorationInput controls = new();
    private ulong commandRevision = 1;
    private string selectedMember = "";
    private bool cameraCut = true;
    private string feedback = "Expedition ready";
    private ExplorationState exploration;
    private PartyState party;
    private DungeonScene? scene;
    private MovementGrid? movement;
    private PatrolActor? actor;
    private WorldFeatures? features;
    private Camera? camera;
    private SessionProjection? projection;
    private bool started, paused, shutdown;

    public RiflesProduct(ProductCreateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        engine = context.Engine;
        try { definitions = GameDefinitions.Load(engine); }
        catch (Exception error) { Console.Error.WriteLine("Rifles content admission failed: " + error); throw; }
        party = new PartyState(definitions.Party.Members);
        selectedMember = party.Members[0].Definition.Id;
        floor = DungeonFloor.Generate(definitions.Generation.Seed, definitions.Generation);
        exploration = new ExplorationState(floor.Entrance, definitions.Exploration);
    }

    public void Start()
    {
        if (started || shutdown) return;
        try
        {
            saves = new ProductStateStore<ExpeditionSnapshot>(engine, "expedition", new ExpeditionCodec());
            scene = new DungeonScene(engine, floor, definitions.Exploration, definitions.Appearance, AllocateLightId);
            actor = PatrolActor.Create(AllocateId(), floor, ActorTuning, definitions.Features);
            features = new WorldFeatures(engine, scene, floor, definitions.Features, new FeatureSnapshot(AllocateId(), AllocateId(), 1, true, false), AllocateLightId());
            BindMovement();
            camera = engine.CameraView.CreateCamera(CameraDescriptor());
            projection = new SessionProjection(engine.Ui);
            engine.CameraView.SetActiveCamera(camera);
            started = true;
            Publish();
        }
        catch (Exception error) { Console.Error.WriteLine("Rifles startup failed: " + error); Shutdown(); throw; }
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
        if (!started || shutdown) return ProductUpdateResult.None;
        controls.Clear();
        bool suppressMovement = paused;
        foreach (ProductInputEvent input in update.Input)
        {
            if (input.Kind == InputEventKind.Clear) { controls.Clear(); suppressMovement = true; continue; }
            string intent = Encoding.UTF8.GetString(input.Intent.Span);
            if (input.Phase == InputPhase.Released) continue;
            if (intent == "rifles.command")
            {
                try { Command(SessionCommand.Parse(input.PayloadData.Span)); }
                catch (Exception error) { feedback = "Command rejected: " + error.Message; }
                suppressMovement = true;
                continue;
            }
            if (intent == "rifles.pause") { SetPaused(!paused); suppressMovement = true; continue; }
            if (intent == "rifles.save") { Save(); suppressMovement = true; continue; }
            if (intent == "rifles.load") { Load(); suppressMovement = true; continue; }
            if (intent == "rifles.use") { Use(features!.Readout?.Selected); suppressMovement = true; continue; }
            if (intent == "rifles.cycle") { features!.Observe(exploration, 1); continue; }
            ExplorationAction? action = intent switch
            {
                "rifles.forward" => ExplorationAction.Forward, "rifles.backward" => ExplorationAction.Backward,
                "rifles.strafe-left" => ExplorationAction.StrafeLeft, "rifles.strafe-right" => ExplorationAction.StrafeRight,
                "rifles.turn-left" => ExplorationAction.TurnLeft, "rifles.turn-right" => ExplorationAction.TurnRight,
                _ => null,
            };
            if (action is { } admitted) controls.Observe(admitted);
        }
        if (!paused)
        {
            double seconds = update.Facts.AdmittedStepCount * update.Facts.FixedDeltaSeconds;
            exploration.Advance(seconds);
            actor!.Advance(seconds);
            if (!suppressMovement) controls.Apply(exploration);
        }
        Publish();
        return ProductUpdateResult.None;
    }

    private void Command(SessionCommand command)
    {
        if (command.Revision != commandRevision.ToString(System.Globalization.CultureInfo.InvariantCulture))
        { feedback = "Command expired; try again"; return; }
        commandRevision = checked(commandRevision + 1);
        switch (command.Action)
        {
            case "select":
                if (!party.Members.Any(m => m.Definition.Id == command.Member)) { feedback = "Member unavailable"; break; }
                selectedMember = command.Member!; feedback = "Selected " + party.Members.Single(m => m.Definition.Id == selectedMember).Definition.Name; break;
            case "pause": SetPaused(!paused); break;
            case "save": Save(); break;
            case "load": Load(); break;
            case "restart": Restart(); break;
            case "use": Use(command.Target is { } target && command.TargetRevision is { } revision ? new InteractionTarget(target, revision) : null); break;
            default: feedback = "Unknown command"; break;
        }
    }
    private void SetPaused(bool value)
    {
        paused = value; controls.Clear(); commandRevision = checked(commandRevision + 1);
        feedback = paused ? "Paused" : "Resumed";
    }
    public void Pause() { if (started && !shutdown) { SetPaused(true); Publish(); } }
    public void Resume() { if (started && !shutdown) { SetPaused(false); Publish(); } }
    public void Restart()
    {
        if (!started || shutdown) return;
        exploration = new ExplorationState(floor.Entrance, definitions.Exploration);
        party = new PartyState(definitions.Party.Members);
        selectedMember = party.Members[0].Definition.Id;
        expeditionId = Guid.NewGuid();
        actor = PatrolActor.Create(actor!.Id, floor, ActorTuning, definitions.Features);
        features!.Reset();
        BindMovement(); controls.Clear(); cameraCut = true; commandRevision = checked(commandRevision + 1);
        paused = false; feedback = "Expedition restarted";
        Publish();
    }

    private ExpeditionSnapshot Capture() => new(expeditionId, floorId, partyId, nextObjectId,
        floor, exploration.Capture(), party.Members.Select(m => m.Definition).ToArray(), party.Capture().ToArray(), paused, selectedMember, actor!.Capture(), features!.Capture());

    private void Save()
    {
        try
        {
            ExpeditionSnapshot saved = Capture();
            PersistenceSaveReceipt receipt = saves!.Save("current", saved);
            if (receipt.Outcome != PersistenceSaveOutcome.Saved) throw new InvalidOperationException(receipt.Outcome.ToString());
            feedback = "Expedition saved";
        }
        catch (Exception error) { feedback = "Save failed: " + error.Message; }
    }

    private void Load()
    {
        try
        {
            ProductStateLoad<ExpeditionSnapshot> loaded = saves!.Load("current");
            if (!loaded.Present) { feedback = "No saved expedition"; return; }
            ExpeditionSnapshot saved = loaded.State!;
            var restored = ExpeditionCodec.Validate(saved, definitions);
            DungeonScene replacement = new(engine, saved.Floor, definitions.Exploration, definitions.Appearance, AllocateLightId);
            MovementGrid replacementGrid = new(saved.Floor.Cells.ToHashSet(), replacement.AdmitStep);
            try { restored.Exploration.Bind(replacementGrid, saved.PartyId); restored.Actor.Bind(replacementGrid); }
            catch { replacement.Dispose(); throw; }
            WorldFeatures replacementFeatures;
            try { replacementFeatures = new WorldFeatures(engine, replacement, saved.Floor, definitions.Features, saved.Features, AllocateLightId()); }
            catch { replacement.Dispose(); throw; }
            // Retire old appearance references before releasing their Engine resources.
            replacementFeatures.Present(restored.Actor);
            WorldFeatures? previousFeatures = features;
            features = replacementFeatures; actor = restored.Actor;
            DungeonScene? previous = scene;
            movement = replacementGrid;
            scene = replacement; floor = saved.Floor; exploration = restored.Exploration; party = restored.Party;
            expeditionId = saved.Id; floorId = saved.FloorId; partyId = saved.PartyId; nextObjectId = saved.NextObjectId;
            paused = saved.Paused;
            selectedMember = saved.SelectedMember;
            controls.Clear(); cameraCut = true; commandRevision = checked(commandRevision + 1);
            previousFeatures?.Dispose(); previous?.Dispose();
            feedback = "Expedition restored";
        }
        catch (Exception error) { feedback = "Load rejected: " + error.Message; }
    }

    // Light IDs identify live presentation resources, not saved gameplay objects.
    private ulong AllocateLightId() => checked(nextLightId++);
    private ExplorationTuning ActorTuning => definitions.Exploration with { StepSeconds = definitions.Features.ActorStepSeconds };
    private ulong AllocateId() { if (nextObjectId > uint.MaxValue) throw new InvalidOperationException("Expedition object identity space exhausted."); ulong id = nextObjectId; nextObjectId = checked(nextObjectId + 1); return id; }
    private void Use(InteractionTarget? target)
    {
        feedback = paused ? "Resume before using world features" : features!.Use(exploration, target);
        commandRevision = checked(commandRevision + 1);
    }
    private void BindMovement()
    {
        movement = new MovementGrid(floor.Cells.ToHashSet(), scene!.AdmitStep);
        exploration.Bind(movement, partyId);
        actor!.Bind(movement);
    }

    private CameraDescriptor CameraDescriptor() => new(
        new CameraPose(scene!.Eye(exploration.VisualCell), 0, exploration.VisualYaw),
        CameraBasisMode.Derived, default,
        new CameraProjection(CameraProjectionKind.Perspective, definitions.Exploration.FieldOfView, 0, definitions.Exploration.NearDistance, definitions.Exploration.FarDistance),
        new CameraViewport(0, 0, 1, 1));

    private void Publish()
    {
        engine.CameraView.UpdateCameraSample(new CameraSampleRequest(camera!, CameraDescriptor(),
            exploration.ElapsedSeconds, definitions.Exploration.CameraDelay, CameraInterpolation.Pose, cameraCut ? (byte)1 : (byte)0));
        cameraCut = false;
        features!.Observe(exploration);
        features.Present(actor!);
        projection!.Publish(floor, exploration, party, paused, feedback, selectedMember, commandRevision, features.Readout);
    }

    public void Shutdown()
    {
        if (shutdown) return;
        shutdown = true;
        if (camera is not null) engine.CameraView.ClearActiveCamera(new ClearActiveCameraRequest(0));
        engine.Graphics.PublishSnapshot(ReadOnlySpan<AppearanceFact>.Empty);
        features?.Dispose();
        saves?.Dispose();
        projection?.Dispose();
        camera?.Dispose();
        scene?.Dispose();
    }
    public void Dispose() => Shutdown();
}
