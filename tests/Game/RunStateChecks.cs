using System.Buffers;
using Rifles.Game.Combat;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Expedition;
using Rifles.Game.Items;
using Rifles.Game.Magic;
using Rifles.Game.Party;
using Rifles.Procgen.Generation;

internal static class RunStateChecks
{
    internal static void Run(GameDefinitions definitions, ExpeditionSnapshot fixture)
    {
        VerifySingleFloorRoundTrip(definitions, fixture);
        VerifyReloadRoundTrips(definitions, fixture);
        VerifyCastingEnemyAndProjectileRoundTrips(definitions, fixture);
        VerifyOpenContainerState(definitions, fixture);
        VerifyBarrierSafeSaveAdmission(definitions, fixture);
        VerifyRunProgress(definitions, fixture);
        VerifyRetainedFloorKeepsOnlyFloorState(definitions, fixture);
        VerifyInvalidRunsAreRejected(definitions, fixture);
        Console.WriteLine("Run state checks passed: retained floors, travelling party state, and run validation.");
    }

    private static void VerifySingleFloorRoundTrip(GameDefinitions definitions, ExpeditionSnapshot fixture)
    {
        RunSnapshot run = CreateRun(definitions, fixture);
        RunCodec.Validate(run, definitions);

        RunCodec codec = new();
        ArrayBufferWriter<byte> bytes = new();
        codec.Encode(run, bytes);
        RunSnapshot decoded = codec.Decode(bytes.WrittenSpan);
        RunCodec.Validate(decoded, definitions);

        Require(decoded.Inactive.Length == 0 && decoded.Active.Id == fixture.Id
            && decoded.Active.Floor.GenerationIdentity == fixture.Floor.GenerationIdentity,
            "A one-floor run round-trips and validates without changing its active floor.");
    }

    private static void VerifyRetainedFloorKeepsOnlyFloorState(GameDefinitions definitions, ExpeditionSnapshot fixture)
    {
        RetainedFloor retained = RetainedFloor.Capture(fixture);
        Require(retained.Inventory.Packs.All(pack => !ItemInventory.IsMember(pack.Owner.Key) && pack.Owner.Key != "party"),
            "Retained floors exclude travelling member packs and the party inventory.");

        ExpeditionSnapshot current = ChangedPartyState(definitions, fixture);
        ExpeditionSnapshot joined = retained.Join(current, retained.Departure);

        Require(DescribeMemberPacks(joined.Inventory) == DescribeMemberPacks(current.Inventory),
            "Returning to a retained floor uses the current travelling member inventory.");
        Require(joined.Members.SequenceEqual(current.Members),
            "Returning to a retained floor uses current travelling member vitality and formation state.");
        Require(joined.Combat.Magic!.Books.SequenceEqual(current.Combat.Magic!.Books),
            "Returning to a retained floor uses current travelling spellbooks.");
        Require(joined.ItemWorld.Door == retained.ItemWorld.Door && joined.ItemWorld.DoorOpen == retained.ItemWorld.DoorOpen
            && joined.Actor == retained.Actor && joined.Combat.Enemies.SequenceEqual(retained.Enemies),
            "Returning to a retained floor preserves its door, patrol actor, and enemy facts.");

        RunCodec.Validate(CreateRun(definitions, joined), definitions);
    }

    private static void VerifyReloadRoundTrips(GameDefinitions definitions, ExpeditionSnapshot fixture)
    {
        string member = fixture.Roster.Single(member => member.Id == "warden").Id;
        string owner = "member:" + member;
        ulong rifle = fixture.Inventory.Packs.Single(pack => pack.Owner.Key == owner).Items
            .Single(item => item.Definition == "rifle").Id;
        ActionDefinition reload = definitions.Combat.Action(CombatActionKind.Reload);
        ActionSnapshot windup = new(CombatActionKind.Reload, rifle, null, null, 0, null,
            reload.Windup, ActionPhase.Windup, reload.Recovery);
        ExpeditionSnapshot reloading = fixture with
        {
            Combat = fixture.Combat with
            {
                Members = fixture.Combat.Members.Select(saved => saved.Member == member ? saved with { Action = windup } : saved).ToArray(),
                LoadedWeapons = [],
            },
        };
        RunSnapshot decodedWindup = RoundTrip(definitions, CreateRun(definitions, reloading));
        ActionSnapshot restoredWindup = decodedWindup.Active.Combat.Members.Single(saved => saved.Member == member).Action
            ?? throw new InvalidOperationException("Reload windup action was lost from the run save.");
        Require(restoredWindup == windup && decodedWindup.Active.Combat.LoadedWeapons.Length == 0,
            "Run reload preserves a windup action and its unloaded rifle state.");

        ItemInventory inventory = ItemInventory.Restore(definitions.Items, decodedWindup.Active.Inventory);
        const string ammoOwner = "party";
        ulong beforeAmmo = ItemQuantity(inventory, ammoOwner, definitions.Combat.AmmunitionItem);
        ActionState action = ActionState.Restore(restoredWindup);
        int commits = 0;
        action.Advance(reload.Windup, _ =>
        {
            inventory.Consume(ammoOwner, definitions.Combat.AmmunitionItem, 1);
            commits++;
        });
        ActionSnapshot recovery = action.Capture() ?? throw new InvalidOperationException("Reload did not enter recovery.");
        Require(commits == 1 && recovery.Phase == ActionPhase.Recovery
            && ItemQuantity(inventory, ammoOwner, definitions.Combat.AmmunitionItem) == beforeAmmo - 1,
            "Resumed reload consumes exactly one Engine-ledger round and commits once.");

        ExpeditionSnapshot committed = decodedWindup.Active with
        {
            Inventory = inventory.Capture(),
            Combat = decodedWindup.Active.Combat with
            {
                Members = decodedWindup.Active.Combat.Members.Select(saved => saved.Member == member
                    ? saved with { Action = recovery } : saved).ToArray(),
                LoadedWeapons = [rifle],
            },
        };
        RunSnapshot decodedRecovery = RoundTrip(definitions, CreateRun(definitions, committed));
        ActionSnapshot restoredRecovery = decodedRecovery.Active.Combat.Members.Single(saved => saved.Member == member).Action
            ?? throw new InvalidOperationException("Reload recovery action was lost from the run save.");
        Require(restoredRecovery == recovery && decodedRecovery.Active.Combat.LoadedWeapons.SequenceEqual([rifle])
            && ItemQuantity(ItemInventory.Restore(definitions.Items, decodedRecovery.Active.Inventory), ammoOwner, definitions.Combat.AmmunitionItem) == beforeAmmo - 1,
            "Committed reload recovery preserves its action, loaded rifle, and spent ammunition.");

        ActionState resumedRecovery = ActionState.Restore(restoredRecovery);
        resumedRecovery.Advance(reload.Recovery, _ => commits++);
        Require(commits == 1 && !resumedRecovery.Busy,
            "Reload recovery completes after reload without committing a second round.");
    }

    private static void VerifyOpenContainerState(GameDefinitions definitions, ExpeditionSnapshot fixture)
    {
        ExpeditionSnapshot opened = fixture with
        {
            ItemWorld = fixture.ItemWorld with { CrateOpened = true, OpenContainer = "crate" },
        };
        RunSnapshot decoded = RoundTrip(definitions, CreateRun(definitions, opened));
        Require(decoded.Active.ItemWorld.OpenContainer == "crate" && decoded.Active.ItemWorld.CrateOpened,
            "An active run preserves an open crate across save and reload.");
        Require(RetainedFloor.Capture(decoded.Active).ItemWorld.OpenContainer is null,
            "Inactive floor capture closes transient container interaction state.");

        ExpeditionSnapshot invalidOpen = fixture with
        {
            ItemWorld = fixture.ItemWorld with { CrateOpened = false, OpenContainer = "crate" },
        };
        RequireRejected(() => RunCodec.Validate(CreateRun(definitions, invalidOpen), definitions),
            "An active run rejects an open-container state without an opened crate.");
    }

    private static void VerifyBarrierSafeSaveAdmission(GameDefinitions definitions, ExpeditionSnapshot fixture)
    {
        GridPoint exitNeighbor = CardinalDirections.Ordered.Select(direction => fixture.Floor.Exit + direction.Offset())
            .First(fixture.Floor.Cells.Contains);
        ExpeditionSnapshot exitGate = fixture with
        {
            ItemWorld = fixture.ItemWorld with { Door = fixture.Floor.Exit, Lever = exitNeighbor },
        };
        RequireRejected(() => RunCodec.Validate(CreateRun(definitions, exitGate), definitions),
            "A save cannot recreate a closed item gate on the expedition exit.");

        CardinalDirection patrolDirection = CardinalDirections.Ordered.First(direction =>
            fixture.Floor.Cells.Contains(fixture.Floor.Exit + direction.Offset()));
        PatrolSnapshot exitPatrol = fixture.Actor with
        {
            Start = fixture.Floor.Exit,
            End = fixture.Floor.Exit + patrolDirection.Offset(),
            Motion = new ExplorationState(fixture.Floor.Exit, definitions.Exploration with
            {
                StepSeconds = definitions.Features.ActorStepSeconds,
                InitialFacing = patrolDirection,
            }).Capture(),
        };
        ExpeditionSnapshot blockedExit = fixture with { Actor = exitPatrol };
        RequireRejected(() => RunCodec.Validate(CreateRun(definitions, blockedExit), definitions),
            "A save cannot move a retained patrol onto the expedition exit.");
    }

    private static void VerifyCastingEnemyAndProjectileRoundTrips(GameDefinitions definitions, ExpeditionSnapshot fixture)
    {
        string member = fixture.Roster.Single(member => member.Id == "warden").Id;
        SpellDefinition spell = definitions.Magic.Spell("spark");
        EnemySnapshot moving = MovingEnemy(definitions, fixture, fixture.Combat.Enemies.Length - 1);
        EnemySnapshot target = fixture.Combat.Enemies[0];
        ActionSnapshot cast = new(CombatActionKind.Cast, 0, null, null, target.Id, member, spell.Windup,
            ActionPhase.Windup, spell.Recovery, target.Motion.Position, Spell: spell.Id, Cost: spell.Cost);
        ExpeditionSnapshot casting = fixture with
        {
            Combat = fixture.Combat with
            {
                Enemies = fixture.Combat.Enemies.Select(enemy => enemy.Id == moving.Id ? moving : enemy).ToArray(),
                Members = fixture.Combat.Members.Select(saved => saved.Member == member ? saved with { Action = cast } : saved).ToArray(),
            },
        };
        RunSnapshot decodedWindup = RoundTrip(definitions, CreateRun(definitions, casting));
        EnemySnapshot decodedEnemy = decodedWindup.Active.Combat.Enemies.Single(enemy => enemy.Id == moving.Id);
        ActionSnapshot decodedCast = decodedWindup.Active.Combat.Members.Single(saved => saved.Member == member).Action
            ?? throw new InvalidOperationException("Casting action was lost from the run save.");
        Require(decodedEnemy.Motion.Action is ExplorationAction.Forward && decodedEnemy.Motion.Destination != decodedEnemy.Motion.Position
            && decodedCast == cast, "Run save preserves an in-transit enemy and an uncommitted cast together.");

        PartyState party = new(definitions.Party.Positions, definitions.Party.MaxPartySize, decodedWindup.Active.Roster);
        party.Restore(decodedWindup.Active.Members);
        PartyMemberState caster = party.Members.Single(saved => saved.Definition.Id == member);
        long resourceBefore = caster.Resource;
        ActionState action = ActionState.Restore(decodedCast);
        int commits = 0;
        action.Advance(spell.Windup, _ =>
        {
            caster.SpendResource(spell.Cost);
            commits++;
        });
        ActionSnapshot recovery = action.Capture() ?? throw new InvalidOperationException("Cast did not enter recovery.");
        Require(commits == 1 && recovery.Phase == ActionPhase.Recovery && caster.Resource == resourceBefore - spell.Cost,
            "A cast resumed from windup spends its resource exactly once before recovery.");

        FlightSnapshot flight = SpellFlight(definitions, decodedWindup.Active, spell, member);
        ExpeditionSnapshot committed = decodedWindup.Active with
        {
            NextObjectId = checked(flight.Id + 1),
            Members = party.Capture().ToArray(),
            Combat = decodedWindup.Active.Combat with
            {
                Members = decodedWindup.Active.Combat.Members.Select(saved => saved.Member == member
                    ? saved with { Action = recovery } : saved).ToArray(),
                Flights = [flight],
            },
        };
        RunSnapshot decodedRecovery = RoundTrip(definitions, CreateRun(definitions, committed));
        FlightSnapshot restoredFlight = decodedRecovery.Active.Combat.Flights.Single();
        ActionSnapshot restoredRecovery = decodedRecovery.Active.Combat.Members.Single(saved => saved.Member == member).Action
            ?? throw new InvalidOperationException("Cast recovery was lost from the run save.");
        Require(restoredFlight == flight && restoredRecovery == recovery
            && decodedRecovery.Active.Members.Single(saved => saved.Id == member).Resource == resourceBefore - spell.Cost,
            "Run save retains the committed spell flight, recovery action, and already-spent cast resource.");

        ActionState resumedRecovery = ActionState.Restore(restoredRecovery);
        resumedRecovery.Advance(spell.Recovery, _ => commits++);
        Require(commits == 1 && !resumedRecovery.Busy,
            "Cast recovery completes without a second cast commit while its projectile remains owned by flight state.");
    }

    private static void VerifyRunProgress(GameDefinitions definitions, ExpeditionSnapshot fixture)
    {
        RunProgress remembered = new(definitions.Run.DefaultDifficulty, false,
            [new MapMemory(fixture.Floor.IntentFloorId, [fixture.Floor.Entrance, fixture.Floor.Exit])]);
        RunSnapshot decoded = RoundTrip(definitions, CreateRun(definitions, fixture, progress: remembered));
        Require(decoded.Progress.Difficulty == remembered.Difficulty && !decoded.Progress.Completed
            && decoded.Progress.Maps.Single().FloorKey == remembered.Maps[0].FloorKey
            && decoded.Progress.Maps.Single().Cells.SequenceEqual(remembered.Maps[0].Cells),
            "Discovered cells and selected difficulty round-trip with the run.");

        RequireRejected(() => RunCodec.Validate(CreateRun(definitions, fixture,
            progress: remembered with { Difficulty = "forged" }), definitions),
            "A run rejects an unknown difficulty profile.");
        RequireRejected(() => RunCodec.Validate(CreateRun(definitions, fixture,
            progress: remembered with { Maps = [new MapMemory("missing-floor", [])] }), definitions),
            "A run rejects discovered maps for floors outside the visited expedition.");
        RequireRejected(() => RunCodec.Validate(CreateRun(definitions, fixture,
            progress: remembered with { Maps = [new MapMemory(fixture.Floor.IntentFloorId, [new(9999, 9999)])] }), definitions),
            "A run rejects discovered cells outside the retained floor geometry.");
        RequireRejected(() => RunCodec.Validate(CreateRun(definitions, fixture,
            progress: remembered with { Completed = true }), definitions),
            "A run cannot forge expedition completion before the objective floor and finale exit are secured.");

        MagicSnapshot forgedMagic = fixture.Combat.Magic! with
        {
            Books = fixture.Combat.Magic!.Books.Select(book => book with { Experience = definitions.Run.FinaleExperience }).ToArray(),
        };
        ExpeditionSnapshot forgedExperience = fixture with { Combat = fixture.Combat with { Magic = forgedMagic } };
        RequireRejected(() => RunCodec.Validate(CreateRun(definitions, forgedExperience), definitions),
            "A non-completed run cannot forge finale experience into its spellbooks.");
    }

    private static EnemySnapshot MovingEnemy(GameDefinitions definitions, ExpeditionSnapshot fixture, int index)
    {
        EnemySnapshot selected = fixture.Combat.Enemies[index];
        EnemyDefinition definition = definitions.Combat.Enemy(selected.Definition);
        MovementGrid movement = new(fixture.Floor.Cells.ToHashSet(), (_, _) => true, definitions.Crowd);
        ExplorationState party = ExplorationState.Restore(fixture.Exploration, fixture.Floor, definitions.Exploration);
        party.Bind(movement, fixture.PartyId);
        PatrolActor actor = PatrolActor.Restore(fixture.Actor, fixture.Floor,
            definitions.Exploration with { StepSeconds = definitions.Features.ActorStepSeconds });
        actor.Bind(movement);
        fixture.Features.Dressing.Bind(movement);

        HashSet<GridPoint> unavailable = fixture.Combat.Enemies.Where(enemy => enemy.Id != selected.Id)
            .Select(enemy => enemy.Motion.Position).Append(fixture.ItemWorld.Door)
            .Concat(fixture.GeneratedFeatures.Gates.Select(gate => gate.Cell)).ToHashSet();
        ExplorationState? moving = null;
        for (int current = 0; current < fixture.Combat.Enemies.Length; current++)
        {
            EnemySnapshot enemy = fixture.Combat.Enemies[current];
            EnemyDefinition enemyDefinition = definitions.Combat.Enemy(enemy.Definition);
            if (enemy.Id != selected.Id)
            {
                ExplorationState stationary = ExplorationState.Restore(enemy.Motion, fixture.Floor,
                    definitions.Exploration with { StepSeconds = enemyDefinition.StepSeconds });
                stationary.Bind(movement, enemy.Id, enemyDefinition.Footprint, enemyDefinition.Faction, enemyDefinition.Share);
                continue;
            }

            foreach (CardinalDirection direction in CardinalDirections.Ordered)
            {
                GridPoint destination = enemy.Motion.Position + direction.Offset();
                if (!fixture.Floor.Cells.Contains(destination) || unavailable.Contains(destination)) continue;
                ExplorationState candidate = new(enemy.Motion.Position,
                    definitions.Exploration with { StepSeconds = definition.StepSeconds, InitialFacing = direction });
                candidate.Bind(movement, enemy.Id, definition.Footprint, definition.Faction, definition.Share);
                if (candidate.Act(ExplorationAction.Forward)) { moving = candidate; break; }
                movement.Remove(enemy.Id);
            }
            if (moving is null) throw new InvalidOperationException("Fixture has no valid in-transit enemy step.");
        }
        return selected with { Motion = moving!.Capture() };
    }

    private static FlightSnapshot SpellFlight(GameDefinitions definitions, ExpeditionSnapshot state, SpellDefinition spell, string member)
    {
        GridPoint source = state.Exploration.Position;
        GridPoint target = state.Combat.Enemies[0].Motion.Position;
        float dx = target.X - source.X, dz = target.Y - source.Y;
        float length = MathF.Sqrt(dx * dx + dz * dz);
        if (length == 0) { dx = 1; length = 1; }
        float cell = definitions.Exploration.CellSize;
        return new(state.NextObjectId, state.PartyId, member, CombatActionKind.Cast,
            (source.X + .5f) * cell, 1, (source.Y + .5f) * cell, dx / length, 0, dz / length,
            spell.Range / 2, source, null, null, spell.Id);
    }

    private static ExpeditionSnapshot ChangedPartyState(GameDefinitions definitions, ExpeditionSnapshot fixture)
    {
        ItemInventory inventory = ItemInventory.Restore(definitions.Items, fixture.Inventory);
        string token = inventory.Items("party").First().Token;
        int moved = (inventory.SlotOf(token) + 1) % definitions.Items.PartySlots;
        inventory.Arrange(token, moved, inventory.Revision);

        MemberSnapshot[] members = fixture.Members.Select((member, index) => index == 0
            ? member with { Vitality = Math.Max(1, member.Vitality - 1) }
            : member).ToArray();
        MagicState magic = new(definitions.Magic, fixture.Roster.Select(member => (member.Id, member.Archetype)));
        MagicBookSnapshot firstBook = magic.Capture().Books[0];
        magic.Select(firstBook.Member, firstBook.Known[0]);

        return fixture with
        {
            Members = members,
            Inventory = inventory.Capture(),
            Combat = fixture.Combat with { Magic = magic.Capture() },
        };
    }

    private static void VerifyInvalidRunsAreRejected(GameDefinitions definitions, ExpeditionSnapshot fixture)
    {
        RetainedFloor sameFloor = RetainedFloor.Capture(fixture);
        RequireRejected(() => RunCodec.Validate(CreateRun(definitions, fixture, [sameFloor]), definitions),
            "A run cannot retain a second copy of the active floor identity.");

        string otherFloor = fixture.Intent.Floors.First(floor => floor.Id != fixture.Floor.IntentFloorId).Id;
        RetainedFloor withMemberPack = sameFloor with
        {
            Floor = sameFloor.Floor with { IntentFloorId = otherFloor },
            Inventory = fixture.Inventory,
        };
        RequireRejected(() => RunCodec.Validate(CreateRun(definitions, fixture, [withMemberPack]), definitions),
            "A retained floor cannot carry travelling member packs.");
    }

    private static string DescribeMemberPacks(InventorySnapshot inventory) => string.Join("|", inventory.Packs
        .Where(pack => ItemInventory.IsMember(pack.Owner.Key) || pack.Owner.Key == "party").OrderBy(pack => pack.Owner.Key, StringComparer.Ordinal)
        .Select(pack => pack.Owner.Key + ":" + string.Join(",", pack.Stacks.OrderBy(stack => stack.Definition, StringComparer.Ordinal)
            .Select(stack => stack.Definition + ":" + stack.Quantity)) + ";" + string.Join(",", pack.Items
            .OrderBy(item => item.Id).Select(item => item.Id + ":" + item.Definition)) + ";" + string.Join(",", (pack.Slots ?? [])
            .OrderBy(slot => slot.Slot).Select(slot => slot.Token + "@" + slot.Slot))));

    private static RunSnapshot CreateRun(GameDefinitions definitions, ExpeditionSnapshot active, RetainedFloor[]? inactive = null,
        RunProgress? progress = null) => new(active, inactive ?? [], progress ?? new RunProgress(definitions.Run.DefaultDifficulty, false, []));

    private static RunSnapshot RoundTrip(GameDefinitions definitions, RunSnapshot run)
    {
        RunCodec.Validate(run, definitions);
        RunCodec codec = new();
        ArrayBufferWriter<byte> bytes = new();
        codec.Encode(run, bytes);
        RunSnapshot decoded = codec.Decode(bytes.WrittenSpan);
        RunCodec.Validate(decoded, definitions);
        return decoded;
    }

    private static ulong ItemQuantity(ItemInventory inventory, string owner, string definition) => inventory.Items(owner)
        .Where(item => item.Definition == definition).Aggregate(0UL, (total, item) => checked(total + item.Quantity));

    private static void RequireRejected(Action action, string message)
    {
        try { action(); }
        catch (Exception) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
