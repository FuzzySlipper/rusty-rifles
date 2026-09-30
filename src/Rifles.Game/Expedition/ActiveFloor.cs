using Rifles.Game.Characters;
using Rifles.Game.Combat;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Generation;
using Rifles.Game.Items;
using Rifles.Game.Magic;
using Rifles.Game.Party;
using Rifles.Procgen;
using Rifles.Procgen.Expeditions;
using Rifles.Procgen.Generation;
using Rusty.Engine;
using System.Numerics;

namespace Rifles.Game.Expedition;

/// <summary>
/// What travels between floors when the party moves: spellbooks ride their
/// characters, conditions ride their entities' timing, packs and loaded
/// rounds ride item identity. Floor-local facts stay frozen behind.
/// </summary>
internal sealed record TravellingState(
    IReadOnlyDictionary<string, MagicState.MagicBook> Books,
    MagicConditionSnapshot[] Conditions,
    SavedPack[] Packs,
    WeaponStateSnapshot Weapons);

/// <summary>
/// The live floor aggregate: floor data plus every built owner whose lifetime
/// matches the active floor. Fresh floors mount directly from construction —
/// no snapshot, no temporary scene, no revalidation. Snapshot restores
/// (loads and retained returns) validate before building. Disposal retires
/// appearance references before Engine resources, mirroring activation order.
/// </summary>
internal sealed class ActiveFloor : IDisposable
{
    internal ActiveFloor(DungeonFloor floor, DungeonScene scene, MovementGrid grid, WorldFeatures features,
        PatrolActor actor, ExplorationState exploration, ExplorationItems itemWorld, ItemInventory inventory,
        MagicState magic, RiflesCombat combat, GeneratedFeatureState generatedFeatures,
        EncounterPlacementResult encounterPlacement, ulong floorId, PartyState party)
    {
        Floor = floor;
        Scene = scene;
        Grid = grid;
        Features = features;
        Actor = actor;
        Exploration = exploration;
        ItemWorld = itemWorld;
        Inventory = inventory;
        Magic = magic;
        Combat = combat;
        Generated = generatedFeatures;
        Generated.BindCombat(combat);
        EncounterPlacement = encounterPlacement;
        FloorId = floorId;
        Party = party;
    }

    internal DungeonFloor Floor { get; }
    internal DungeonScene Scene { get; }
    internal MovementGrid Grid { get; }
    internal WorldFeatures Features { get; }
    internal PatrolActor Actor { get; }
    internal ExplorationState Exploration { get; }
    internal ExplorationItems ItemWorld { get; }
    internal ItemInventory Inventory { get; }
    internal MagicState Magic { get; }
    internal RiflesCombat Combat { get; }
    internal GeneratedFeatureState Generated { get; }
    internal GeneratedFeatureSnapshot GeneratedFeatures => Generated.Snapshot;
    internal EncounterPlacementResult EncounterPlacement { get; }
    internal ulong FloorId { get; }
    /// <summary>Reference to the travelling party, not floor ownership.</summary>
    internal PartyState Party { get; }

    public void Dispose()
    {
        Features.Dispose();
        Scene.Dispose();
    }

    /// <summary>
    /// Validates an admitted snapshot (loads and retained returns) and builds
    /// the live floor. A travelling party moves untouched with its entities,
    /// books, and markers; fresh boots rebuild from the snapshot. Retained
    /// validation still checks the thawed merge; only fresh factory mounts
    /// skip it, trusting construction.
    /// </summary>
    internal static ActiveFloor Restore(ExpeditionSnapshot saved, FloorServices services,
        PartyState? travellingParty, IReadOnlyDictionary<string, MagicState.MagicBook>? travellingBooks,
        IReadOnlyDictionary<ulong, string>? allItems, string? style, bool roomLights,
        double incomingDamageMultiplier, ulong partyId, Func<ulong> allocateId, AdmittedFloor? admitted = null)
    {
        var restored = admitted ?? ExpeditionCodec.Validate(saved, services.Definitions,
            allItems ?? saved.Inventory.Packs.SelectMany(p => p.Items).ToDictionary(i => i.Id, i => i.Definition),
            travellingParty, travellingBooks);
        ItemInventory restoredInventory = restored.Inventory;
        RestoredCombat restoredCombat = restored.Combat;
        ExplorationItems restoredItems = restored.ItemWorld;
        DungeonScene replacement = new(services.Engine, services.Materials, saved.Floor, services.Definitions.Exploration, services.Definitions.Appearance,
            services.AllocateLightId, services.Definitions.ItemExploration);
        MovementGrid replacementGrid = new(saved.Floor.Cells.ToHashSet(), replacement.AdmitStep, services.Definitions.Crowd);
        try
        {
            replacement.SetDoor(saved.ItemWorld.Door, saved.ItemWorld.DoorOpen);
            foreach (var gate in saved.GeneratedFeatures.Gates) replacement.SetDoor(gate.Cell, gate.Open);
            FloorFactory.ConfigureDoorClearance(replacementGrid, saved.Floor, saved.ItemWorld.Door, services.Definitions.Combat.DoorClearance);
            if (!restored.Party.Defeated) restored.Exploration.Bind(replacementGrid, saved.PartyId);
            if (restoredCombat.Allies.Single(a => a.Id == saved.Actor.Id).Vitality > 0) restored.Actor.Bind(replacementGrid);
        }
        catch { replacement.Dispose(); throw; }
        WorldFeatures replacementFeatures;
        try
        {
            replacementFeatures = new WorldFeatures(services.Engine, services.Art, replacement, saved.Floor, services.Definitions.Features,
                services.Definitions.Art, saved.Features, services.AllocateLightId(), style);
        }
        catch { replacement.Dispose(); throw; }
        try
        {
            if (style is not null && replacement.Style != style) replacement.SetStyle(style);
            replacement.SetRoomLights(roomLights);
            replacementFeatures.Bind(replacementGrid, restoredCombat.Allies.Single(a => a.Id == saved.Features.Dressing.ObserverId).Vitality > 0);
            foreach (var ally in restoredCombat.Allies.Where(a => a.Vitality == 0)) replacementGrid.Remove(ally.Id);
            foreach (EnemyState enemy in restoredCombat.Enemies.Where(e => e.Alive)) enemy.Motion.Bind(replacementGrid, enemy.Id, enemy.Definition.Footprint, enemy.Definition.Faction, enemy.Definition.Share);
            replacementFeatures.Present(restored.Actor, restored.Exploration, services.ItemArt.Facts(restoredInventory, restoredItems, replacement));
        }
        catch
        {
            replacementFeatures.Dispose(); replacement.Dispose(); throw;
        }
        GeneratedFeatureState generated = new(saved.GeneratedFeatures, services.Definitions, replacement,
            restored.Exploration, restoredItems, restored.Actor, restoredInventory, restored.Party,
            restoredCombat.Magic, services.ItemArt, services.Message, cell => services.Aim(replacement, cell));
        CombatScope scope = new(replacement, replacementGrid, restored.Exploration, restoredItems, restored.Actor,
            () => replacementFeatures, generated, saved.Floor, partyId, incomingDamageMultiplier, allocateId,
            cell => services.Aim(replacement, cell), services.Message, services.Sound, services.CancelRest);
        RiflesCombat combat = RiflesCombat.Restore(restoredCombat, services.Definitions, restored.Party.Entities, restored.Party,
            restoredCombat.Magic, restoredInventory, scope);
        return new ActiveFloor(saved.Floor, replacement, replacementGrid, replacementFeatures, restored.Actor,
            restored.Exploration, restoredItems, restoredInventory, restoredCombat.Magic, combat,
            generated, saved.EncounterPlacement, saved.FloorId, restored.Party);
    }

    /// <summary>
    /// Builds and mounts a fresh floor directly. The travelling party object
    /// moves untouched — no duplicate party, no snapshot roundtrip. Fresh
    /// member packs receive starter grants only on a fresh run; travelling
    /// packs restore into them without duplicating the kit.
    /// </summary>
    internal static ActiveFloor CreateFresh(FloorServices services, ResolvedExpedition intent, string floorKey,
        ulong floorId, ulong partyId, PartyState party, TravellingState? travelling, ExplorationSnapshot pose,
        Func<ulong> allocate, string? preset, string? style, double incomingDamageMultiplier, DungeonFloor? resolvedFloor = null)
    {
        DungeonFloor floor = resolvedFloor ?? DungeonFloor.Generate(intent.Floors.Single(f => f.Id == floorKey),
            services.Definitions.Generation.Policy, services.Definitions.Rooms, services.Definitions.Generation.Elevation)
            .WithArchitecture(services.Definitions.Architecture);
        ResolvedFloorIntent resolved = intent.Floors.Single(f => f.Id == floorKey);
        // Caller-mismatch guard only: the intent graph re-hash that the old
        // snapshot factory ran here re-proved generation output against its
        // own input. Floor identity plus seed equality suffices.
        GameDefinitions.Require(floor.IntentFloorId == resolved.Id && floor.Seed == resolved.Candidate.Seed,
            "Resolved floor does not belong to the requested expedition floor.");

        DungeonScene scene = new(services.Engine, services.Materials, floor, services.Definitions.Exploration, services.Definitions.Appearance,
            services.AllocateLightId, services.Definitions.ItemExploration);
        try
        {
            return CreateFreshInner(services, intent, floorId, partyId, party, travelling, pose,
                allocate, preset, style, incomingDamageMultiplier, floor, scene);

        }
        catch
        {
            scene.Dispose();
            throw;
        }
    }

    private static ActiveFloor CreateFreshInner(FloorServices services, ResolvedExpedition intent,
        ulong floorId, ulong partyId, PartyState party, TravellingState? travelling, ExplorationSnapshot pose,
        Func<ulong> allocate, string? preset, string? style, double incomingDamageMultiplier,
        DungeonFloor floor, DungeonScene scene)
    {
        ExplorationState exploration = ExplorationState.Restore(pose, floor, services.Definitions.Exploration);
        PatrolActor actor = PatrolActor.Create(allocate(), floor,
            services.Definitions.Exploration with { StepSeconds = services.Definitions.Features.ActorStepSeconds }, services.Definitions.Features);
        FeatureSnapshot features = new(allocate(), allocate(), 1, true, false,
            RoomDressing.Create(floor, actor, services.Definitions.Art, allocate));

        ExplorationItems itemWorld = ExplorationItems.Create(services.Definitions.ItemExploration, floor, features.Dressing, actor, allocate);
        // Travelling packs keep their ledger identity: the floor registers
        // the travelled owners instead of allocating fresh ones, so restored
        // contents land on the registered record. Fresh runs allocate.
        PackOwner TravellingOrFresh(string key, Func<PackOwner> fresh) =>
            travelling?.Packs.SingleOrDefault(p => p.Owner.Key == key)?.Owner ?? fresh();
        IEnumerable<PackOwner> memberPacks = party.Members.Select(member => TravellingOrFresh("member:" + member.Definition.Id,
            () => new PackOwner(allocate(), "member:" + member.Definition.Id, services.Definitions.Items.Backpack.Mass, services.Definitions.Items.Backpack.Space)));
        PackOwner partyPack = TravellingOrFresh(ItemInventory.PartyKey,
            () => new(allocate(), ItemInventory.PartyKey, services.Definitions.Items.Party.Mass, services.Definitions.Items.Party.Space));
        IEnumerable<PackOwner> anchorPacks = itemWorld.Anchors.Select(anchor =>
        {
            PackDefinition capacity = anchor.Key == "crate" ? services.Definitions.Items.Container : services.Definitions.Items.Anchor;
            return new PackOwner(anchor.Id, anchor.Key, capacity.Mass, capacity.Space);
        });
        ItemInventory inventory = new(services.Definitions.Items, memberPacks.Append(partyPack).Concat(anchorPacks));
        inventory.BindMembers(party.Entities, party.Members);
        // Starter grants land in floor-local owners on every fresh floor;
        // member and party kits travel instead of granting again.
        inventory.GrantStarting(allocate, preset, owner => travelling is null
            || InventoryOwner.Parse(owner) is not (MemberOwner or PartyOwner));
        if (travelling is not null)
            foreach (SavedPack pack in travelling.Packs) inventory.RestorePack(pack);
        scene.SetDoor(itemWorld.Door, false);

        MovementGrid movement = new(floor.Cells.ToHashSet(), scene.AdmitStep, services.Definitions.Crowd);
        foreach (FloorConnector connector in floor.Connectors)
            movement.SetClearance(connector.From, connector.To, connector.Clearance);
        exploration.Bind(movement, partyId);
        actor.Bind(movement);
        features.Dressing.Bind(movement);
        FloorFactory.ConfigureDoorClearance(movement, floor, itemWorld.Door, services.Definitions.Combat.DoorClearance);

        Dictionary<string, GridPoint> drops = [];
        GeneratedFeatureSnapshot generatedFeatures = FloorFactory.CreateGeneratedFeatures(intent, floor, services.Definitions, inventory, drops, allocate);
        foreach (GeneratedGate gate in generatedFeatures.Gates) scene.SetDoor(gate.Cell, false);
        EncounterPlacementResult encounterPlacement = FloorFactory.CreateEnemies(floor, services.Definitions, inventory, itemWorld, movement,
            generatedFeatures, drops, allocate, party.Entities, out EnemyState[] enemies);
        generatedFeatures = FloorFactory.AddRouteSupplies(floor, services.Definitions, inventory, drops, generatedFeatures, allocate);
        inventory.BindRemaining(party.Entities);

        MagicState magic = new(services.Definitions.Magic, party.Members.Select(member => (member.Definition.Id, member.Definition.Archetype)),
            party.Entities, travelling?.Books);
        if (travelling is not null) magic.RejoinTravelling(travelling.Conditions);
        WorldFeatures world;
        try
        {
            world = new WorldFeatures(services.Engine, services.Art, scene, floor, services.Definitions.Features, services.Definitions.Art,
                features, services.AllocateLightId(), style);
        }
        catch
        {
            scene.Dispose();
            throw;
        }
        try
        {
            GeneratedFeatureState generated = new(generatedFeatures, services.Definitions, scene,
                exploration, itemWorld, actor, inventory, party, magic, services.ItemArt, services.Message, cell => services.Aim(scene, cell));
            CombatScope scope = new(scene, movement, exploration, itemWorld, actor, () => world, generated,
                floor, partyId, incomingDamageMultiplier, allocate,
                cell => services.Aim(scene, cell),
                services.Message, services.Sound, services.CancelRest);
            // Member action states ride the travelling entities (travel only runs
            // idle); the multiplier never applies here since no damage runs.
            RiflesCombat combat = RiflesCombat.CreateFresh(services.Definitions, party.Entities, party, magic, inventory, scope, [.. enemies],
                [RiflesCombat.FreshAlly(actor.Id, services.Definitions), RiflesCombat.FreshAlly(features.Dressing.ObserverId, services.Definitions)],
                drops, travelling?.Weapons);
            return new ActiveFloor(floor, scene, movement, world, actor, exploration, itemWorld, inventory, magic, combat,
                generated, encounterPlacement, floorId, party);
        }
        catch
        {
            world.Dispose();
            scene.Dispose();
            throw;
        }
    }
}
