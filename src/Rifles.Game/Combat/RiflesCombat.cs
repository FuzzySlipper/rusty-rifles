using System.Numerics;
using Rifles.Game.Audio;
using Rifles.Game.Characters;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Items;
using Rifles.Game.Magic;
using Rifles.Game.Party;
using Rifles.Game.Presentation;
using Rifles.Procgen.Generation;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;

namespace Rifles.Game.Combat;

/// <summary>
/// The composed combat/action owner: member action states attached to
/// character entities, live enemy/flight/drop/loaded state, and the named
/// attack/cast/remedy operations input, UI, and AI all call. The product root
/// keeps lifecycle, update ordering, and defeat aftermath; saves use the
/// snapshot DTOs via <see cref="Capture"/> and <see cref="Restore"/>.
/// </summary>
internal sealed partial class RiflesCombat
{
    private readonly GameDefinitions definitions;
    private readonly CharacterEntities entities;
    private readonly PartyState party;
    private readonly MagicState magic;
    private readonly ItemInventory inventory;
    private readonly CombatScope scope;
    private readonly Dictionary<ulong, RiflesCharacter> allies;
    private readonly List<EnemyState> enemies;
    private readonly List<FlightState> flights;
    private readonly Dictionary<string, GridPoint> drops;
    private WeaponState weapons;
    private ChargeState charge = new();
    private readonly Queue<string> log = [];
    private ulong selectedTarget;
    private int pathCursor;

    private RiflesCombat(GameDefinitions definitions, CharacterEntities entities, PartyState party,
        MagicState magic, ItemInventory inventory, Dictionary<ulong, RiflesCharacter> allies,
        CombatScope scope, List<EnemyState> enemies)
    {
        this.definitions = definitions;
        this.entities = entities;
        this.party = party;
        this.magic = magic;
        this.inventory = inventory;
        this.allies = allies;
        this.scope = scope;
        this.enemies = enemies;
        flights = [];
        drops = [];
        weapons = WeaponState.Create(definitions.Items, inventory);
    }

    private CombatDefinition Combat => definitions.Combat;

    internal IReadOnlyList<EnemyState> Enemies => enemies;
    internal IReadOnlyDictionary<string, GridPoint> Drops => drops;
    internal WeaponState Weapons => weapons;
    internal IReadOnlyList<FlightState> Flights => flights;
    internal IReadOnlyDictionary<ulong, RiflesCharacter> Allies => allies;
    internal ulong SelectedTarget => selectedTarget;
    internal IReadOnlyCollection<string> Log => log;
    internal bool Defeated => party.Defeated;
    internal bool ChargeExecuting => charge.Executing;

    internal ActionState ActionOf(RiflesCharacter member) =>
        new Actor(entities.Store, member.Entity).Get<ActionState>();

    internal bool ActionsBusy => party.Members.Any(m => ActionOf(m).Busy);
    internal bool HasFlights => flights.Count != 0;
    internal long SpellCost(string member, SpellDefinition spell) => Math.Max(0, spell.Cost - magic.CostDiscount(member));

    internal void AddLog(string message)
    {
        log.Enqueue(message);
        while (log.Count > Combat.LogLength) log.Dequeue();
    }

    internal void CancelAll()
    {
        if (ChargeExecuting) charge.Clear();
        foreach (RiflesCharacter member in party.Members) ActionOf(member).Cancel();
    }

    internal bool Threatened => enemies.Any(e => e.Alive && (e.Aware
        || Vector3.Distance(EnemyAim(e), scope.Aim(scope.Exploration.Position)) <= definitions.Magic.ThreatRange && SeesParty(e)));

    /// <summary>Fresh combat with generated world drops and empty actions/flights.</summary>
    internal static RiflesCombat CreateFresh(GameDefinitions definitions, CharacterEntities entities, PartyState party,
        MagicState magic, ItemInventory inventory, CombatScope scope, List<EnemyState> enemies, AllySnapshot[] allies,
        IReadOnlyDictionary<string, GridPoint> generatedDrops, WeaponStateSnapshot? travellingWeapons = null)
    {
        RiflesCombat combat = new(definitions, entities, party, magic, inventory, [], scope, enemies);
        foreach ((string owner, GridPoint cell) in generatedDrops) combat.drops.Add(owner, cell);
        foreach (RiflesCharacter member in party.Members)
            entities.AttachComponent(member.Definition.Id, () => new ActionState());
        combat.BuildAllies(allies);
        if (travellingWeapons is not null) combat.weapons = WeaponState.Restore(definitions.Items, travellingWeapons, inventory);
        return combat;
    }

    /// <summary>Live combat rebuilt from an admitted snapshot; save DTOs stay untouched.</summary>
    internal static RiflesCombat Restore(RestoredCombat saved, GameDefinitions definitions, CharacterEntities entities,
        PartyState party, MagicState magic, ItemInventory inventory, CombatScope scope)
    {
        ArgumentNullException.ThrowIfNull(saved);
        RiflesCombat combat = new(definitions, entities, party, magic, inventory, [], scope, [.. saved.Enemies]);
        foreach (RiflesCharacter member in party.Members)
        {
            if (!saved.Actions.TryGetValue(member.Definition.Id, out ActionState? action) || action is null)
                action = new ActionState();
            entities.AttachComponent(member.Definition.Id, () => action);
        }

        foreach (FlightSnapshot flight in saved.Flights) combat.flights.Add(FlightState.Restore(flight));
        foreach (DropSnapshot drop in saved.Drops) combat.drops.Add(drop.Owner, drop.Cell);
        combat.weapons = saved.Weapons;
        combat.charge = ChargeState.Restore(saved.Charge);
        combat.selectedTarget = saved.SelectedTarget;
        combat.BuildAllies(saved.Allies);
        return combat;
    }

    internal CombatSnapshot Capture() => new(enemies.Select(e => e.Capture()).ToArray(),
        party.Members.Select(member => new MemberActionSnapshot(member.Definition.Id, ActionOf(member).Capture())).ToArray(),
        weapons.Capture(inventory), flights.Select(f => f.Capture()).ToArray(),
        drops.Select(d => new DropSnapshot(d.Key, d.Value)).ToArray(),
        allies.Select(a => new AllySnapshot(a.Key, RiflesStats.SnapshotForPersistence(a.Value.Stats))).ToArray(), selectedTarget, magic.Capture(), charge.Capture());

    private void CombatMessage(string message)
    {
        // Single enqueue path: the product message writer sets feedback and
        // appends to this log. Enqueuing here as well duplicated every line.
        scope.Message(message);
    }

    private RiflesCharacter Member(string id) => party.Members.SingleOrDefault(m => m.Definition.Id == id)
        ?? throw new InvalidDataException("Character unavailable.");

    private void BuildAllies(AllySnapshot[] snapshots)
    {
        allies.Clear();
        foreach (AllySnapshot ally in snapshots)
            allies.Add(ally.Id, CreateAlly(ally.Id, ally.Stats));
    }

    internal static AllySnapshot FreshAlly(ulong id, GameDefinitions definitions)
    {
        MemberDefinition definition = AllyDefinition(id, definitions);
        return new(id, RiflesStats.SnapshotForPersistence(RiflesStats.ForMember(definition)));
    }

    internal static MemberDefinition AllyDefinition(ulong id, GameDefinitions definitions)
    {
        FormationPositionDefinition front = definitions.Party.Positions.OrderBy(position => position.Rank).First();
        return new(id.ToString(), "garrison-ally", "Garrison ally",
            front.Id, definitions.Combat.AllyVitality, StartingVitality: definitions.Combat.AllyVitality);
    }

    private RiflesCharacter CreateAlly(ulong id, StatsComponentSnapshot saved)
    {
        MemberDefinition definition = AllyDefinition(id, definitions);
        RiflesStats.AdmitStats(saved, RiflesStats.ForMember(definition));
        entities.Detach(definition.Id);
        (EntityId entity, StatsComponent stats) = entities.AttachStats(
            definition.Id, "rifles:ally", () => RiflesStats.RebuildForRestore(saved));
        FormationPositionDefinition front = definitions.Party.Positions.OrderBy(position => position.Rank).First();
        return new RiflesCharacter(new Actor(entities.Store, entity), definition, stats, front.Id, front.Rank);
    }

    private Vector3 Aim(GridPoint cell) => scope.Aim(cell);

    private SpatialEntityCollider[] CombatBodies()
    {
        List<SpatialEntityCollider> bodies = [];
        void Add(ulong id, GridPoint cell, Vector2 offset = default, float width = 0, float depth = 0)
        {
            Vector3 center = Aim(cell) + new Vector3(offset.X, 0, offset.Y) * scope.Scene.LogicalCellSize;
            width = width > 0 ? width : Combat.BodyWidth; depth = depth > 0 ? depth : Combat.BodyWidth;
            Vector3 min = new(center.X - width / 2, scope.Scene.GroundHeight(cell), center.Z - depth / 2);
            bodies.Add(new(id, min, min + new Vector3(width, Combat.BodyHeight, depth), 0, 0, true, false, false));
        }
        if (!Defeated) Add(scope.PartyId, scope.Exploration.Position);
        foreach (EnemyState enemy in enemies.Where(e => e.Alive))
        {
            var slot = scope.Movement.Placement(enemy.Id);
            Add(enemy.Id, enemy.Motion.Position, enemy.Motion.CrowdOffset, slot.Width * scope.Scene.LogicalCellSize, slot.Depth * scope.Scene.LogicalCellSize);
        }
        if (allies.GetValueOrDefault(scope.Actor.Id)?.IsLiving == true) Add(scope.Actor.Id, scope.Actor.Motion.Position);
        RoomDressing dressing = scope.Features().Dressing;
        if (allies.GetValueOrDefault(dressing.ObserverId)?.IsLiving == true) Add(dressing.ObserverId, dressing.Observer);
        Add(dressing.BenchId, dressing.Bench); Add(dressing.CrateId, dressing.Crate);
        return bodies.ToArray();
    }

    internal bool Visible(EnemyState enemy)
    {
        if (!enemy.Alive) return false;
        Vector3 direction = EnemyAim(enemy) - Aim(scope.Exploration.Position);
        var facing = scope.Exploration.Facing.Offset();
        if (direction.Length() > Combat.Action(CombatActionKind.Fire).Range
            || Vector3.Dot(Vector3.Normalize(direction), new Vector3(facing.X, 0, facing.Y)) < Math.Cos(Combat.TargetAngle * Math.PI / 180)) return false;
        SpatialHit hit = scope.Scene.Trace(Aim(scope.Exploration.Position), EnemyAim(enemy), CombatBodies(), scope.PartyId);
        return hit.Present && hit.Kind == SpatialHitKind.Entity && hit.Entity == enemy.Id;
    }

    internal CarriedItem? Weapon(string member) => inventory.Items("member:" + member).SingleOrDefault(i => i.Slots.Contains("weapon"));
    internal WeaponCapabilities? WeaponCapabilities(string member) => Capabilities(Weapon(member));
    private WeaponCapabilities? Capabilities(CarriedItem? weapon) => weapon is null ? null : weapons.Capabilities(weapon.Entity, inventory);
    private float ActionRange(CombatActionKind kind, WeaponCapabilities? weapon)
    {
        string? reach = kind == CombatActionKind.Fire ? weapon?.FireReach : kind == CombatActionKind.Melee ? weapon?.MeleeReach : null;
        return reach is null ? Combat.Action(kind).Range : definitions.Formation.Weapon(reach).MaximumForwardDistance;
    }
    // Enemy rifles retain their finite ammunition packs. Player muskets load
    // from their own persistent weapon state and never consume a party stack.
    private ulong Ammo(string owner) => inventory.Items(owner).SingleOrDefault(i => i.Definition == Combat.AmmunitionItem)?.Quantity ?? 0;
    private ActionSnapshot NewAction(CombatActionKind kind, ulong weapon = 0, string? token = null, string? source = null,
        ulong target = 0, string? member = null, GridPoint? aim = null, WeaponCapabilities? capabilities = null,
        GridPoint? orderOrigin = null, CardinalDirection? orderFacing = null, BayonetActionTiming? bayonetTiming = null)
    {
        ActionDefinition? tuning = CombatDefinition.UsesCombatTuning(kind) ? Combat.Action(kind) : null;
        double windup = bayonetTiming?.WindupSeconds ?? (capabilities is null ? tuning!.Windup
            : kind == CombatActionKind.Reload ? capabilities.ReloadSeconds : capabilities.WindupSeconds);
        double recovery = bayonetTiming?.RecoverySeconds ?? capabilities?.RecoverySeconds ?? tuning!.Recovery;
        return new(kind, weapon, token, source, target, member, windup, ActionPhase.Windup, recovery,
            aim, OrderOrigin: orderOrigin, OrderFacing: orderFacing);
    }

    internal GameOutcome BeginCombat(SessionCommand command, string selectedMember, bool paused)
    {
        if (ChargeExecuting) return GameOutcome.Reject("The party is charging.");
        if (command.Action is "attack" or "fire") return BeginOrder(CombatActionKind.Fire, paused);
        if (command.Action == "melee") return BeginOrder(CombatActionKind.Melee, paused);
        if (command.Action == "reload") return BeginReloadOrder(paused);
        if (command.Action == "target")
        {
            EnemyState? target = enemies.SingleOrDefault(e => e.Id == command.Target && Visible(e));
            if (target is null) return GameOutcome.Reject("Target is not visible.");
            selectedTarget = target.Id; CombatMessage("Target: " + target.Definition.Name); return GameOutcome.Accept();
        }
        if (paused || Defeated) return GameOutcome.Reject("Resume with a living party before acting.");
        RiflesCharacter member;
        try { member = Member(selectedMember); }
        catch (InvalidDataException) { return GameOutcome.Reject("Choose a living character."); }
        if (!member.IsLiving || member.Definition.Commander) return GameOutcome.Reject("Choose a living soldier.");
        if (party.Formation.Affects(member.InstanceId)) return GameOutcome.Reject("That character is repositioning.");
        ActionState state = ActionOf(member);
        if (command.Action == "interrupt")
        {
            if (state.Current?.Phase == ActionPhase.Recovery) return GameOutcome.Reject("Committed actions must finish recovery.");
            state.Cancel(); CombatMessage(member.Definition.Name + " interrupted; no cost committed."); return GameOutcome.Accept();
        }
        scope.CancelRest("Rest interrupted by an action.");
        if (state.Busy) return GameOutcome.Reject("That character is still acting.");
        CarriedItem? weapon = Weapon(selectedMember);
        WeaponCapabilities? capabilities = Capabilities(weapon);
        string owner = "member:" + selectedMember;
        CombatActionKind kind = command.Action switch
        {
            "throw" => CombatActionKind.Throw,
            "consume" => CombatActionKind.Consume,
            _ => capabilities is { FireDamage: > 0 } ? CombatActionKind.Fire : CombatActionKind.Melee,
        };
        if (kind is CombatActionKind.Reload or CombatActionKind.Fire)
        {
            if (capabilities is not { FireDamage: > 0 }) return GameOutcome.Reject("Equip a musket first.");
            if (kind == CombatActionKind.Fire && !capabilities.Loaded) return GameOutcome.Reject("Dry musket — reload first.");
            if (kind == CombatActionKind.Reload && capabilities.Loaded) return GameOutcome.Reject("Musket already loaded.");
        }
        if (kind == CombatActionKind.Melee && capabilities is not { MeleeDamage: > 0, MeleeReach: not null })
            return GameOutcome.Reject("Equip a melee weapon first.");
        string? token = null, source = null, targetMember = null;
        GridPoint? aim = null; ulong targetId = 0;
        if (kind is CombatActionKind.Throw or CombatActionKind.Consume)
        {
            source = command.Source ?? owner;
            try { RequireItemAccess(source); }
            catch (InvalidDataException error) { return GameOutcome.Reject(error.Message); }
            token = command.Item;
            if (token is null) return GameOutcome.Reject("Select an item.");
            _ = inventory.Find(source, token);
            if (kind == CombatActionKind.Consume)
            {
                targetMember = command.Member ?? selectedMember;
                ValidateRemedy(source, token, targetMember);
            }
            else if (command.Destination == "plate")
            {
                var focused = scope.Features().Readout?.Selected;
                aim = scope.GeneratedFeatures.Plates.SingleOrDefault(p => focused is { } target && p.Id == target.Id)?.Cell
                    ?? scope.ItemWorld.Anchor("plate").Cell;
            }
        }
        if (kind is CombatActionKind.Fire or CombatActionKind.Melee or CombatActionKind.Throw && aim is null)
        {
            targetId = command.Target ?? selectedTarget;
            EnemyState? visibleTarget = enemies.SingleOrDefault(e => e.Id == targetId && Visible(e));
            if (visibleTarget is null) return GameOutcome.Reject("Select a visible enemy.");
            aim = visibleTarget.Motion.Position;
        }
        if (aim is { } cell && Vector3.Distance(Aim(scope.Exploration.Position), Aim(cell)) > ActionRange(kind, capabilities))
            return GameOutcome.Reject("Target is out of range.");
        ActionSnapshot prepared = NewAction(kind, kind is CombatActionKind.Melee or CombatActionKind.Fire or CombatActionKind.Reload ? weapon?.Entity ?? 0 : 0,
            token, source, targetId, targetMember, aim, capabilities);
        EnemyState? aimedEnemy = enemies.SingleOrDefault(e => e.Id == targetId);
        if (aimedEnemy is not null) prepared = prepared with { AimOffsetX = aimedEnemy.Motion.CrowdOffset.X, AimOffsetY = aimedEnemy.Motion.CrowdOffset.Y };
        state.Start(prepared);
        CombatMessage(member.Definition.Name + ": " + kind + " windup");
        return GameOutcome.Accept();
    }

    private void ValidateRemedy(string source, string token, string targetId)
    {
        RiflesCharacter target = Member(targetId);
        GearDefinition use = definitions.Items.Item(inventory.Find(source, token).Definition);
        if (!target.IsLiving || use.Use is not (ItemUse.Vitality or ItemUse.Resource)) throw new InvalidDataException("Choose a living character and a remedy.");
        if (use.Use == ItemUse.Vitality && target.Vitality == target.MaximumVitality || use.Use == ItemUse.Resource && target.Resource == target.MaximumResource)
            throw new InvalidDataException("That character needs no restoration.");
    }

    private void CommitMember(string memberId, ActionSnapshot action)
    {
        RiflesCharacter member = Member(memberId);
        if (!member.IsLiving) return;
        if (action.Kind == CombatActionKind.Cast) { CommitSpell(memberId, null, action); return; }
        if (action.Kind is CombatActionKind.Fire or CombatActionKind.Reload or CombatActionKind.Melee or CombatActionKind.FixBayonet or CombatActionKind.UnfixBayonet
            && (Weapon(memberId)?.Entity ?? 0) != action.Weapon) throw new InvalidDataException("Equipment changed; action interrupted.");
        if (action.Kind == CombatActionKind.Reload)
        {
            weapons.SetLoaded(action.Weapon, inventory, true);
            scope.Sound(SoundCue.Reload, Aim(scope.Exploration.Position));
            CombatMessage(member.Definition.Name + " loaded one round."); return;
        }
        if (action.Kind is CombatActionKind.FixBayonet or CombatActionKind.UnfixBayonet)
        {
            bool fix = action.Kind == CombatActionKind.FixBayonet;
            weapons.SetBayonetFixed(action.Weapon, inventory, fix);
            CombatMessage(member.Definition.Name + (fix ? " fixed a bayonet." : " removed a bayonet.")); return;
        }
        if (action.Kind == CombatActionKind.Consume)
        {
            RequireItemAccess(action.SourceOwner!); ValidateRemedy(action.SourceOwner!, action.ItemToken!, action.TargetMember!);
            GearDefinition use = definitions.Items.Item(inventory.Find(action.SourceOwner!, action.ItemToken!).Definition);
            inventory.PrepareUse(ItemRef.Parse(action.SourceOwner!, action.ItemToken!), inventory.Revision).Publish();
            RiflesCharacter target = Member(action.TargetMember!);
            long restored = use.Use == ItemUse.Vitality ? target.Heal(use.Effect) : target.RecoverResource(use.Effect);
            CombatMessage(target.Definition.Name + " restored " + restored + " " + use.Use); return;
        }
        WeaponCapabilities? capabilities = action.Weapon == 0 ? null : weapons.Capabilities(action.Weapon, inventory);
        OrderTarget? orderedTarget = capabilities is null ? null : ResolveOrderTarget(member, action, capabilities);
        if (action.OrderOrigin is not null && orderedTarget is null)
        {
            CombatMessage(member.Definition.Name + " found no replacement in the committed forward area."); return;
        }
        if (action.Kind == CombatActionKind.Fire && !weapons.Capabilities(action.Weapon, inventory).Loaded) throw new InvalidDataException("Dry musket.");
        if (action.Kind == CombatActionKind.Fire) weapons.SetLoaded(action.Weapon, inventory, false);
        if (action.Kind == CombatActionKind.Fire) EmitNoise(scope.Exploration.Position, NoiseKind.Gunfire);
        GridPoint cell = orderedTarget?.Enemy.Motion.Position ?? action.AimCell!.Value;
        if (action.Kind is CombatActionKind.Throw)
        {
            RequireItemAccess(action.SourceOwner!);
            _ = inventory.Find(action.SourceOwner!, action.ItemToken!);
            ulong pack = scope.AllocateIds(); string flightOwner = "combat:flight:" + pack;
            inventory.RegisterOwner(new(pack, flightOwner, Combat.DropCapacity.Mass, Combat.DropCapacity.Space));
            inventory.BindRemaining(entities);
            inventory.Transfer(ItemRef.Parse(action.SourceOwner!, action.ItemToken!), InventoryOwner.Parse(flightOwner), 1, inventory.Revision);
            Launch(scope.PartyId, memberId, action.Kind, scope.Exploration.Position, cell, flightOwner, action.Target == 0 ? "plate" : null, new(action.AimOffsetX, action.AimOffsetY));
            CombatMessage(member.Definition.Name + " released " + action.Kind); return;
        }
        Vector3 start = Aim(scope.Exploration.Position), end = orderedTarget is null
            ? Aim(cell) + new Vector3(action.AimOffsetX, 0, action.AimOffsetY) * scope.Scene.LogicalCellSize : EnemyAim(orderedTarget.Enemy);
        if (Vector3.Distance(start, end) > ActionRange(action.Kind, capabilities)) { CombatMessage("Attack missed — target outside reach."); return; }
        ulong attackTarget = orderedTarget?.Enemy.Id ?? action.Target;
        if (action.Kind == CombatActionKind.Fire && capabilities is not null && !weapons.RollFireHit(action.Weapon, inventory, attackTarget))
        {
            CombatMessage(member.Definition.Name + " missed (accuracy " + (capabilities.Accuracy * 100).ToString("0") + "%)."); return;
        }
        SpatialHit hit = action.OrderOrigin is null ? scope.Scene.Trace(start, end, CombatBodies(), scope.PartyId) : TraceOrderHit(start, end);
        ResolveHit(hit, action.Kind, memberId, scope.PartyId, end - start,
            action.Kind == CombatActionKind.Fire ? capabilities?.FireDamage : capabilities?.MeleeDamage);
    }

    internal void Advance(double seconds)
    {
        if (seconds <= 0) return;
        if (Defeated) { CancelAll(); return; }
        AdvanceFlights(seconds);
        foreach (RiflesCharacter member in party.Members)
        {
            if (Defeated) { CancelAll(); break; }
            ActionState state = ActionOf(member);
            if (!member.IsLiving) { state.Cancel(); continue; }
            if (state.Current is { Phase: ActionPhase.Windup } pending
                && pending.Kind is CombatActionKind.Melee or CombatActionKind.Fire or CombatActionKind.Reload or CombatActionKind.FixBayonet or CombatActionKind.UnfixBayonet
                && (Weapon(member.Definition.Id)?.Entity ?? 0) != pending.Weapon)
            { state.Cancel(); CombatMessage(member.Definition.Name + " interrupted by equipment change."); }
            try { state.Advance(seconds * magic.Speed(new MemberTarget(member.Definition.Id)), action => CommitMember(member.Definition.Id, action)); }
            catch (InvalidDataException error) { CombatMessage(error.Message); }
            catch (InvalidOperationException error) { CombatMessage("Action interrupted: " + error.Message); }
            BeginAutomaticReload(member, state);
        }
        AdvanceEnemies(seconds);
    }

    private void CommitEnemy(EnemyState enemy, ActionSnapshot action)
    {
        if (!enemy.Alive || Defeated) return;
        if (action.Kind == CombatActionKind.Cast) { CommitSpell(null, enemy, action); return; }
        if (action.Kind == CombatActionKind.Reload)
        {
            inventory.Consume(InventoryOwner.Parse(enemy.Owner), Combat.AmmunitionItem, 1); enemy.Loaded = true;
            scope.Sound(SoundCue.Reload, EnemyAim(enemy));
            CombatMessage(enemy.Definition.Name + " loaded a round."); return;
        }
        if (action.Kind == CombatActionKind.Fire)
        {
            if (!enemy.Loaded) return;
            enemy.Loaded = false;
            EmitNoise(enemy.Motion.Position, NoiseKind.Gunfire, enemy.Id);
        }
        Vector3 start = EnemyAim(enemy), end = Aim(action.AimCell!.Value);
        if (Vector3.Distance(start, end) > Combat.Action(action.Kind).Range) { CombatMessage(enemy.Definition.Name + " missed."); return; }
        ResolveHit(scope.Scene.Trace(start, end, CombatBodies(), enemy.Id), action.Kind, null, enemy.Id, end - start);
    }

    private void ResolveHit(SpatialHit hit, CombatActionKind kind, string? member, ulong shooter, Vector3 direction, long? weaponDamage = null)
    {
        if (!hit.Present) { CombatMessage(kind + " missed."); return; }
        if (hit.Kind != SpatialHitKind.Entity) { CombatMessage(kind + " blocked by masonry or a closed gate."); return; }
        scope.Sound(SoundCue.Impact, hit.Entity == scope.PartyId ? Aim(scope.Exploration.Position) : Aim(enemies.FirstOrDefault(e => e.Id == hit.Entity)?.Motion.Position ?? scope.Exploration.Position));
        long damage = (weaponDamage ?? Combat.Action(kind).Damage) + (member is null ? 0 : Member(member).Power);
        EnemyState? enemy = enemies.SingleOrDefault(e => e.Id == hit.Entity && e.Alive);
        if (enemy is not null)
        {
            if (shooter != scope.PartyId && !Combat.FriendlyFire) { CombatMessage("Shot stopped by a friendly body."); return; }
            DamageEnemy(enemy, Math.Max(Combat.MinimumDamage, damage - enemy.Definition.Defense));
            return;
        }
        if (hit.Entity == scope.PartyId)
        {
            if (shooter == scope.PartyId && !Combat.FriendlyFire) return;
            // Incoming direction selects one authored screening sector/lane.
            // A gap, or a directionless source, exposes the commander; this
            // single hit never falls through to another soldier.
            RiflesCharacter? target = MemberInLineOfFire(direction);
            if (target is null) return;
            scope.CancelRest("Rest interrupted by damage.");
            long applied = DamageMember(target, Math.Max(Combat.MinimumDamage, damage - target.Defense));
            CombatMessage(target.Definition.Name + " took " + applied + " damage" + (target.IsLiving ? "." : " and died. Pack retained."));
            if (!target.IsLiving) { ActionOf(target).Cancel(); magic.Clear(new MemberTarget(target.Definition.Id)); }
            return;
        }
        if (allies.TryGetValue(hit.Entity, out RiflesCharacter? ally) && Combat.FriendlyFire)
        {
            ally.ApplyDamage(damage);
            if (!ally.IsLiving) { if (hit.Entity == scope.Actor.Id) { scope.Actor.Motion.Stop(); scope.Actor.Motion.Detach(); } scope.Movement.Remove(hit.Entity); }
            CombatMessage("Garrison ally " + (ally.IsLiving ? "wounded." : "fell."));
        }
        else CombatMessage("Attack stopped by a world body; friendly damage disabled.");
    }

    internal void DamageEnemy(EnemyState enemy, long damage)
    {
        long applied = enemy.Damage(damage);
        enemy.Brain.Observe(null, scope.Exploration.Position); CombatMessage(enemy.Definition.Name + " took " + applied + " damage.");
        if (!enemy.Alive)
        {
            magic.Clear(new EnemyTarget(enemy.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))); magic.Reward(enemy.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            enemy.Action.Cancel(); enemy.Motion.Stop(); scope.Movement.Remove(enemy.Id); enemy.Motion.Detach();
            drops[enemy.Owner] = enemy.Motion.Position;
            // An enemy's loaded round stays with its unique rifle on death.
            if (enemy.Loaded)
            {
                CarriedItem? rifle = inventory.Items(enemy.Owner).FirstOrDefault(i => definitions.Items.Item(i.Definition).Ammunition.Length > 0 && i.Entity != 0);
                if (rifle is not null) weapons.SetLoaded(rifle.Entity, inventory, true);
                enemy.Loaded = false;
            }
            CombatMessage(enemy.Definition.Name + " fell. Belongings can be recovered.");
        }
    }

    internal long DamageMember(RiflesCharacter member, long damage)
    {
        scope.CancelRest("Rest interrupted by injury.");
        long applied = member.ApplyDamage(checked((long)Math.Ceiling(damage * scope.IncomingDamageMultiplier)));
        if (!member.IsLiving) { ActionOf(member).Cancel(); magic.Clear(new MemberTarget(member.Definition.Id)); }
        return applied;
    }

    private void Launch(ulong shooter, string? member, CombatActionKind kind, GridPoint source, GridPoint target, string? owner, string? destination, Vector2 targetOffset = default)
    {
        Vector3 position = Aim(source), offset = Aim(target) + new Vector3(targetOffset.X, 0, targetOffset.Y) * scope.Scene.LogicalCellSize - position;
        if (offset.LengthSquared() == 0) offset = new Vector3(scope.Exploration.Facing.Offset().X, 0, scope.Exploration.Facing.Offset().Y);
        Vector3 direction = Vector3.Normalize(offset);
        float distance = Math.Min(offset.Length(), Combat.Action(kind).Range);
        flights.Add(new FlightState(scope.AllocateIds(), shooter, member, kind, position.X, position.Y, position.Z, direction.X, direction.Y, direction.Z,
            distance, source, owner, destination));
    }

    private void AdvanceFlights(double seconds)
    {
        foreach (FlightState flight in flights.ToArray())
        {
            Vector3 start = new(flight.X, flight.Y, flight.Z);
            float travel = Math.Min(flight.Remaining, (float)((flight.Spell is null ? Combat.Action(flight.Kind).Speed : definitions.Magic.Spell(flight.Spell).Speed) * seconds));
            Vector3 end = start + new Vector3(flight.DirectionX, flight.DirectionY, flight.DirectionZ) * travel;
            SpatialHit hit = scope.Scene.Trace(start, end, CombatBodies(), flight.Shooter);
            GridPoint landed = new((int)MathF.Floor(end.X / scope.Scene.LogicalCellSize), (int)MathF.Floor(end.Z / scope.Scene.LogicalCellSize));
            if (hit.Present)
            {
                if (hit.Kind == SpatialHitKind.Entity) landed = new((int)MathF.Floor(hit.Point.X / scope.Scene.LogicalCellSize), (int)MathF.Floor(hit.Point.Z / scope.Scene.LogicalCellSize));
                else
                {
                    // Move one representable float toward the incoming segment, outside the wall boundary.
                    float Before(float value, float direction) => direction > 0 ? MathF.BitDecrement(value) : direction < 0 ? MathF.BitIncrement(value) : value;
                    landed = new((int)MathF.Floor(Before(hit.Point.X, flight.DirectionX) / scope.Scene.LogicalCellSize),
                        (int)MathF.Floor(Before(hit.Point.Z, flight.DirectionZ) / scope.Scene.LogicalCellSize));
                }
                if (flight.Spell is null) ResolveHit(hit, flight.Kind, flight.Member, flight.Shooter,
                    new Vector3(flight.DirectionX, flight.DirectionY, flight.DirectionZ));
            }
            if (!scope.Floor.Cells.Contains(landed) || landed == scope.ItemWorld.Door && !scope.ItemWorld.DoorOpen
                || scope.GeneratedFeatures.Gates.Any(g => g.Cell == landed && !g.Open)) landed = flight.LastCell;
            flights.Remove(flight);
            if (hit.Present || travel >= flight.Remaining)
            {
                if (flight.Spell is not null) ResolveSpellImpact(flight, hit, hit.Present ? hit.Point : end);
                if (flight.Owner is not null)
                {
                    drops[flight.Owner] = landed;
                    if (!Combat.RecoverThrownItems)
                    {
                        CarriedItem consumed = inventory.Items(flight.Owner).Single();
                        inventory.Destroy(ItemRef.Parse(flight.Owner, consumed.Token));
                        CombatMessage("Thrown item consumed on impact."); continue;
                    }
                    if (landed == scope.ItemWorld.Anchor("plate").Cell)
                    {
                        CarriedItem item = inventory.Items(flight.Owner).Single();
                        try { inventory.Transfer(ItemRef.Parse(flight.Owner, item.Token), InventoryOwner.Parse("plate"), item.Quantity, inventory.Revision); }
                        catch (InvalidDataException) { /* Occupied plate: keep the recoverable pile at this cell. */ }
                    }
                    CombatMessage("Thrown item landed; recover it from nearby ground inventory.");
                }
            }
            else
            {
                flight.X = end.X; flight.Y = end.Y; flight.Z = end.Z;
                flight.Remaining -= travel; flight.LastCell = landed;
                flights.Add(flight);
            }
        }
    }

    internal bool DropReachable(string owner) => drops.TryGetValue(owner, out GridPoint cell)
        && scope.ItemWorld.Reachable(Aim(cell), scope.Exploration, scope.Scene);

    /// <summary>
    /// Converts an actual incoming world direction to the authored
    /// facing-relative sector/lane. Screening is a PartyState lookup; a gap
    /// returns the commander and never selects an unrelated living soldier.
    /// </summary>
    private RiflesCharacter? MemberInLineOfFire(Vector3 direction)
    {
        if (direction.X == 0 && direction.Z == 0) return party.Commander;
        FormationApproach approach = FormationRules.DeriveApproach(scope.Exploration.Facing,
            new Vector2(-direction.X, -direction.Z), definitions.Formation.LaneBoundaryRatio);
        return party.ScreenedRecipient(definitions.Formation, approach);
    }

    internal void RequireItemAccess(string owner)
    {
        if (ChargeExecuting) throw new InvalidDataException("The party is charging.");
        if (InventoryOwner.Parse(owner) is not CombatOwner) { scope.ItemWorld.RequireAccess(owner, scope.Exploration, scope.Scene); return; }
        if (!DropReachable(owner)) throw new InvalidDataException("Those belongings are not within reach.");
    }

    internal enum NoiseKind { Footstep, Gunfire, Alarm }
    private Vector3 EnemyAim(EnemyState enemy) => Aim(enemy.Motion.Position)
        + new Vector3(enemy.Motion.CrowdOffset.X, 0, enemy.Motion.CrowdOffset.Y) * scope.Scene.LogicalCellSize;

    internal void EmitNoise(GridPoint cell, NoiseKind kind, ulong emitter = 0)
    {
        if (kind == NoiseKind.Gunfire) scope.Sound(SoundCue.Rifle, Aim(cell));
        // Events are consumed now on the active floor, never replayed from a save.
        foreach (EnemyState enemy in enemies.Where(e => e.Alive && e.Id != emitter))
        {
            EnemyBrainDefinition tuning = enemy.Definition.Brain;
            float range = kind switch { NoiseKind.Gunfire => tuning.GunfireHearingRange,
                NoiseKind.Alarm => tuning.AlarmHearingRange, _ => tuning.FootstepHearingRange };
            if (Vector3.Distance(EnemyAim(enemy), Aim(cell)) <= range)
                enemy.Brain.Observe(null, cell);
        }
    }

    private bool SeesParty(EnemyState enemy)
    {
        Vector3 delta = Aim(scope.Exploration.Position) - EnemyAim(enemy);
        if (delta.Length() > enemy.Definition.AwarenessRange) return false;
        GridPoint forward = enemy.Motion.Facing.Offset();
        if (delta.LengthSquared() > 0 && Vector3.Dot(Vector3.Normalize(delta), new(forward.X, 0, forward.Y))
            < Math.Cos(enemy.Definition.Brain.SightConeDegrees * Math.PI / 360)) return false;
        RoomDressing dressing = scope.Features().Dressing;
        SpatialEntityCollider[] sightBodies = CombatBodies().Where(body => Combat.ActorsBlockSight
            || body.Entity == scope.PartyId || body.Entity == dressing.BenchId || body.Entity == dressing.CrateId).ToArray();
        SpatialHit sight = scope.Scene.Trace(EnemyAim(enemy), Aim(scope.Exploration.Position), sightBodies, enemy.Id);
        return sight.Present && sight.Kind == SpatialHitKind.Entity && sight.Entity == scope.PartyId;
    }

    private bool Face(EnemyState enemy, GridPoint target)
    {
        int dx = target.X - enemy.Motion.Position.X, dy = target.Y - enemy.Motion.Position.Y;
        if (dx == 0 && dy == 0) return true;
        CardinalDirection facing = Math.Abs(dx) >= Math.Abs(dy) ? dx >= 0 ? CardinalDirection.East : CardinalDirection.West
            : dy >= 0 ? CardinalDirection.South : CardinalDirection.North;
        if (enemy.Motion.Facing == facing) return true;
        enemy.Motion.Act(enemy.Motion.Facing.Rotate(-1) == facing ? ExplorationAction.TurnLeft : ExplorationAction.TurnRight);
        return false;
    }

    private void AdvanceEnemies(double seconds)
    {
        foreach (EnemyState enemy in enemies.Where(e => e.Alive))
        {
            enemy.Motion.Advance(seconds, magic.Speed(new EnemyTarget(enemy.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            enemy.Brain.Advance(seconds);
            enemy.DecisionRemaining = Math.Max(0, enemy.DecisionRemaining - seconds);
            if (Defeated) { enemy.Action.Cancel(); enemy.Motion.Stop(); continue; }
            try { enemy.Action.Advance(seconds * magic.Speed(new EnemyTarget(enemy.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))), action => CommitEnemy(enemy, action)); }
            catch (InvalidOperationException error) { CombatMessage(enemy.Definition.Name + ": " + error.Message); }
        }
        if (Defeated || enemies.Count == 0) return;
        int queries = Combat.PathQueriesPerStep;
        // Rotate the first decision slot so bounded path work cannot starve later actors.
        for (int index = 0; index < enemies.Count; index++)
        {
            EnemyState enemy = enemies[(pathCursor + index) % enemies.Count];
            if (!enemy.Alive || enemy.Motion.Moving || enemy.Action.Busy || enemy.DecisionRemaining > 0) continue;
            enemy.DecisionRemaining = enemy.Definition.DecisionSeconds;
            bool sees = SeesParty(enemy);
            enemy.Brain.Observe(sees ? scope.Exploration.Position : null, null);
            GridPoint? goal = enemy.Brain.Goal(enemy.Motion.Position);
            if (goal is null || goal == enemy.Motion.Position)
            {
                enemy.NavigationStatus = "Holding";
                if (enemy.Brain.Mode == EnemyBrainMode.Search) enemy.Motion.Act(ExplorationAction.TurnRight);
                continue;
            }
            if (!sees && !Face(enemy, goal.Value)) continue;
            bool retreat = false;
            if (sees)
            {
                if (!Face(enemy, scope.Exploration.Position)) continue;
                float distance = Vector3.Distance(EnemyAim(enemy), Aim(scope.Exploration.Position));
                CombatActionKind kind = enemy.Definition.Attack;
                bool ranged = kind == CombatActionKind.Fire && (enemy.Loaded || Ammo(enemy.Owner) > 0);
                if (!ranged) kind = CombatActionKind.Melee;
                retreat = ranged && enemy.Brain.NeedsRetreat(distance) && enemy.Brain.RetreatReady;
                SpatialHit shot = scope.Scene.Trace(EnemyAim(enemy), Aim(scope.Exploration.Position), CombatBodies(), enemy.Id);
                bool clearShot = shot.Present && shot.Kind == SpatialHitKind.Entity && shot.Entity == scope.PartyId;
                if (clearShot && TryEnemySpell(enemy, distance)) { enemy.NavigationStatus = "Casting"; continue; }
                if (!retreat && clearShot && distance <= Combat.Action(kind).Range
                    && (!ranged || distance <= enemy.Definition.Brain.PreferredMaximumRange))
                {
                    BeginEnemyAttack(enemy, kind);
                    enemy.NavigationStatus = "Engaging"; continue;
                }
            }
            if (!enemy.Brain.CanRequestPath) { enemy.NavigationStatus = "Waiting to replan"; continue; }
            if (queries == 0) { enemy.DecisionRemaining = 0; enemy.NavigationStatus = "Waiting for path budget"; continue; }
            GridPoint[] goals = EnemyGoals(enemy, goal.Value, sees, retreat).Take(Math.Min(queries, Combat.PathGoalsPerDecision)).ToArray();
            queries -= goals.Length;
            GridPoint door = scope.ItemWorld.Door;
            bool tooWideForDoor = definitions.Crowd.Footprints[enemy.Definition.Footprint].EdgeClearance > Combat.DoorClearance;
            var narrowLandings = scope.Floor.Connectors.Where(c => c.Clearance < definitions.Crowd.Footprints[enemy.Definition.Footprint].EdgeClearance)
                .Select(c => c.To).ToHashSet();
            IEnumerable<GridPoint> blocked = scope.Floor.Cells.Where(cell => !scope.Movement.CanFit(enemy.Id, cell)
                || tooWideForDoor && cell == door || narrowLandings.Contains(cell));
            GridPoint? next = scope.Scene.NextStep(enemy.Motion.Position, goals, blocked);
            if (next is { } step && enemy.Motion.StepTo(step))
            {
                if (retreat) enemy.Brain.TrySpendRetreatStep();
                enemy.NavigationStatus = retreat ? "Retreating" : "Moving";
            }
            else
            {
                enemy.Brain.ReportPathUnavailable();
                if (retreat && sees)
                {
                    SpatialHit shot = scope.Scene.Trace(EnemyAim(enemy), Aim(scope.Exploration.Position), CombatBodies(), enemy.Id);
                    if (shot.Present && shot.Kind == SpatialHitKind.Entity && shot.Entity == scope.PartyId
                        && Vector3.Distance(EnemyAim(enemy), Aim(scope.Exploration.Position)) <= Combat.Action(CombatActionKind.Fire).Range)
                    {
                        BeginEnemyAttack(enemy, CombatActionKind.Fire);
                        enemy.NavigationStatus = "Holding ground";
                        continue;
                    }
                }
                enemy.NavigationStatus = scope.Movement.Blocked(enemy.Id) is { } waiting
                    ? waiting.Expired ? "Route blocked" : "Waiting for occupied step" : "Route blocked";
            }
        }
        pathCursor = (pathCursor + 1) % enemies.Count;
    }

    private void BeginEnemyAttack(EnemyState enemy, CombatActionKind kind)
    {
        if (kind == CombatActionKind.Fire && !enemy.Loaded) enemy.Action.Start(NewAction(CombatActionKind.Reload));
        else enemy.Action.Start(NewAction(kind, target: scope.PartyId, aim: scope.Exploration.Position));
    }

    private IEnumerable<GridPoint> EnemyGoals(EnemyState enemy, GridPoint known, bool sees, bool retreat)
    {
        GridPoint current = enemy.Motion.Position;
        if (!sees)
            return scope.Movement.CanFit(enemy.Id, known) ? new[] { known }
                : CardinalDirections.Ordered.Select(d => known + d.Offset()).Where(c => scope.Floor.Cells.Contains(c) && scope.Movement.CanFit(enemy.Id, c))
                    .OrderBy(c => c.ManhattanDistance(current));
        if (enemy.Definition.Attack != CombatActionKind.Fire || !enemy.Loaded && Ammo(enemy.Owner) == 0)
            return CardinalDirections.Ordered.Select(d => known + d.Offset()).Where(scope.Floor.Cells.Contains)
                .OrderBy(c => c.ManhattanDistance(current));
        EnemyBrainDefinition tuning = enemy.Definition.Brain;
        float oldDistance = Vector3.Distance(Aim(current), Aim(known));
        // Candidate facts use the currently visible target only; last-known pursuit never queries its live pose.
        return scope.Floor.Cells.Where(c => c != current && scope.Movement.CanFit(enemy.Id, c))
            .Where(c => !retreat || c.ManhattanDistance(current) == 1)
            .Select(c => (Cell: c, Distance: Vector3.Distance(Aim(c), Aim(known))))
            .Where(c => c.Distance <= tuning.PreferredMaximumRange && (!retreat || c.Distance > oldDistance))
            .OrderBy(c => c.Distance < tuning.PreferredMinimumRange ? tuning.PreferredMinimumRange - c.Distance : 0)
            .ThenBy(c => c.Cell.ManhattanDistance(current)).ThenBy(c => c.Cell.Y).ThenBy(c => c.Cell.X)
            .Take(Combat.CandidateCellsPerDecision)
            .Where(c => { SpatialHit hit = scope.Scene.Trace(Aim(c.Cell), Aim(known), CombatBodies(), enemy.Id);
                return hit.Present && hit.Kind == SpatialHitKind.Entity && hit.Entity == scope.PartyId; })
            .Select(c => c.Cell);
    }

}
