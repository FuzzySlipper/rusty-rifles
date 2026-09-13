using Rifles.Procgen.Generation;
using System.Text;
using Rifles.Game.Combat;
using Rifles.Game.Content;
using Rifles.Game.Items;
using Rifles.Game.Expedition;
using Rusty.Engine.Persistence;
using Rusty.Engine.Interaction;
using Rusty.Engine;
using Rifles.Game.Dungeon;
using Rifles.Game.Party;
using Rifles.Game.Presentation;

namespace Rifles.Game;

public sealed partial class RiflesProduct : IEngineProduct
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
    private bool roomLights = true;

    public RiflesProduct(ProductCreateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        engine = context.Engine;
        try { definitions = GameDefinitions.Load(context.Content); }
        catch (Exception error) { Console.Error.WriteLine("Rifles content admission failed: " + error); throw; }
        // Engine closes renderer resource selection after Create; select every treatment here.
        foreach (TextureDefinition texture in definitions.Appearance.Styles.SelectMany(s => s.Textures))
            engine.Graphics.OpenResource(new RenderResourceRequest(texture.Path, TextureFilter.Linear, TextureWrap.Repeat));
        foreach (string path in definitions.Art.Styles.SelectMany(s => s.Images).Select(i => i.Path).Distinct())
            engine.Graphics.OpenResource(new RenderResourceRequest(path, TextureFilter.Linear, TextureWrap.Clamp));
        preset = string.IsNullOrEmpty(preset) ? definitions.Characters.DefaultPresetId : preset;
        party = new PartyState(definitions.Characters.GetPreset(preset));
        engine.Graphics.OpenResource(new RenderResourceRequest(definitions.ItemArt.Path, TextureFilter.Linear, TextureWrap.Clamp));
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
            scene = new DungeonScene(engine, floor, definitions.Exploration, definitions.Appearance, AllocateLightId, definitions.ItemExploration);
            actor = PatrolActor.Create(AllocateId(), floor, ActorTuning, definitions.Features);
            features = new WorldFeatures(engine, scene, floor, definitions.Features, definitions.Art, new FeatureSnapshot(AllocateId(), AllocateId(), 1, true, false,
                RoomDressing.Create(floor, actor, definitions.Art, AllocateId)), AllocateLightId());
            StartItems();
            itemArt = new ItemArt(engine, definitions.ItemArt, definitions.Art, definitions.ItemExploration);
            BindMovement();
            combatArt = new WorldArt(engine, definitions.Art);
            float[] boltColor = Combat.BoltColor;
            boltAppearance = engine.Graphics.CreatePrimitive(new PrimitiveAppearanceRequest(PrimitiveGeometry.Sphere, false,
                new Color(boltColor[0], boltColor[1], boltColor[2], boltColor[3])));
            StartCombat();
            camera = engine.CameraView.CreateCamera(CameraDescriptor());
            projection = new SessionProjection(engine.Ui);
            engine.CameraView.SetActiveCamera(camera);
            started = true;
            Publish();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Rifles startup failed: " + error);
            if (error is EngineCallException engineError)
                foreach (EngineDiagnostic diagnostic in engineError.Diagnostics.Span) Console.Error.WriteLine(diagnostic);
            Shutdown(); throw;
        }
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
            if (intent is "rifles.attack" or "rifles.reload")
            {
                try { BeginCombat(new SessionCommand(commandRevision.ToString(), intent == "rifles.attack" ? "attack" : "reload", null, null, null)); }
                catch (Exception error) { feedback = error.Message; }
                suppressMovement = true; continue;
            }
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
            for (uint step = 0; step < update.Facts.AdmittedStepCount; step++)
            {
                double seconds = update.Facts.FixedDeltaSeconds;
                GridPoint previousCell = exploration.Position;
                if (!Defeated) exploration.Advance(seconds);
                if (exploration.Position != previousCell) EmitNoise(exploration.Position, NoiseKind.Footstep);
                if (allies[actor!.Id].IsLiving) actor.Advance(seconds);
                AdvanceCombat(seconds);
            }
            if (!suppressMovement && !Defeated) controls.Apply(exploration);
        }
        itemWorld!.CheckOpen(exploration, scene!);
        bool wasOpen = itemWorld.Capture().DoorOpen;
        itemWorld.UpdateDoor(inventory!, exploration, actor!, movement!, scene!);
        if (!wasOpen && itemWorld.Capture().DoorOpen) EmitNoise(itemWorld.Capture().Door, NoiseKind.Alarm);
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
            case "transfer": case "equip": case "unequip": case "consume": case "item-feature":
                ItemCommand(command); break;
            case "target": case "attack": case "reload": case "bolt": case "throw": case "interrupt":
                BeginCombat(command); break;
            case "formation":
                feedback = party.SwapFormation(command.Member ?? selectedMember, command.OtherMember ?? "") ? "Formation changed" : "Choose two living members"; break;
            case "choose-party":
                _ = definitions.Characters.GetPreset(command.Preset ?? "");
                preset = command.Preset!; Restart(); break;
            case "open-container":
                if (command.Target != itemWorld!.Anchor("crate").Id || command.TargetRevision != itemWorld.Revision)
                    throw new InvalidDataException("Container target expired.");
                itemWorld.Open("crate", exploration, scene!); feedback = "Crate opened"; break;
            case "close-container": itemWorld!.Close(); feedback = "Crate closed"; break;
            case "select":
                if (!party.Members.Any(m => m.Definition.Id == command.Member)) { feedback = "Member unavailable"; break; }
                selectedMember = command.Member!; feedback = "Selected " + party.Members.Single(m => m.Definition.Id == selectedMember).Definition.Name; break;
            case "pause": SetPaused(!paused); break;
            case "save": Save(); break;
            case "load": Load(); break;
            case "restart": Restart(); break;
            case "art-style":
                string currentStyle = features!.Style;
                int index = Array.FindIndex(definitions.Art.Styles, s => s.Id == currentStyle);
                string nextStyle = definitions.Art.Styles[(index + 1) % definitions.Art.Styles.Length].Id;
                scene!.SetStyle(nextStyle); features.SetStyle(nextStyle);
                feedback = "Art treatment: " + nextStyle; break;
            case "art-light": features!.CycleLight(); feedback = "Light position " + (features.LightPosition + 1); break;
            case "art-fill": roomLights = !roomLights; scene!.SetRoomLights(roomLights); feedback = roomLights ? "Room lights on" : "Room fill disabled"; break;
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
        preset = string.IsNullOrEmpty(preset) ? definitions.Characters.DefaultPresetId : preset;
        party = new PartyState(definitions.Characters.GetPreset(preset));
        selectedMember = party.Members[0].Definition.Id;
        expeditionId = Guid.NewGuid();
        actor = PatrolActor.Create(actor!.Id, floor, ActorTuning, definitions.Features);
        features!.Reset();
        StartItems();
        BindMovement(); StartCombat(); controls.Clear(); cameraCut = true; commandRevision = checked(commandRevision + 1);
        paused = false; feedback = "Expedition restarted";
        Publish();
    }

    private ExpeditionSnapshot Capture() => new(expeditionId, floorId, partyId, nextObjectId,
        floor, exploration.Capture(), party.Members.Select(m => m.Definition).ToArray(), party.Capture().ToArray(), paused, selectedMember, actor!.Capture(), features!.Capture(), preset, inventory!.Capture(), itemWorld!.Capture(), CaptureCombat());

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
            ItemInventory restoredInventory = ItemInventory.Restore(definitions.Items, saved.Inventory);
            RestoredCombat restoredCombat = CombatRestore.Validate(saved.Combat, definitions, saved.Floor, restoredInventory, restored.Party, saved.PartyId,
                new[] { saved.Actor.Id, saved.Features.Dressing.ObserverId });
            ExplorationItems restoredItems = new(definitions.ItemExploration, saved.ItemWorld);
            DungeonScene replacement = new(engine, saved.Floor, definitions.Exploration, definitions.Appearance, AllocateLightId, definitions.ItemExploration);
            replacement.SetDoor(saved.ItemWorld.Door, saved.ItemWorld.DoorOpen);
            MovementGrid replacementGrid = new(saved.Floor.Cells.ToHashSet(), replacement.AdmitStep, definitions.Crowd);
            foreach (var direction in CardinalDirections.Ordered)
                if (saved.Floor.Cells.Contains(saved.ItemWorld.Door + direction.Offset()))
                    replacementGrid.SetClearance(saved.ItemWorld.Door, saved.ItemWorld.Door + direction.Offset(), Combat.DoorClearance);
            try
            {
                if (restored.Party.Members.Any(m => m.IsLiving)) restored.Exploration.Bind(replacementGrid, saved.PartyId);
                if (restoredCombat.Allies.Single(a => a.Id == saved.Actor.Id).Vitality > 0) restored.Actor.Bind(replacementGrid);
            }
            catch { replacement.Dispose(); throw; }
            WorldFeatures replacementFeatures;
            try { replacementFeatures = new WorldFeatures(engine, replacement, saved.Floor, definitions.Features, definitions.Art, saved.Features, AllocateLightId(), features!.Style); }
            catch { replacement.Dispose(); throw; }
            try
            {
                if (replacement.Style != features!.Style) replacement.SetStyle(features.Style);
                replacement.SetRoomLights(roomLights);
                replacementFeatures.Bind(replacementGrid, restoredCombat.Allies.Single(a => a.Id == saved.Features.Dressing.ObserverId).Vitality > 0);
                foreach (var ally in restoredCombat.Allies.Where(a => a.Vitality == 0)) replacementGrid.Remove(ally.Id);
                foreach (EnemyState enemy in restoredCombat.Enemies.Where(e => e.Alive)) enemy.Motion.Bind(replacementGrid, enemy.Id, enemy.Definition.Footprint, enemy.Definition.Faction, enemy.Definition.Share);
                // Retire old appearance references before releasing their Engine resources.
                replacementFeatures.Present(restored.Actor, restored.Exploration, itemArt!.Facts(restoredInventory, restoredItems, replacement));
            }
            catch
            {
                replacementFeatures.Dispose(); replacement.Dispose(); throw;
            }
            WorldFeatures? previousFeatures = features;
            features = replacementFeatures; actor = restored.Actor;
            DungeonScene? previous = scene;
            movement = replacementGrid;
            scene = replacement; floor = saved.Floor; exploration = restored.Exploration; party = restored.Party;
            expeditionId = saved.Id; floorId = saved.FloorId; partyId = saved.PartyId; nextObjectId = saved.NextObjectId;
            inventory = restoredInventory; itemWorld = restoredItems; preset = saved.Preset;
            ApplyEquipment();
            ApplyCombatRestore(restoredCombat);
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
        features!.SetExtraCandidates(ItemCandidates());
        feedback = paused ? "Resume before using world features" : features.Use(exploration, target, UseItemFeature);
        commandRevision = checked(commandRevision + 1);
    }
    private void BindMovement()
    {
        movement = new MovementGrid(floor.Cells.ToHashSet(), scene!.AdmitStep, definitions.Crowd);
        exploration.Bind(movement, partyId);
        actor!.Bind(movement);
        features!.Bind(movement);
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
        features!.SetExtraCandidates(ItemCandidates());
        features.Observe(exploration);
        features.Present(actor!, exploration, itemArt!.Facts(inventory!, itemWorld!, scene!).Concat(CombatFacts()),
            allies[actor!.Id].IsLiving ? 1 : Combat.CorpseScale,
            allies[features.Capture().Dressing.ObserverId].IsLiving ? 1 : Combat.CorpseScale);
        projection!.Publish(floor, exploration, party, paused, feedback, selectedMember, commandRevision, features.Readout, features.Style, roomLights, features.LightPosition, inventory!, itemWorld!, scene!, definitions.Characters, preset, actor!, definitions.ItemArt, CombatProjection, DropReachable);
    }

    public void Shutdown()
    {
        if (shutdown) return;
        shutdown = true;
        if (camera is not null) engine.CameraView.ClearActiveCamera(new ClearActiveCameraRequest(0));
        engine.Graphics.PublishSnapshot(ReadOnlySpan<AppearanceFact>.Empty);
        features?.Dispose();
        itemArt?.Dispose();
        combatArt?.Dispose();
        boltAppearance?.Dispose();
        saves?.Dispose();
        projection?.Dispose();
        camera?.Dispose();
        scene?.Dispose();
    }
    public void Dispose() => Shutdown();
}
