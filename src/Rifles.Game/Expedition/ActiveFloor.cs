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
    ulong[] LoadedWeapons);

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
        MagicState magic, RiflesCombat combat, GeneratedFeatureSnapshot generatedFeatures,
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
        GeneratedFeatures = generatedFeatures;
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
    internal GeneratedFeatureSnapshot GeneratedFeatures { get; private set; }
    internal EncounterPlacementResult EncounterPlacement { get; }
    internal ulong FloorId { get; }
    /// <summary>Reference to the travelling party, not floor ownership.</summary>
    internal PartyState Party { get; }

    /// <summary>
    /// Replaces the floor's generated-feature facts (lever/gate/hazard
    /// transitions). The only mutation slot on the aggregate; everything else
    /// mounts once.
    /// </summary>
    internal void UpdateGeneratedFeatures(GeneratedFeatureSnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        GeneratedFeatures = value;
    }

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
    internal static ActiveFloor Restore(ExpeditionSnapshot saved, GameDefinitions definitions,
        PartyState? travellingParty, IReadOnlyDictionary<string, MagicState.MagicBook>? travellingBooks,
        string[] rewards, IReadOnlyDictionary<ulong, string>? allItems, long completionExperience,
        IEngineContext engine, DungeonMaterialCache materials, GeneratedArt artResources, ItemArt itemArt,
        Func<ulong> allocateLightId, string? style, bool roomLights, double incomingDamageMultiplier,
        ulong partyId, Action<string> message, Action<Audio.SoundCue, System.Numerics.Vector3> sound,
        Action<string> cancelRest, Func<ulong, string?> featureUseProblem,
        Func<ulong, System.Numerics.Vector3> featurePoint, Func<ulong, ulong, string> useFeature, Func<ulong> allocateId,
        Func<DungeonScene, GridPoint, Vector3> aim)
    {
        var restored = ExpeditionCodec.Validate(saved, definitions, rewards,
            allItems ?? saved.Inventory.Packs.SelectMany(p => p.Items).ToDictionary(i => i.Id, i => i.Definition),
            completionExperience, travellingParty, travellingBooks);
        ItemInventory restoredInventory = ItemInventory.Restore(definitions.Items, saved.Inventory);
        RestoredCombat restoredCombat = CombatRestore.Validate(saved.Combat, definitions, saved.Floor, restoredInventory,
            restored.Party, saved.PartyId, new[] { saved.Actor.Id, saved.Features.Dressing.ObserverId },
            rewards, completionExperience, travellingBooks);
        ExplorationItems restoredItems = new(definitions.ItemExploration, saved.ItemWorld);
        DungeonScene replacement = new(engine, materials, saved.Floor, definitions.Exploration, definitions.Appearance,
            allocateLightId, definitions.ItemExploration);
        MovementGrid replacementGrid = new(saved.Floor.Cells.ToHashSet(), replacement.AdmitStep, definitions.Crowd);
        try
        {
            replacement.SetDoor(saved.ItemWorld.Door, saved.ItemWorld.DoorOpen);
            foreach (var gate in saved.GeneratedFeatures.Gates) replacement.SetDoor(gate.Cell, gate.Open);
            foreach (var connector in saved.Floor.Connectors) replacementGrid.SetClearance(connector.From, connector.To, connector.Clearance);
            foreach (var direction in Rifles.Procgen.Generation.CardinalDirections.Ordered)
                if (saved.Floor.Cells.Contains(saved.ItemWorld.Door + direction.Offset()))
                    replacementGrid.SetClearance(saved.ItemWorld.Door, saved.ItemWorld.Door + direction.Offset(), definitions.Combat.DoorClearance);
            if (restored.Party.Members.Any(m => m.IsLiving)) restored.Exploration.Bind(replacementGrid, saved.PartyId);
            if (restoredCombat.Allies.Single(a => a.Id == saved.Actor.Id).Vitality > 0) restored.Actor.Bind(replacementGrid);
        }
        catch { replacement.Dispose(); throw; }
        WorldFeatures replacementFeatures;
        try
        {
            replacementFeatures = new WorldFeatures(engine, artResources, replacement, saved.Floor, definitions.Features,
                definitions.Art, saved.Features, allocateLightId(), style);
        }
        catch { replacement.Dispose(); throw; }
        try
        {
            if (style is not null && replacement.Style != style) replacement.SetStyle(style);
            replacement.SetRoomLights(roomLights);
            replacementFeatures.Bind(replacementGrid, restoredCombat.Allies.Single(a => a.Id == saved.Features.Dressing.ObserverId).Vitality > 0);
            foreach (var ally in restoredCombat.Allies.Where(a => a.Vitality == 0)) replacementGrid.Remove(ally.Id);
            foreach (EnemyState enemy in restoredCombat.Enemies.Where(e => e.Alive)) enemy.Motion.Bind(replacementGrid, enemy.Id, enemy.Definition.Footprint, enemy.Definition.Faction, enemy.Definition.Share);
            replacementFeatures.Present(restored.Actor, restored.Exploration, itemArt.Facts(restoredInventory, restoredItems, replacement));
        }
        catch
        {
            replacementFeatures.Dispose(); replacement.Dispose(); throw;
        }
        restoredInventory.BindMembers(restored.Party.Entities, restored.Party.Members);
        restoredInventory.BindRemaining(restored.Party.Entities);
        CombatScope scope = new(replacement, replacementGrid, restored.Exploration, restoredItems, restored.Actor,
            () => replacementFeatures, saved.GeneratedFeatures, saved.Floor, partyId, incomingDamageMultiplier, allocateId,
            cell => aim(replacement, cell), message, sound, cancelRest, featureUseProblem, featurePoint, useFeature);
        RiflesCombat combat = RiflesCombat.Restore(restoredCombat, definitions, restored.Party.Entities, restored.Party,
            restoredCombat.Magic, restoredInventory, scope);
        return new ActiveFloor(saved.Floor, replacement, replacementGrid, replacementFeatures, restored.Actor,
            restored.Exploration, restoredItems, restoredInventory, restoredCombat.Magic, combat,
            saved.GeneratedFeatures, saved.EncounterPlacement, saved.FloorId, restored.Party);
    }

    /// <summary>
    /// Builds and mounts a fresh floor directly. The travelling party object
    /// moves untouched — no duplicate party, no snapshot roundtrip. Fresh
    /// member packs receive starter grants only on a fresh run; travelling
    /// packs restore into them without duplicating the kit.
    /// </summary>
    internal static ActiveFloor CreateFresh(GameDefinitions definitions, ResolvedExpedition intent, string floorKey,
        ulong floorId, ulong partyId, PartyState party, TravellingState? travelling, ExplorationSnapshot pose,
        IEngineContext engine, DungeonMaterialCache materials, GeneratedArt artResources, Func<ulong> allocateLightId,
        Func<ulong> allocate, string? preset, string? style, double incomingDamageMultiplier,
        Action<string> message, Action<Audio.SoundCue, System.Numerics.Vector3> sound, Action<string> cancelRest,
        Func<ulong, string?> featureUseProblem, Func<ulong, System.Numerics.Vector3> featurePoint,
        Func<ulong, ulong, string> useFeature, Func<DungeonScene, GridPoint, Vector3> aim, DungeonFloor? resolvedFloor = null)
    {
        DungeonFloor floor = resolvedFloor ?? DungeonFloor.Generate(intent.Floors.Single(f => f.Id == floorKey),
            definitions.Generation.Policy, definitions.Rooms, definitions.Generation.Elevation)
            .WithArchitecture(definitions.Architecture);
        ResolvedFloorIntent resolved = intent.Floors.Single(f => f.Id == floorKey);
        // Caller-mismatch guard only: the intent graph re-hash that the old
        // snapshot factory ran here re-proved generation output against its
        // own input. Floor identity plus seed equality suffices.
        GameDefinitions.Require(floor.IntentFloorId == resolved.Id && floor.Seed == resolved.Candidate.Seed,
            "Resolved floor does not belong to the requested expedition floor.");

        DungeonScene scene = new(engine, materials, floor, definitions.Exploration, definitions.Appearance,
            allocateLightId, definitions.ItemExploration);
        try
        {
            return CreateFreshInner(definitions, intent, floorKey, floorId, partyId, party, travelling, pose, engine,
                materials, artResources, allocateLightId, allocate, preset, style, incomingDamageMultiplier,
                message, sound, cancelRest, featureUseProblem, featurePoint, useFeature, aim, floor, scene, resolvedFloor);
        }
        catch
        {
            scene.Dispose();
            throw;
        }
    }

    private static ActiveFloor CreateFreshInner(GameDefinitions definitions, ResolvedExpedition intent, string floorKey,
        ulong floorId, ulong partyId, PartyState party, TravellingState? travelling, ExplorationSnapshot pose,
        IEngineContext engine, DungeonMaterialCache materials, GeneratedArt artResources, Func<ulong> allocateLightId,
        Func<ulong> allocate, string? preset, string? style, double incomingDamageMultiplier,
        Action<string> message, Action<Audio.SoundCue, Vector3> sound, Action<string> cancelRest,
        Func<ulong, string?> featureUseProblem, Func<ulong, Vector3> featurePoint,
        Func<ulong, ulong, string> useFeature, Func<DungeonScene, GridPoint, Vector3> aim,
        DungeonFloor floor, DungeonScene scene, DungeonFloor? resolvedFloor)
    {
        ExplorationState exploration = ExplorationState.Restore(pose, floor, definitions.Exploration);
        PatrolActor actor = PatrolActor.Create(allocate(), floor,
            definitions.Exploration with { StepSeconds = definitions.Features.ActorStepSeconds }, definitions.Features);
        FeatureSnapshot features = new(allocate(), allocate(), 1, true, false,
            RoomDressing.Create(floor, actor, definitions.Art, allocate));

        ExplorationItems itemWorld = ExplorationItems.Create(definitions.ItemExploration, floor, features.Dressing, actor, allocate);
        // Travelling packs keep their ledger identity: the floor registers
        // the travelled owners instead of allocating fresh ones, so restored
        // contents land on the registered record. Fresh runs allocate.
        PackOwner TravellingOrFresh(string key, Func<PackOwner> fresh) =>
            travelling?.Packs.SingleOrDefault(p => p.Owner.Key == key)?.Owner ?? fresh();
        IEnumerable<PackOwner> memberPacks = party.Members.Select(member => TravellingOrFresh("member:" + member.Definition.Id,
            () => new PackOwner(allocate(), "member:" + member.Definition.Id, definitions.Items.Backpack.Mass, definitions.Items.Backpack.Space)));
        PackOwner partyPack = TravellingOrFresh(ItemInventory.PartyKey,
            () => new(allocate(), ItemInventory.PartyKey, definitions.Items.Party.Mass, definitions.Items.Party.Space));
        IEnumerable<PackOwner> anchorPacks = itemWorld.Anchors.Select(anchor =>
        {
            PackDefinition capacity = anchor.Key == "crate" ? definitions.Items.Container : definitions.Items.Anchor;
            return new PackOwner(anchor.Id, anchor.Key, capacity.Mass, capacity.Space);
        });
        ItemInventory inventory = new(definitions.Items, memberPacks.Append(partyPack).Concat(anchorPacks));
        inventory.BindMembers(party.Entities, party.Members);
        // Starter grants land in floor-local owners on every fresh floor;
        // member and party kits travel instead of granting again.
        inventory.GrantStarting(allocate, preset, owner => travelling is null
            || InventoryOwner.Parse(owner) is not (MemberOwner or PartyOwner));
        if (travelling is not null)
            foreach (SavedPack pack in travelling.Packs) inventory.RestorePack(pack);
        scene.SetDoor(itemWorld.Door, false);

        MovementGrid movement = new(floor.Cells.ToHashSet(), scene.AdmitStep, definitions.Crowd);
        foreach (FloorConnector connector in floor.Connectors)
            movement.SetClearance(connector.From, connector.To, connector.Clearance);
        exploration.Bind(movement, partyId);
        actor.Bind(movement);
        features.Dressing.Bind(movement);
        FloorFactory.ConfigureDoorClearance(movement, floor, itemWorld.Door, definitions.Combat.DoorClearance);

        Dictionary<string, GridPoint> drops = [];
        GeneratedFeatureSnapshot generatedFeatures = FloorFactory.CreateGeneratedFeatures(intent, floor, definitions, inventory, drops, scene, allocate);
        EncounterPlacementResult encounterPlacement = FloorFactory.CreateEnemies(floor, definitions, inventory, itemWorld, movement,
            generatedFeatures, drops, allocate, party.Entities, out EnemyState[] enemies);
        generatedFeatures = FloorFactory.AddRouteSupplies(floor, definitions, inventory, encounterPlacement, drops, generatedFeatures, allocate);
        inventory.BindRemaining(party.Entities);

        MagicState magic = new(definitions.Magic, party.Members.Select(member => (member.Definition.Id, member.Definition.Archetype)),
            party.Entities, travelling?.Books);
        if (travelling is not null) magic.RejoinTravelling(travelling.Conditions);
        WorldFeatures world;
        try
        {
            world = new WorldFeatures(engine, artResources, scene, floor, definitions.Features, definitions.Art,
                features, allocateLightId(), style);
        }
        catch
        {
            scene.Dispose();
            throw;
        }
        try
        {
            CombatScope scope = new(scene, movement, exploration, itemWorld, actor, () => world, generatedFeatures,
                floor, partyId, incomingDamageMultiplier, allocate,
                cell => aim(scene, cell),
                message, sound, cancelRest, featureUseProblem, featurePoint, useFeature);
            // Member action states ride the travelling entities (travel only runs
            // idle); the multiplier never applies here since no damage runs.
            RiflesCombat combat = RiflesCombat.CreateFresh(definitions, party.Entities, party, magic, inventory, scope, [.. enemies],
                [new(actor.Id, definitions.Combat.AllyVitality), new(features.Dressing.ObserverId, definitions.Combat.AllyVitality)],
                travelling?.LoadedWeapons ?? []);
            return new ActiveFloor(floor, scene, movement, world, actor, exploration, itemWorld, inventory, magic, combat,
                generatedFeatures, encounterPlacement, floorId, party);
        }
        catch
        {
            world.Dispose();
            scene.Dispose();
            throw;
        }
    }
}
