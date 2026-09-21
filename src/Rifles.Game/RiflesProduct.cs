using Rifles.Procgen.Expeditions;
using Rifles.Game.Audio;
using Rifles.Game.Debugging;
using Rusty.Engine.Debugging;
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

public sealed partial class RiflesProduct : IEngineProduct, IDebugCommandModuleSource, IDebugCommandModule
{
    private readonly RiflesUpdateProfile updateProfile = new();
    private readonly GameDefinitions definitions;
    private readonly IEngineContext engine;
    private GeneratedArt? generatedArt;
    private DungeonMaterialCache? dungeonMaterials;
    private GameAudio? audio;
    // The mounted live floor. Set by Start/Travel/Activate before any
    // command, update, or projection runs — the same guarantee the old
    // scene!/movement! sites relied on. Run-level state stays on product.
    private ActiveFloor active = null!;
    private string? artStyle;
    private void Mount(ActiveFloor next)
    {
        ActiveFloor? previous = active;
        active = next;
        artStyle = next.Features.Style;
        // Retire appearance references before releasing Engine resources.
        previous?.Dispose();
    }
    private PartyState party = null!;
    private ProductStateStore<RunSnapshot>? saves;
    private Guid expeditionId = Guid.NewGuid();
    private ulong nextLightId = 1;
    private ulong partyId = 2, nextObjectId = 3;
    private readonly ExplorationInput controls = new();
    private readonly HudPublication hudPublication = new();
    private ResolvedExpedition expedition;
    private string selectedMember = "";
    private bool cameraCut = true;
    private string feedback = "Expedition ready";
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
        progress = new(definitions.Run.DefaultDifficulty, false, []);
        preset = string.IsNullOrEmpty(preset) ? definitions.Characters.DefaultPresetId : preset;
        ExpeditionGenerationResult generated = new ExpeditionGenerator().Generate(definitions.Generation.Expedition, definitions.Generation.Seed);
        if (!generated.Accepted) throw new InvalidDataException("Expedition rejected: " + string.Join(", ", generated.Diagnostics.Select(d => d.Code + ": " + d.Detail)));
        expedition = generated.Expedition!;
        generatedArt = new GeneratedArt(context.Content, engine.Graphics,
            definitions.Appearance.Styles.SelectMany(s => s.Textures)
                .Select(texture => (texture.Path, TextureFilter.Linear, TextureWrap.Repeat))
                .Concat(definitions.Art.Styles.SelectMany(s => s.Images)
                    .Select(image => (image.Path, TextureFilter.Linear, TextureWrap.Clamp)))
                .Append((definitions.ItemArt.Path, TextureFilter.Linear, TextureWrap.Clamp)));
        dungeonMaterials = new DungeonMaterialCache(engine, generatedArt);
        try { audio = new GameAudio(context.Content, engine.Audio, definitions.Audio); }
        catch { dungeonMaterials.Dispose(); generatedArt.Dispose(); throw; }
    }

    public void RegisterDebugCommands(IDebugCommandModuleRegistrar registrar)
    {
        DebugCommandRegistrationResult result = registrar.Register(updateProfile);
        if (!result.Succeeded) throw new InvalidOperationException(result.Message);
        result = registrar.Register(this);
        if (!result.Succeeded) throw new InvalidOperationException(result.Message);
    }

    [DebugCommand("rifles.audio.read", Description = "Read Engine clip, signal and realization diagnostics without emitting audio.")]
    public string ReadAudio() => System.Text.Json.JsonSerializer.Serialize(new
    {
        State = engine.Audio.Read(),
        Realization = engine.Audio.ReadRealization(),
        Diagnostics = Enumerable.Range(0, checked((int)engine.Audio.Read().RetainedDiagnosticCount))
            .Select(index => engine.Audio.ReadDiagnosticAt(new((uint)index))).ToArray(),
    });

    [DebugCommand("rifles.expedition.read", Description = "Read the resolved expedition graph, floor roles and connectors stored with this run. Does not travel or regenerate.")]
    public string ReadExpedition() => System.Text.Json.JsonSerializer.Serialize(expedition,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });

    [DebugCommand("rifles.floor.read", Description = "Read the played floor cells, resolved room functions, architectural landmarks and thresholds.")]
    public string ReadFloor() => System.Text.Json.JsonSerializer.Serialize(active.Floor);

    [DebugCommand("rifles.floor.population", Description = "Read retained generated gate, key, hazard, supply and encounter placement facts.")]
    public string ReadFloorPopulation() => System.Text.Json.JsonSerializer.Serialize(new { Features = active.GeneratedFeatures, Encounters = active.EncounterPlacement },
        new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });

    [DebugCommand("rifles.floor.validate", Description = "Inspect actual floor connectivity, generated key acquisition and gate-handle reachability.")]
    public string ValidateFloor() => System.Text.Json.JsonSerializer.Serialize(
        Rifles.Game.Generation.FloorProgression.Inspect(active.Floor, active.GeneratedFeatures.Gates, active.GeneratedFeatures.Plates));

    [DebugCommand("rifles.floor.navigation", Description = "Compare a bounded set of resolved steps against live Engine navigation, including barriers and heights.")]
    public string InspectFloorNavigation(int maximumEdges)
    {
        var result = active.Scene.InspectNavigation(maximumEdges);
        return System.Text.Json.JsonSerializer.Serialize(new { result.Checked, result.Complete, result.Mismatches });
    }

    public void Start()
    {
        if (started || shutdown) return;
        try
        {
            saves = new ProductStateStore<RunSnapshot>(engine, "expedition", RunCodec.CreateStoreCodec());
            itemArt = new ItemArt(engine, generatedArt!, definitions.ItemArt, definitions.Art, definitions.ItemExploration);
            combatArt = new WorldArt(engine, generatedArt!, definitions.Art);
            float[] boltColor = Combat.BoltColor;
            boltAppearance = engine.Graphics.CreatePrimitive(new PrimitiveAppearanceRequest(PrimitiveGeometry.Sphere, false,
                new Color(boltColor[0], boltColor[1], boltColor[2], boltColor[3])));
            // The run party is built once here and travels across floors;
            // fresh floors mount directly with no snapshot roundtrip.
            party = new PartyState(definitions.Party.Positions, definitions.Party.MaxPartySize, definitions.Characters, preset);
            selectedMember = party.Members[0].Definition.Id;
            DungeonFloor firstFloor = DungeonFloor.Generate(expedition.Floors.Single(f => f.Id == expedition.EntranceFloor),
                definitions.Generation.Policy, definitions.Rooms, definitions.Generation.Elevation).WithArchitecture(definitions.Architecture);
            ulong next = nextObjectId;
            ulong Allocate()
            {
                if (next == 0 || next > uint.MaxValue) throw new InvalidOperationException("Expedition object identity space exhausted.");
                return checked(next++);
            }
            ulong firstFloorId = Allocate();
            if (firstFloorId == partyId) throw new InvalidDataException("Floor and party identities must differ.");
            ExplorationSnapshot pose = new(firstFloor.Entrance, definitions.Exploration.InitialFacing, 0, null,
                firstFloor.Entrance, definitions.Exploration.InitialFacing, 0);
            Mount(ActiveFloor.CreateFresh(definitions, expedition, expedition.EntranceFloor, firstFloorId, partyId, party, null, pose,
                engine, dungeonMaterials!, generatedArt!, AllocateLightId, Allocate, preset, artStyle,
                definitions.Run.Difficulty(progress.Difficulty).IncomingDamageMultiplier,
                CombatMessage, (cue, point) => audio!.Play(cue, point), CancelRest,
                GeneratedUseProblem, GeneratedFeaturePoint, (target, revision) => UseGeneratedFeature(new(target, revision)),
                (scene, cell) => AimOn(scene, cell, definitions.Combat.AimHeight), firstFloor));
            nextObjectId = next;
            spellLightId = AllocateLightId(); spellLight = engine.Graphics.CreateLight(SpellLightRequest());
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
        active.Scene.Attach();
        engine.CameraView.SetActiveCamera(camera!);
        Publish();
    }

    public ProductUpdateResult Update(ProductUpdate update)
    {
        if (!started || shutdown) return ProductUpdateResult.None;
        long updateStarted = updateProfile.Begin();
        long phaseStarted = updateStarted;
        controls.Clear();
        bool suppressMovement = paused;
        bool immediateHud = false;
        hudPublication.Advance(update.Facts.FixedDeltaSeconds * update.Facts.AdmittedStepCount);
        foreach (ProductInputEvent input in update.Input)
        {
            if (input.Kind == InputEventKind.Clear) { controls.Clear(); suppressMovement = true; continue; }
            string intent = Encoding.UTF8.GetString(input.Intent.Span);
            if (input.Phase == InputPhase.Released) continue;
            if (intent is "rifles.command" or "rifles.pause" or "rifles.save" or "rifles.load"
                or "rifles.use" or "rifles.cycle" or "rifles.attack" or "rifles.melee" or "rifles.fix-bayonets" or "rifles.unfix-bayonets") immediateHud = true;
            if (intent == "rifles.command")
            {
                try { Command(SessionCommand.Parse(input.PayloadData.Span)); }
                catch (InvalidDataException error) { feedback = "Command rejected: " + error.Message; }
                suppressMovement = true;
                continue;
            }
            if (intent == "rifles.pause") { SetPaused(!paused); suppressMovement = true; continue; }
            if (intent == "rifles.save") { Save(); suppressMovement = true; continue; }
            if (intent == "rifles.load") { Load(); suppressMovement = true; continue; }
            if (intent == "rifles.use") { Use(active.Features.Readout?.Selected); suppressMovement = true; continue; }
            if (intent == "rifles.cycle") { active.Features.Observe(active.Exploration, 1); continue; }
            if (intent is "rifles.attack" or "rifles.melee" or "rifles.fix-bayonets" or "rifles.unfix-bayonets")
            {
                try
                {
                    GameOutcome outcome = intent switch
                    {
                        "rifles.fix-bayonets" => active.Combat.BeginBayonetOrder(true, paused),
                        "rifles.unfix-bayonets" => active.Combat.BeginBayonetOrder(false, paused),
                        _ => active.Combat.BeginOrder(intent == "rifles.melee" ? CombatActionKind.Melee : CombatActionKind.Fire, paused),
                    };
                    ApplyOutcome(outcome);
                }
                catch (InvalidDataException error) { feedback = error.Message; }
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
        phaseStarted = updateProfile.Record(UpdatePhase.Input, phaseStarted);
        if (!paused && !progress.Completed)
        {
            for (uint step = 0; step < update.Facts.AdmittedStepCount; step++)
            {
                double seconds = update.Facts.FixedDeltaSeconds;
                GridPoint previousCell = active.Exploration.Position;
                if (!active.Combat.Defeated && !party.Formation.Executing) active.Exploration.Advance(seconds, PartySpeed);
                if (active.Exploration.Position != previousCell)
                {
                    active.Combat.EmitNoise(active.Exploration.Position, RiflesCombat.NoiseKind.Footstep);
                    var connector = active.Floor.Connectors.SingleOrDefault(c => c.From == previousCell && c.To == active.Exploration.Position);
                    if (connector is { Damage: > 0 })
                    {
                        foreach (var member in party.Members.Where(m => m.IsLiving)) active.Combat.DamageMember(member, connector.Damage);
                        CombatMessage("The party falls into the drain pit. Climb out by an adjacent step.");
                    }
                }
                if (active.Combat.Allies[active.Actor.Id].IsLiving) active.Actor.Advance(seconds);
                AdvanceCombat(seconds);
                AdvanceGeneratedHazards(seconds);
                party.Formation.Advance(seconds);
            }
            if (!suppressMovement && !active.Combat.Defeated && !party.Formation.Executing) controls.Apply(active.Exploration);
        }
        phaseStarted = updateProfile.Record(UpdatePhase.Simulation, phaseStarted);
        active.ItemWorld.CheckOpen(active.Exploration, active.Scene);
        bool wasOpen = active.ItemWorld.DoorOpen;
        active.ItemWorld.UpdateDoor(active.Inventory, active.Exploration, active.Actor, active.Grid, active.Scene);
        if (!wasOpen && active.ItemWorld.DoorOpen) active.Combat.EmitNoise(active.ItemWorld.Door, RiflesCombat.NoiseKind.Alarm);
        updateProfile.Record(UpdatePhase.WorldInteractions, phaseStarted);
        Publish(immediateHud);
        updateProfile.Record(UpdatePhase.TotalUpdate, updateStarted);
        return ProductUpdateResult.None;
    }

    private void ApplyOutcome(GameOutcome outcome)
    {
        if (!outcome.Accepted || outcome.Message.Length != 0) feedback = outcome.Message;
    }

    private void Command(SessionCommand command)
    {
        // No freshness gate: every command inspects actual state when acted
        // on, so independent queued actions from one projection all execute.
        GameOutcome outcome = command.Action switch
        {
            "transfer" or "equip" or "unequip" or "consume" or "item-feature" or "arrange" => ItemCommand(command),
            "attack" or "fire" => active.Combat.BeginOrder(CombatActionKind.Fire, paused),
            "melee" => active.Combat.BeginOrder(CombatActionKind.Melee, paused),
            "fix-bayonets" => active.Combat.BeginBayonetOrder(true, paused),
            "unfix-bayonets" => active.Combat.BeginBayonetOrder(false, paused),
            "target" or "reload" or "throw" or "interrupt" => active.Combat.BeginCombat(command, selectedMember, paused),
            "spell-select" or "spell-cancel" or "spell-assign" or "spell-hotbar"
                or "cast" or "ability" or "rest" or "rest-cancel" or "advance" => MagicCommand(command),
            "formation-open" or "formation-place" or "formation-execute" or "formation-cancel" => FormationCommand(command),
            "formation" or "move" => GameOutcome.Reject("Use Change formation to plan a repositioning order."),
            "choose-party" => ChooseParty(command.Preset ?? ""),
            "open-container" => OpenContainer(command.Target),
            "close-container" => CloseContainer(),
            "select" => SelectMember(command.Member),
            "pause" => TogglePaused(),
            "save" => SaveCommand(),
            "load" => LoadCommand(),
            "restart" => RestartCommand(),
            "complete" => CompleteCommand(),
            "new-run" => NewRunOutcome(command.Choice ?? ""),
            "travel" => TravelCommand(command.Choice ?? ""),
            "art-style" => CycleArtStyle(),
            "art-light" => CycleLightCommand(),
            "art-fill" => ToggleRoomLights(),
            "use" => UseCommand(command.Target is { } target && command.TargetRevision is { } revision ? new InteractionTarget(target, revision) : null),
            _ => GameOutcome.Reject("Unknown command"),
        };
        ApplyOutcome(outcome);
    }

    private GameOutcome ChooseParty(string presetId)
    {
        _ = definitions.Characters.GetPreset(presetId);
        preset = presetId; Restart();
        return GameOutcome.Accept();
    }

    private GameOutcome OpenContainer(ulong? target)
    {
        if (target != active.ItemWorld.Anchor("crate").Id) return GameOutcome.Reject("Choose the crate.");
        active.ItemWorld.Open("crate", active.Exploration, active.Scene);
        return GameOutcome.Accept("Crate opened");
    }

    private GameOutcome CloseContainer()
    {
        active.ItemWorld.Close();
        return GameOutcome.Accept("Crate closed");
    }

    private GameOutcome SelectMember(string? member)
    {
        if (!party.Members.Any(m => m.Definition.Id == member)) return GameOutcome.Reject("Member unavailable");
        selectedMember = member!;
        return GameOutcome.Accept("Selected " + party.Members.Single(m => m.Definition.Id == selectedMember).Definition.Name);
    }

    private GameOutcome TogglePaused()
    {
        SetPaused(!paused);
        return GameOutcome.Accept();
    }

    private GameOutcome SaveCommand()
    {
        Save();
        return GameOutcome.Accept();
    }

    private GameOutcome LoadCommand()
    {
        Load();
        return GameOutcome.Accept();
    }

    private GameOutcome RestartCommand()
    {
        Restart();
        return GameOutcome.Accept();
    }

    private GameOutcome CompleteCommand()
    {
        CompleteRun();
        return GameOutcome.Accept();
    }

    private GameOutcome NewRunOutcome(string choice)
    {
        NewRunCommand(choice);
        return GameOutcome.Accept();
    }

    private GameOutcome TravelCommand(string choice)
    {
        Travel(choice);
        return GameOutcome.Accept();
    }

    private GameOutcome CycleArtStyle()
    {
        string currentStyle = active.Features.Style;
        int index = Array.FindIndex(definitions.Art.Styles, s => s.Id == currentStyle);
        string nextStyle = definitions.Art.Styles[(index + 1) % definitions.Art.Styles.Length].Id;
        active.Scene.SetStyle(nextStyle); active.Features.SetStyle(nextStyle);
        return GameOutcome.Accept("Art treatment: " + nextStyle);
    }

    private GameOutcome CycleLightCommand()
    {
        active.Features.CycleLight();
        return GameOutcome.Accept("Light position " + (active.Features.LightPosition + 1));
    }

    private GameOutcome ToggleRoomLights()
    {
        roomLights = !roomLights; active.Scene.SetRoomLights(roomLights);
        return GameOutcome.Accept(roomLights ? "Room lights on" : "Room fill disabled");
    }

    private GameOutcome UseCommand(InteractionTarget? target)
    {
        Use(target);
        return GameOutcome.Accept();
    }
    private void SetPaused(bool value)
    {
        paused = value || party.Formation.Open || progress.Completed || active.Combat.Defeated; controls.Clear();
        feedback = paused ? "Paused" : "Resumed";
    }
    public void Pause() { if (started && !shutdown) { SetPaused(true); Publish(); } }
    public void Resume() { if (started && !shutdown) { SetPaused(false); Publish(); } }
    public void Restart()
    {
        if (!started || shutdown) return;
        try { StartNewRun(expedition.Seed); }
        catch (InvalidDataException error) { feedback = "Restart rejected: " + error.Message; }
        Publish();
    }

    private ExpeditionSnapshot Capture() => new(expeditionId, active.FloorId, partyId, nextObjectId,
        active.Floor, active.Exploration.Capture(), party.Members.Select(m => m.Definition).ToArray(), party.Capture().ToArray(), paused, selectedMember, active.Actor.Capture(), active.Features.Capture(), preset, active.Inventory.Capture(), active.ItemWorld.Capture(), active.Combat.Capture(), expedition, active.GeneratedFeatures, active.EncounterPlacement, party.RestRemaining, party.RestOwner, party.Formation.Capture());

    private void Save()
    {
        try
        {
            if (party.Formation.Open) throw new InvalidDataException("Execute or cancel the formation draft before saving.");
            RunSnapshot saved = CaptureRun();
            RunCodec.Validate(saved, definitions);
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
            ProductStateLoad<RunSnapshot> loaded = saves!.Load("current");
            if (!loaded.Present) { feedback = "No saved expedition"; return; }
            RunSnapshot run = loaded.State!;
            RunCodec.Validate(run, definitions);
            Activate(run.Active, RunCodec.Items(run));
            progress = run.Progress;
            mapObservation = null;
            inactiveFloors.Clear();
            foreach (var retained in run.Inactive) inactiveFloors.Add(retained.Floor.IntentFloorId, retained);
            feedback = "Expedition restored";
        }
        catch (Exception error) { feedback = "Load rejected: " + error.Message; }
    }

    private void Activate(ExpeditionSnapshot saved, IReadOnlyDictionary<ulong, string>? allItems = null)
    {
        Mount(ActiveFloor.Restore(saved, definitions, null, null, allItems,
            engine, dungeonMaterials!, generatedArt!, itemArt!, AllocateLightId, artStyle, roomLights,
            definitions.Run.Difficulty(progress.Difficulty).IncomingDamageMultiplier, partyId,
            CombatMessage, (cue, point) => audio!.Play(cue, point), CancelRest,
            GeneratedUseProblem, GeneratedFeaturePoint, (target, revision) => UseGeneratedFeature(new(target, revision)),
            AllocateId, (scene, cell) => AimOn(scene, cell, definitions.Combat.AimHeight)));
        party = active.Party;
        expedition = saved.Intent;
        expeditionId = saved.Id; partyId = saved.PartyId; nextObjectId = saved.NextObjectId;
        preset = saved.Preset;
        paused = saved.Paused || party.Defeated;
        selectedMember = saved.SelectedMember;
        controls.Clear(); cameraCut = true;
        feedback = "Expedition restored";
    }

    // Light IDs identify live presentation resources, not saved gameplay objects.
    private ulong AllocateLightId() => checked(nextLightId++);
    private ExplorationTuning ActorTuning => definitions.Exploration with { StepSeconds = definitions.Features.ActorStepSeconds };
    private ulong AllocateId() { if (nextObjectId > uint.MaxValue) throw new InvalidOperationException("Expedition object identity space exhausted."); ulong id = nextObjectId; nextObjectId = checked(nextObjectId + 1); return id; }
    private void Use(InteractionTarget? target)
    {
        if (target is { } stair && (stair.Id == active.Features.ExitId || stair.Id == active.FloorId))
        {
            var routes = Connections().Where(c => c.Forward == (stair.Id == active.Features.ExitId)).ToArray();
            if (routes.Length == 0 && stair.Id == active.Features.ExitId)
            {
                try { CompleteRun(); } catch (InvalidDataException error) { feedback = error.Message; }
                return;
            }
            if (routes.Length == 1)
            {
                try { Travel(routes[0].Link.Id); }
                catch (InvalidDataException error) { feedback = error.Message; }
                return;
            }
        }
        active.Features.SetExtraCandidates(ItemCandidates());
        feedback = paused ? "Resume before using world features" : active.Features.Use(active.Exploration, target, UseItemFeature);
    }


    private CameraDescriptor CameraDescriptor() => new(
        new CameraPose(active.Scene.Eye(active.Exploration.VisualCell), 0, active.Exploration.VisualYaw),
        CameraBasisMode.Derived, default,
        new CameraProjection(CameraProjectionKind.Perspective, definitions.Exploration.FieldOfView, 0, definitions.Exploration.NearDistance, definitions.Exploration.FarDistance),
        new CameraViewport(0, 0, 1, 1));

    private void Publish(bool immediateHud = true)
    {
        long phaseStarted = updateProfile.Begin();
        engine.CameraView.UpdateCameraSample(new CameraSampleRequest(camera!, CameraDescriptor(),
            active.Exploration.ElapsedSeconds, definitions.Exploration.CameraDelay, CameraInterpolation.Pose, cameraCut ? (byte)1 : (byte)0));
        cameraCut = false;
        UpdateSpellLight();
        phaseStarted = updateProfile.Record(UpdatePhase.CameraAndLight, phaseStarted);
        active.Features.SetExtraCandidates(ItemCandidates());
        active.Features.Observe(active.Exploration);
        phaseStarted = updateProfile.Record(UpdatePhase.FeatureFocus, phaseStarted);
        active.Features.Present(active.Actor, active.Exploration, itemArt!.Facts(active.Inventory, active.ItemWorld, active.Scene).Concat(CombatFacts()).Concat(GeneratedFeatureFacts()),
            active.Combat.Allies[active.Actor.Id].IsLiving ? 1 : Combat.CorpseScale,
            active.Combat.Allies[active.Features.Dressing.ObserverId].IsLiving ? 1 : Combat.CorpseScale);
        phaseStarted = updateProfile.Record(UpdatePhase.AppearancePublication, phaseStarted);
        if (updateProfile.UiProjectionEnabled && hudPublication.Take(definitions.Hud.RefreshSeconds, immediateHud)) projection!.Publish(active.Floor, active.Exploration, party, paused, feedback, selectedMember, active.Features.Readout, active.Features.Style, roomLights, active.Features.LightPosition, active.Inventory, active.ItemWorld, active.Scene, definitions.Characters, preset, active.Actor, definitions.ItemArt, CombatProjection, active.Combat.DropReachable, RunProjection, definitions.Formation);
        updateProfile.Record(UpdatePhase.UiProjection, phaseStarted);
    }

    public void Shutdown()
    {
        if (shutdown) return;
        shutdown = true;
        if (camera is not null) engine.CameraView.ClearActiveCamera(new ClearActiveCameraRequest(0));
        engine.Graphics.PublishSnapshot(ReadOnlySpan<AppearanceFact>.Empty);
        active.Features?.Dispose();
        itemArt?.Dispose();
        combatArt?.Dispose();
        boltAppearance?.Dispose();
        spellLight?.Dispose();
        saves?.Dispose();
        projection?.Dispose();
        camera?.Dispose();
        active.Scene?.Dispose();
        dungeonMaterials?.Dispose();
        generatedArt?.Dispose();
        audio?.Dispose();
    }
    public void Dispose() => Shutdown();
}
