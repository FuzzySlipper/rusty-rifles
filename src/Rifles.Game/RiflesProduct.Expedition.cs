using Rifles.Game.Expedition;
using Rifles.Game.Dungeon;
using Rifles.Game.Items;
using Rifles.Game.Content;
using System.Text.Json;
using Rifles.Game.Presentation;
using Rifles.Game.Party;
using Rifles.Procgen.Expeditions;
using Rifles.Procgen.Generation;

namespace Rifles.Game;

public sealed partial class RiflesProduct
{
    private readonly Dictionary<string, RetainedFloor> inactiveFloors = [];
    private RunProgress progress;
    private RunSnapshot CaptureRun() => new(Capture(), inactiveFloors.Values.ToArray(), progress);
    private GameDefinitions FloorDefinitions(string difficulty) => definitions with { RouteSupplies = definitions.RouteSupplies with
        { AmmunitionAllowance = definitions.Run.Difficulty(difficulty).AmmunitionAllowance } };

    private IEnumerable<(ExpeditionConnector Link, string Destination, GridPoint Departure, bool Forward)> Connections()
    {
        foreach (var link in expedition.Connectors)
        {
            if (link.FromFloor == active.Floor.IntentFloorId) yield return (link, link.ToFloor, active.Floor.Exit, true);
            if (link.TwoWay && link.ToFloor == active.Floor.IntentFloorId) yield return (link, link.FromFloor, active.Floor.Entrance, false);
        }
    }

    private string? TravelProblem(GridPoint departure)
    {
        if (progress.Completed) return "Expedition complete.";
        if (active.Combat.Defeated) return "A living party is required.";
        if (paused) return "Resume before travelling.";
        if (active.Exploration.Position != departure) return "Stand on the marked stair to travel.";
        if (active.Exploration.Moving || active.Combat.ActionsBusy || active.Combat.HasFlights || party.RestRemaining > 0)
            return "Finish movement, actions and projectile flights before travelling.";
        return null;
    }

    private void Travel(string connector)
    {
        var options = Connections().Where(c => c.Link.Id == connector).ToArray();
        if (options.Length != 1) throw new InvalidDataException("Stair destination unavailable.");
        var route = options[0];
        if (TravelProblem(route.Departure) is { } problem) throw new InvalidDataException(problem);
        // Freeze the departing live floor as compact retained state, then hand
        // the travelling store over: members and the party pack keep their
        // entities, books, actions, and markers; floor-local keys retire.
        ExpeditionSnapshot current = Capture();
        RetainedFloor departed = RetainedFloor.Capture(current);
        string[] keep = [.. party.Members.Select(m => m.Definition.Id), ItemInventory.PartyKey];
        party.Entities.DetachAllExcept(keep);
        party.Entities.RemoveInventoryFacades(keep);
        // Travelling gameplay rides live identity: books stay attached, packs
        // restore by id, conditions re-register timing onto the kept markers.
        TravellingState travelling = new(active.Magic.Books,
            current.Combat.Magic!.Conditions.Where(c => !c.Target.StartsWith("enemy:", StringComparison.Ordinal)).ToArray(),
            current.Inventory.Packs.Where(p => InventoryOwner.Parse(p.Owner.Key) is (MemberOwner or PartyOwner)).ToArray(),
            current.Combat.LoadedWeapons.Where(current.Inventory.Packs.SelectMany(p => p.Items).Select(i => i.Id).ToHashSet().Contains).ToArray());
        ulong next = nextObjectId;
        ulong Allocate()
        {
            if (next == 0 || next > uint.MaxValue) throw new InvalidOperationException("Expedition object identity space exhausted.");
            return checked(next++);
        }
        // The travelling handoff mutates the shared store before the
        // destination builds, so a failed build rolls the departed floor back
        // from its freeze instead of leaving detached locals behind. Gameplay
        // rejections surface with their reason; programming failures propagate.
        ActiveFloor destination;
        RetainedFloor[] retained;
        try
        {
            RetainedFloor? frozen = inactiveFloors.GetValueOrDefault(route.Destination);
            if (frozen is not null)
            {
                // Returning floors thaw through the joined snapshot: the merge of
                // two live generations is validated, and the live party and books
                // are adopted rather than rebuilt.
                GridPoint back = route.Forward ? frozen.Floor.Entrance : frozen.Floor.Exit;
                var pose = new ExplorationSnapshot(back, active.Exploration.Facing, frozen.Departure.ElapsedSeconds,
                    null, back, active.Exploration.Facing, 0);
                var candidate = frozen.Join(current with { NextObjectId = next }, pose);
                retained = inactiveFloors.Values.Where(f => f.Floor.IntentFloorId != route.Destination).Append(departed).ToArray();
                var run = new RunSnapshot(candidate, retained, progress);
                destination = ActiveFloor.Restore(candidate, definitions, party, active.Magic.Books,
                    RunCodec.Items(run),
                    engine, dungeonMaterials!, generatedArt!, itemArt!, AllocateLightId, artStyle, roomLights,
                    definitions.Run.Difficulty(progress.Difficulty).IncomingDamageMultiplier, partyId,
                    CombatMessage, (cue, point) => audio!.Play(cue, point), CancelRest,
                    GeneratedUseProblem, GeneratedFeaturePoint, (target, revision) => UseGeneratedFeature(new(target, revision)),
                    AllocateId, (scene, cell) => AimOn(scene, cell, definitions.Combat.AimHeight));
                next = candidate.NextObjectId;
            }
        else
        {
            // Fresh floors mount directly from construction: no snapshot, no
            // temporary scene, no revalidation of just-built state.
            ulong floorId = Allocate();
            if (floorId == partyId) throw new InvalidDataException("Floor and party identities must differ.");
            DungeonFloor fresh = DungeonFloor.Generate(expedition.Floors.Single(f => f.Id == route.Destination),
                FloorDefinitions(progress.Difficulty).Generation.Policy, definitions.Rooms,
                FloorDefinitions(progress.Difficulty).Generation.Elevation).WithArchitecture(definitions.Architecture);
            GridPoint arrival = route.Forward ? fresh.Entrance : fresh.Exit;
            var pose = new ExplorationSnapshot(arrival, active.Exploration.Facing, 0, null,
                arrival, active.Exploration.Facing, 0);
            destination = ActiveFloor.CreateFresh(definitions, expedition, route.Destination, floorId, partyId, party, travelling, pose,
                engine, dungeonMaterials!, generatedArt!, AllocateLightId, Allocate, preset, artStyle,
                definitions.Run.Difficulty(progress.Difficulty).IncomingDamageMultiplier,
                CombatMessage, (cue, point) => audio!.Play(cue, point), CancelRest,
                GeneratedUseProblem, GeneratedFeaturePoint, (target, revision) => UseGeneratedFeature(new(target, revision)),
                (scene, cell) => AimOn(scene, cell, definitions.Combat.AimHeight), fresh);
            retained = inactiveFloors.Values.Append(departed).ToArray();
        }
        }
        catch
        {
            var run = new RunSnapshot(current, inactiveFloors.Values.Append(departed).ToArray(), progress);
            Mount(ActiveFloor.Restore(current, definitions, party, active.Magic.Books,
                RunCodec.Items(run),
                engine, dungeonMaterials!, generatedArt!, itemArt!, AllocateLightId, artStyle, roomLights,
                definitions.Run.Difficulty(progress.Difficulty).IncomingDamageMultiplier, partyId,
                CombatMessage, (cue, point) => audio!.Play(cue, point), CancelRest,
                GeneratedUseProblem, GeneratedFeaturePoint, (target, revision) => UseGeneratedFeature(new(target, revision)),
                AllocateId, (scene, cell) => AimOn(scene, cell, definitions.Combat.AimHeight)));
            throw;
        }
        // The replacement is fully built before the departed floor retires.
        Mount(destination);
        nextObjectId = next;
        inactiveFloors.Clear();
        foreach (var state in retained) inactiveFloors.Add(state.Floor.IntentFloorId, state);
        feedback = "Entered " + expedition.Floors.Single(f => f.Id == route.Destination).Title + ". Inactive floors are frozen.";
    }

    private void StartNewRun(ulong seed, string? difficulty = null)
    {
        difficulty ??= progress.Difficulty;
        var floorDefinitions = FloorDefinitions(difficulty);
        var generated = new ExpeditionGenerator().Generate(definitions.Generation.Expedition, seed);
        if (!generated.Accepted) throw new InvalidDataException("Expedition generation rejected: " + string.Join(", ", generated.Diagnostics.Select(d => d.Detail)));
        ulong next = nextObjectId;
        ulong runPartyId = next++;
        PartyState runParty = new(definitions.Party.Positions, definitions.Party.MaxPartySize, definitions.Characters, preset);
        ulong Allocate()
        {
            if (next == 0 || next > uint.MaxValue) throw new InvalidOperationException("Expedition object identity space exhausted.");
            return checked(next++);
        }
        ulong firstFloorId = Allocate();
        if (firstFloorId == runPartyId) throw new InvalidDataException("Floor and party identities must differ.");
        DungeonFloor firstFloor = DungeonFloor.Generate(generated.Expedition!.Floors.Single(f => f.Id == generated.Expedition!.EntranceFloor),
            floorDefinitions.Generation.Policy, definitions.Rooms, floorDefinitions.Generation.Elevation).WithArchitecture(definitions.Architecture);
        ExplorationSnapshot pose = new(firstFloor.Entrance, definitions.Exploration.InitialFacing, 0, null,
            firstFloor.Entrance, definitions.Exploration.InitialFacing, 0);
        expeditionId = Guid.NewGuid();
        partyId = runPartyId;
        party = runParty;
        selectedMember = party.Members[0].Definition.Id;
        Mount(ActiveFloor.CreateFresh(definitions, generated.Expedition!, generated.Expedition!.EntranceFloor, firstFloorId, partyId,
            party, null, pose, engine, dungeonMaterials!, generatedArt!, AllocateLightId, Allocate, preset, artStyle,
            floorDefinitions.Run.Difficulty(difficulty).IncomingDamageMultiplier,
            CombatMessage, (cue, point) => audio!.Play(cue, point), CancelRest,
            GeneratedUseProblem, GeneratedFeaturePoint, (target, revision) => UseGeneratedFeature(new(target, revision)),
            (scene, cell) => AimOn(scene, cell, definitions.Combat.AimHeight), firstFloor));
        nextObjectId = next;
        expedition = generated.Expedition!;
        inactiveFloors.Clear();
        progress = new(difficulty, false, []);
        mapObservation = null;
        feedback = "New expedition started.";
    }

    private void NewRunCommand(string choice)
    {
        using var input = JsonDocument.Parse(choice);
        if (!ulong.TryParse(input.RootElement.GetProperty("seed").GetString(), out ulong seed))
            throw new InvalidDataException("Seed must be an unsigned whole number.");
        StartNewRun(seed, input.RootElement.GetProperty("difficulty").GetString());
    }

    private string? CompletionProblem()
    {
        if (progress.Completed) return "Expedition already complete.";
        if (active.Floor.IntentFloorId != expedition.ObjectiveFloor) return "Reach " + expedition.Floors.Single(f => f.Id == expedition.ObjectiveFloor).Title + ".";
        if (inactiveFloors.Count + 1 != expedition.Floors.Length) return "Explore every expedition floor.";
        if (active.Combat.Enemies.Any(e => e.Alive)) return "Defeat the remaining garrison: " + active.Combat.Enemies.Count(e => e.Alive) + " foes.";
        return TravelProblem(active.Floor.Exit);
    }

    private void CompleteRun()
    {
        if (CompletionProblem() is { } problem) throw new InvalidDataException(problem);
        active.Magic.AwardExperience(definitions.Run.FinaleExperience);
        active.Features.MarkExitUsed();
        progress = progress with { Completed = true };
        SetPaused(true);
        feedback = definitions.Run.SuccessText;
    }

    private uint RunProjection(SessionValueBuilder value)
    {
        ObserveMap();
        var connections = Connections().Select(c => (c.Link.Id, value.Object(
            ("title", value.String((c.Forward ? "Descend to " : "Return to ") + expedition.Floors.Single(f => f.Id == c.Destination).Title)),
            ("problem", value.String(TravelProblem(c.Departure) ?? "")),
            ("x", value.Number(c.Departure.X)), ("y", value.Number(c.Departure.Y))))).ToArray();
        return value.Object(("id", value.String(expeditionId.ToString())), ("title", value.String(expedition.Title)),
            ("floor", value.String(expedition.Floors.Single(f => f.Id == active.Floor.IntentFloorId).Title)),
            ("floorKey", value.String(active.Floor.IntentFloorId)),
            ("seed", value.String(expedition.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            ("nextSeed", value.String(unchecked(expedition.Seed + definitions.Run.SeedIncrement).ToString(System.Globalization.CultureInfo.InvariantCulture))),
            ("difficulty", value.String(progress.Difficulty)),
            ("status", value.String(progress.Completed ? "Complete" : active.Combat.Defeated ? "Defeated" : "Exploring")),
            ("objective", value.String(definitions.Run.Goal + " Visited " + (inactiveFloors.Count + 1) + "/" + expedition.Floors.Length + " floors.")),
            ("result", value.String(progress.Completed ? definitions.Run.SuccessText : active.Combat.Defeated ? definitions.Run.DefeatText : "")),
            ("completionProblem", value.String(CompletionProblem() ?? "")),
            ("finaleLabel", value.String(definitions.Run.FinaleLabel)),
            ("difficulties", value.Object(definitions.Run.Difficulties.Select(d => (d.Id, value.Object(
                ("name", value.String(d.Name)), ("description", value.String(d.Description))))).ToArray())),
            ("maps", MapProjection(value)), ("x", value.Number(active.Exploration.Position.X)),
            ("y", value.Number(active.Exploration.Position.Y)), ("facing", value.String(active.Exploration.Facing.ToString())),
            ("connections", value.Object(connections)));
    }
}
