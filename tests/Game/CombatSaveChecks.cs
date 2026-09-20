using Rifles.Game.Magic;
using Rifles.Game.Characters;
using Rifles.Game.Tests;
using Rifles.Game.Combat;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Items;
using Rifles.Game.Party;
using Rifles.Procgen.Generation;

internal static class CombatSaveChecks
{
    internal static void Run(GameDefinitions definitions)
    {
        CombatFixture fixture = CombatFixture.Create(definitions);
        RestoredCombat restored = CombatRestore.Validate(fixture.Saved, definitions, fixture.Floor, fixture.Inventory, fixture.Party,
            fixture.PartyId, fixture.AllyIds);
        Require(restored.Enemies.Length == definitions.Combat.Encounter.Length && restored.Actions.Count == fixture.Party.Members.Count,
            "Combat restore rebuilds exact enemy and party action owners.");

        VerifySpellSettlementAndTargets(definitions, fixture);
        VerifyCorruptPositionsAreRejected(definitions, fixture);
        VerifyNonFiniteFlightIsRejected(definitions, fixture);
        VerifyNonWeaponLoadIsRejected(definitions, fixture);
        VerifyInvalidAimAndDeadActivityAreRejected(definitions, fixture);

        EnemySpawnDefinition first = definitions.Combat.Encounter[0];
        GameDefinitions repeated = definitions with { Combat = definitions.Combat with
            { Encounter = [first with { Id = "first" }, first with { Id = "second" }] } };
        CombatFixture copies = CombatFixture.Create(repeated);
        RestoredCombat repeatedResult = CombatRestore.Validate(copies.Saved, repeated, copies.Floor, copies.Inventory, copies.Party, copies.PartyId, copies.AllyIds);
        Require(repeatedResult.Enemies.Length == 2 && repeatedResult.Enemies[0].Id != repeatedResult.Enemies[1].Id,
            "Multiple instances of one archetype keep separate identities and inventories across restore.");

        Console.WriteLine("Combat save checks passed: combat owners, action phases, flights, drops, and allies.");
    }

    private static void VerifySpellSettlementAndTargets(GameDefinitions definitions, CombatFixture fixture)
    {
        SpellDefinition ward = definitions.Magic.Spell("ward");
        ActionSnapshot cast = new(CombatActionKind.Cast, 0, null, null, 0, "warden", ward.Recovery,
            ActionPhase.Recovery, ward.Recovery, Spell: ward.Id, Cost: ward.Cost);
        CombatSnapshot WithCast(ActionSnapshot action) => fixture.Saved with
        {
            Members = fixture.Saved.Members.Select(m => m.Member == "warden" ? m with { Action = action } : m).ToArray(),
        };
        Validate(WithCast(cast), definitions, fixture);
        RequireRejected(() => Validate(WithCast(cast with { Cost = 0 }), definitions, fixture),
            "Restored spells cannot forge a free cast.");
        MagicSnapshot magic = fixture.Saved.Magic!;
        Validate(fixture.Saved with { Magic = magic with
            { Conditions = [new("member:warden", "blight", 1, 1)] } }, definitions, fixture);
        RequireRejected(() => Validate(fixture.Saved with { Magic = magic with
            { Conditions = [new("enemy:" + fixture.Saved.Enemies[0].Id, "ward", 1, 0)] } }, definitions, fixture),
            "Ally-only wards cannot be forged on enemy targets; hostile enemy spells on party members remain valid.");
    }

    private static void VerifyCorruptPositionsAreRejected(GameDefinitions definitions, CombatFixture fixture)
    {
        EnemySnapshot enemy = fixture.Saved.Enemies[0];
        CombatSnapshot corruptMotion = fixture.Saved with
        {
            Enemies = [enemy with { Motion = enemy.Motion with { Position = new GridPoint(9000, 9000) } }, .. fixture.Saved.Enemies.Skip(1)],
        };
        RequireRejected(() => Validate(corruptMotion, definitions, fixture), "Enemies outside the restored floor are rejected.");

        CombatSnapshot corruptDrop = fixture.Saved with { Drops = [new DropSnapshot(fixture.Saved.Enemies[0].Owner, new(9000, 9000))] };
        RequireRejected(() => Validate(corruptDrop, definitions, fixture), "Drops outside the restored floor are rejected.");
    }

    private static void VerifyNonFiniteFlightIsRejected(GameDefinitions definitions, CombatFixture fixture)
    {
        FlightSnapshot flight = new(600, fixture.PartyId, fixture.Party.Members[0].Definition.Id, CombatActionKind.Cast,
            1, 1, 1, 1, 0, 0, 1, fixture.Floor.Entrance, null, null, Spell: "spark");
        RequireRejected(() => Validate(fixture.Saved with { Flights = [flight with { X = float.NaN }] }, definitions, fixture),
            "Non-finite projectile coordinates are rejected before a live trace.");
        RequireRejected(() => Validate(fixture.Saved with { Flights = [flight with { Shooter = fixture.PartyId + 1 }] }, definitions, fixture),
            "Saved projectiles cannot claim an arbitrary shooter.");
        RequireRejected(() => Validate(fixture.Saved with { Flights = [flight with { Member = "missing" }] }, definitions, fixture),
            "Saved projectiles retain a real party member even after that member dies.");
        RequireRejected(() => Validate(fixture.Saved with { Flights = [flight with { Kind = CombatActionKind.Throw, Spell = null, Owner = fixture.Saved.Enemies[0].Owner }] }, definitions, fixture),
            "A thrown item cannot claim a living enemy inventory as its flight owner.");

        RestoredCombat normalized = CombatRestore.Validate(fixture.Saved with { Flights = [flight with { DirectionX = 3 }] }, definitions,
            fixture.Floor, fixture.Inventory, fixture.Party, fixture.PartyId, fixture.AllyIds);
        Require(Math.Abs(normalized.Flights[0].DirectionX - 1) < .0001f, "Saved flight direction is normalized before resuming.");
    }

    private static void VerifyNonWeaponLoadIsRejected(GameDefinitions definitions, CombatFixture fixture)
    {
        ulong knife = fixture.Inventory.Owners.SelectMany(owner => fixture.Inventory.Items(owner.Key))
            .First(item => item.Definition == "knife" && item.Entity != 0).Entity;
        RequireRejected(() => Validate(fixture.Saved with { LoadedWeapons = [knife] }, definitions, fixture),
            "A loaded item must be an actual rifle identity.");
    }

    private static void VerifyInvalidAimAndDeadActivityAreRejected(GameDefinitions definitions, CombatFixture fixture)
    {
        string member = fixture.Party.Members[0].Definition.Id;
        ActionSnapshot action = new(CombatActionKind.Fire, 0, null, null, fixture.Saved.Enemies[0].Id, null,
            definitions.Combat.Action(CombatActionKind.Fire).Windup, ActionPhase.Windup, definitions.Combat.Action(CombatActionKind.Fire).Recovery, new(9000, 9000));
        MemberActionSnapshot[] members = fixture.Saved.Members.Select(saved => saved.Member == member ? saved with { Action = action } : saved).ToArray();
        RequireRejected(() => Validate(fixture.Saved with { Members = members }, definitions, fixture), "Attack aims must remain on the restored floor.");

        EnemySnapshot first = fixture.Saved.Enemies[0];
        EnemySnapshot dead = first with
        {
            Stats = StatSnapshotHelpers.WithTrack(first.Stats, RiflesStatIds.Vitality, 0),
            Action = action with { AimCell = fixture.Floor.Entrance },
        };
        CombatSnapshot activeDead = fixture.Saved with
        {
            Enemies = [dead, .. fixture.Saved.Enemies.Skip(1)],
            Drops = [new DropSnapshot(dead.Owner, fixture.Floor.Entrance)],
        };
        RequireRejected(() => Validate(activeDead, definitions, fixture), "Dead enemies cannot retain an action while restored.");
    }

    private static void Validate(CombatSnapshot saved, GameDefinitions definitions, CombatFixture fixture) => CombatRestore.Validate(saved,
        definitions, fixture.Floor, fixture.Inventory, fixture.Party, fixture.PartyId, fixture.AllyIds);

    private static void RequireRejected(Action action, string message)
    {
        try
        {
            action();
        }
        catch (Exception) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record CombatFixture(DungeonFloor Floor, ItemInventory Inventory, PartyState Party, CombatSnapshot Saved,
        ulong PartyId, ulong[] AllyIds)
    {
        internal static CombatFixture Create(GameDefinitions definitions)
        {
            DungeonFloor floor = DungeonFloor.Generate(definitions.Generation.Seed, definitions.Generation, definitions.Rooms);
            PartyState party = new(definitions.Party.Positions, definitions.Party.MaxPartySize, definitions.Characters, definitions.Characters.DefaultPresetId);
            ulong partyId = 1;
            ulong[] allyIds = [2, 3];
            ulong nextId = 100;
            ItemInventory inventory = new(definitions.Items, party.Members.Select(member => new PackOwner(nextId++, "member:" + member.Definition.Id,
                definitions.Items.Backpack.Mass, definitions.Items.Backpack.Space)));

            EnemySnapshot[] enemies = definitions.Combat.Encounter.Select((spawn, index) =>
            {
                EnemyDefinition definition = definitions.Combat.Enemy(spawn.Enemy);
                ulong id = (ulong)(10 + index);
                string owner = "combat:enemy:" + id;
                inventory.RegisterOwner(new PackOwner(nextId++, owner, definitions.Combat.DropCapacity.Mass, definitions.Combat.DropCapacity.Space));
                foreach (StartingItem loot in definition.Loot) inventory.Grant(InventoryOwner.Parse(owner), loot.Definition, loot.Quantity, () => nextId++);
                ExplorationState motion = new(floor.Cells.First(), definitions.Exploration with { StepSeconds = definition.StepSeconds });
                return new EnemySnapshot(id, definition.Id, motion.Capture(), StatSnapshotHelpers.FullEnemy(definition, definitions.Magic.EnemyResource), null, 0, false, false, owner, new EnemyBrain(definition.Brain, motion.Position, [motion.Position]).Capture(), spawn.Id);
            }).ToArray();
            MemberActionSnapshot[] members = party.Members.Select(member => new MemberActionSnapshot(member.Definition.Id, null)).ToArray();
            AllySnapshot[] allies = allyIds.Select(id => RiflesCombat.FreshAlly(id, definitions)).ToArray();
            CombatSnapshot saved = new(enemies, members, [], [], [], allies, enemies[0].Id, new MagicState(definitions.Magic, party.Members.Select(m => (m.Definition.Id, m.Definition.Archetype)), party.Entities).Capture());
            return new CombatFixture(floor, inventory, party, saved, partyId, allyIds);
        }
    }
}
