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
internal sealed class RiflesCombat
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
    private readonly HashSet<ulong> loadedWeapons;
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
        loadedWeapons = [];
    }

    private CombatDefinition Combat => definitions.Combat;

    internal IReadOnlyList<EnemyState> Enemies => enemies;
    internal IReadOnlyDictionary<string, GridPoint> Drops => drops;
    internal IReadOnlyCollection<ulong> LoadedWeapons => loadedWeapons;
    internal IReadOnlyList<FlightState> Flights => flights;
    internal IReadOnlyDictionary<ulong, RiflesCharacter> Allies => allies;
    internal ulong SelectedTarget => selectedTarget;
    internal IReadOnlyCollection<string> Log => log;
    internal bool Defeated => party.Members.All(m => !m.IsLiving);

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
        foreach (RiflesCharacter member in party.Members) ActionOf(member).Cancel();
    }

    internal bool Threatened => enemies.Any(e => e.Alive && (e.Aware
        || Vector3.Distance(EnemyAim(e), scope.Aim(scope.Exploration.Position)) <= definitions.Magic.ThreatRange && SeesParty(e)));

    /// <summary>Fresh combat for a newly built floor: empty actions/flights, no drops.</summary>
    internal static RiflesCombat CreateFresh(GameDefinitions definitions, CharacterEntities entities, PartyState party,
        MagicState magic, ItemInventory inventory, CombatScope scope, List<EnemyState> enemies, AllySnapshot[] allies,
        ulong[]? travellingLoaded = null)
    {
        RiflesCombat combat = new(definitions, entities, party, magic, inventory, [], scope, enemies);
        foreach (RiflesCharacter member in party.Members)
            entities.AttachComponent(member.Definition.Id, () => new ActionState());
        combat.BuildAllies(allies);
        if (travellingLoaded is not null) combat.loadedWeapons.UnionWith(travellingLoaded);
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
        combat.loadedWeapons.UnionWith(saved.LoadedWeapons);
        combat.selectedTarget = saved.SelectedTarget;
        combat.BuildAllies(saved.Allies);
        return combat;
    }

    internal CombatSnapshot Capture() => new(enemies.Select(e => e.Capture()).ToArray(),
        party.Members.Select(member => new MemberActionSnapshot(member.Definition.Id, ActionOf(member).Capture())).ToArray(),
        loadedWeapons.ToArray(), flights.Select(f => f.Capture()).ToArray(),
        drops.Select(d => new DropSnapshot(d.Key, d.Value)).ToArray(),
        allies.Select(a => new AllySnapshot(a.Key, a.Value.Vitality)).ToArray(), selectedTarget, magic.Capture());

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
            allies.Add(ally.Id, CreateAlly(ally.Id, ally.Vitality));
    }

    private RiflesCharacter CreateAlly(ulong id, long vitality)
    {
        FormationPositionDefinition front = definitions.Party.Positions.OrderBy(position => position.Rank).First();
        MemberDefinition definition = new(id.ToString(), "garrison-ally", "Garrison ally",
            front.Id, Combat.AllyVitality, StartingVitality: vitality);
        entities.Detach(definition.Id);
        (EntityId entity, StatsComponent stats) = entities.AttachStats(
            definition.Id, "rifles:ally", () => RiflesStats.ForMember(definition));
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

    internal CarriedItem? Weapon(string member) => inventory.Items("member:" + member).SingleOrDefault(i => i.Slots.Contains("main-hand"));
    // Rifle ammunition is pooled: reload draws from the shared party
    // inventory, never from a character pack. Enemies keep their own packs.
    private ulong Ammo(string owner) => inventory.Items(owner).SingleOrDefault(i => i.Definition == Combat.AmmunitionItem)?.Quantity ?? 0;
    internal ulong PartyAmmo() => Ammo(ItemInventory.PartyKey);
    private ActionSnapshot NewAction(CombatActionKind kind, ulong weapon = 0, string? token = null, string? source = null,
        ulong target = 0, string? member = null, GridPoint? aim = null)
    {
        ActionDefinition tuning = Combat.Action(kind);
        return new(kind, weapon, token, source, target, member, tuning.Windup, ActionPhase.Windup, tuning.Recovery, aim);
    }

    internal GameOutcome BeginCombat(SessionCommand command, string selectedMember, bool paused)
    {
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
        if (!member.IsLiving) return GameOutcome.Reject("Choose a living character.");
        ActionState state = ActionOf(member);
        if (command.Action == "interrupt")
        {
            if (state.Current?.Phase == ActionPhase.Recovery) return GameOutcome.Reject("Committed actions must finish recovery.");
            state.Cancel(); CombatMessage(member.Definition.Name + " interrupted; no cost committed."); return GameOutcome.Accept();
        }
        scope.CancelRest("Rest interrupted by an action.");
        if (state.Busy) return GameOutcome.Reject("That character is still acting.");
        CarriedItem? weapon = Weapon(selectedMember);
        string owner = "member:" + selectedMember;
        CombatActionKind kind = command.Action switch
        {
            "reload" => CombatActionKind.Reload, "throw" => CombatActionKind.Throw,
            "consume" => CombatActionKind.Consume,
            _ => weapon is not null && definitions.Items.Item(weapon.Definition).Ammunition.Length > 0 ? CombatActionKind.Fire : CombatActionKind.Melee,
        };
        if (kind is CombatActionKind.Reload or CombatActionKind.Fire)
        {
            if (weapon is null || definitions.Items.Item(weapon.Definition).Ammunition.Length == 0) return GameOutcome.Reject("Equip a rifle first.");
            if (kind == CombatActionKind.Fire && !loadedWeapons.Contains(weapon.Entity)) return GameOutcome.Reject("Dry rifle — reload first.");
            if (kind == CombatActionKind.Reload && loadedWeapons.Contains(weapon.Entity)) return GameOutcome.Reject("Rifle already loaded.");
            if (kind == CombatActionKind.Reload && PartyAmmo() == 0) return GameOutcome.Reject("No rifle shot in the party inventory.");
        }
        if (kind == CombatActionKind.Melee && !party.CanUseReach(selectedMember, PartyReach.Melee)) return GameOutcome.Reject("Only the front row can reach with melee.");
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
        if (aim is { } cell && Vector3.Distance(Aim(scope.Exploration.Position), Aim(cell)) > Combat.Action(kind).Range)
            return GameOutcome.Reject("Target is out of range.");
        ActionSnapshot prepared = NewAction(kind, kind is CombatActionKind.Melee or CombatActionKind.Fire or CombatActionKind.Reload ? weapon?.Entity ?? 0 : 0,
            token, source, targetId, targetMember, aim);
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
        if (action.Kind is CombatActionKind.Fire or CombatActionKind.Reload or CombatActionKind.Melee
            && (Weapon(memberId)?.Entity ?? 0) != action.Weapon) throw new InvalidDataException("Equipment changed; action interrupted.");
        if (action.Kind == CombatActionKind.Reload)
        {
            inventory.Consume(new PartyOwner(), Combat.AmmunitionItem, 1); loadedWeapons.Add(action.Weapon);
            scope.Sound(SoundCue.Reload, Aim(scope.Exploration.Position));
            CombatMessage(member.Definition.Name + " loaded one round."); return;
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
        if (action.Kind == CombatActionKind.Melee && !party.CanUseReach(memberId, PartyReach.Melee)) throw new InvalidDataException("Formation changed; melee interrupted.");
        if (action.Kind == CombatActionKind.Fire && !loadedWeapons.Remove(action.Weapon)) throw new InvalidDataException("Dry rifle.");
        if (action.Kind == CombatActionKind.Fire) EmitNoise(scope.Exploration.Position, NoiseKind.Gunfire);
        GridPoint cell = action.AimCell!.Value;
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
        Vector3 start = Aim(scope.Exploration.Position), end = Aim(cell) + new Vector3(action.AimOffsetX, 0, action.AimOffsetY) * scope.Scene.LogicalCellSize;
        if (Vector3.Distance(start, end) > Combat.Action(action.Kind).Range) { CombatMessage("Attack missed — target outside reach."); return; }
        SpatialHit hit = scope.Scene.Trace(start, end, CombatBodies(), scope.PartyId);
        ResolveHit(hit, action.Kind, memberId, scope.PartyId, end - start);
    }

    internal void Advance(double seconds)
    {
        if (seconds <= 0) return;
        AdvanceFlights(seconds);
        foreach (RiflesCharacter member in party.Members)
        {
            ActionState state = ActionOf(member);
            if (!member.IsLiving) { state.Cancel(); continue; }
            if (state.Current is { Phase: ActionPhase.Windup } pending
                && pending.Kind is CombatActionKind.Melee or CombatActionKind.Fire or CombatActionKind.Reload
                && (Weapon(member.Definition.Id)?.Entity ?? 0) != pending.Weapon)
            { state.Cancel(); CombatMessage(member.Definition.Name + " interrupted by equipment change."); }
            try { state.Advance(seconds * magic.Speed(new MemberTarget(member.Definition.Id)), action => CommitMember(member.Definition.Id, action)); }
            catch (InvalidDataException error) { CombatMessage(error.Message); }
            catch (InvalidOperationException error) { CombatMessage("Action interrupted: " + error.Message); }
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

    private void ResolveHit(SpatialHit hit, CombatActionKind kind, string? member, ulong shooter, Vector3 direction)
    {
        if (!hit.Present) { CombatMessage(kind + " missed."); return; }
        if (hit.Kind != SpatialHitKind.Entity) { CombatMessage(kind + " blocked by masonry or a closed gate."); return; }
        scope.Sound(SoundCue.Impact, hit.Entity == scope.PartyId ? Aim(scope.Exploration.Position) : Aim(enemies.FirstOrDefault(e => e.Id == hit.Entity)?.Motion.Position ?? scope.Exploration.Position));
        long damage = Combat.Action(kind).Damage + (member is null ? 0 : Member(member).Power);
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
            // Directional hits meet whoever stands closest to the incoming
            // side; directionless cases fall back to front-rank order.
            RiflesCharacter? target = MemberInLineOfFire(direction)
                ?? party.Members.Where(m => m.IsLiving).OrderBy(m => m.Rank).ThenBy(m => m.Position, StringComparer.Ordinal).FirstOrDefault();
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
                if (rifle is not null) loadedWeapons.Add(rifle.Entity);
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
                        inventory.Destroy(ItemRef.Parse(flight.Owner, consumed.Token)); loadedWeapons.Remove(consumed.Entity);
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
    /// Whoever stands closest to the incoming side of the party cell meets a
    /// directional hit. Formation offsets are authored facing-relative, so the
    /// ray converts to facing-relative axes first: the same layout reads
    /// correctly whether the attack comes from the front, flank, or rear.
    /// Null means degenerate direction — the caller keeps rank order.
    /// </summary>
    private RiflesCharacter? MemberInLineOfFire(Vector3 direction)
    {
        Rifles.Procgen.Generation.GridPoint facing = scope.Exploration.Facing.Offset();
        float forwardX = facing.X, forwardZ = facing.Y, leftX = facing.Y, leftZ = -facing.X;
        string? id = FormationPositionDefinition.FirstEncountered(
            direction.X * forwardX + direction.Z * forwardZ,
            direction.X * leftX + direction.Z * leftZ,
            party.Members.Select(member =>
            {
                FormationPositionDefinition position = party.PositionOf(member.Definition.Id);
                return (member.Definition.Id, member.Position, position.OffsetForward, position.OffsetLeft, member.IsLiving);
            }));
        return id is null ? null : party.Members.Single(member => member.Definition.Id == id);
    }

    internal void RequireItemAccess(string owner)
    {
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

    private bool HasItem(string member, string item) => inventory.Items("member:" + member).Any(i => i.Definition == item && i.Quantity > 0);

    /// <summary>
    /// Planning-time spell eligibility shared by execution and projection.
    /// Translates this owner's own rejection channel into a reason; only
    /// InvalidDataException converts, programming failures still propagate.
    /// </summary>
    internal string? SpellAvailability(string memberId, SpellDefinition spell, string targetMember, ulong target, ulong featureRevision)
    {
        try { ValidateSpell(memberId, spell, targetMember, target, featureRevision, committing: false); return null; }
        catch (InvalidDataException error) { return error.Message; }
    }

    internal void ValidateSpell(string memberId, SpellDefinition spell, string targetMember, ulong target, ulong featureRevision, bool committing)
    {
        RiflesCharacter member = Member(memberId);
        if (!member.IsLiving || !magic.For(memberId).Known.Contains(spell.Id)) throw new InvalidDataException("That character cannot cast this spell.");
        if (member.Resource < SpellCost(memberId, spell)) throw new InvalidDataException("Insufficient resource.");
        if (!committing && ActionOf(member).Busy) throw new InvalidDataException("That character is still acting.");
        if (spell.Target == SpellTarget.Enemy && !committing)
        {
            EnemyState? foe = enemies.SingleOrDefault(e => e.Id == target && Visible(e));
            if (foe is null || Vector3.Distance(Aim(scope.Exploration.Position), EnemyAim(foe)) > spell.Range)
                throw new InvalidDataException("Select a visible enemy within spell range.");
        }
        if (spell.Target == SpellTarget.Ally)
        {
            RiflesCharacter ally = Member(targetMember);
            if (spell.Effect == SpellEffect.Revive)
            {
                if (ally.IsLiving || magic.For(targetMember).Revivals >= definitions.Magic.MaximumRevivals || Threatened)
                    throw new InvalidDataException("Revival needs a fallen ally with a revival remaining and no threats.");
                if (!HasItem(memberId, definitions.Magic.RevivalItem)) throw new InvalidDataException("Caster needs " + definitions.Magic.RevivalItem + " for revival.");
            }
            else if (!ally.IsLiving) throw new InvalidDataException("Choose a living ally.");
            if (spell.Effect == SpellEffect.Heal && ally.Vitality == ally.MaximumVitality) throw new InvalidDataException("Ally needs no healing.");
        }
        if (spell.Target == SpellTarget.Feature)
        {
            bool generated = scope.GeneratedFeatures.Gates.Any(g => g.Id == target) || scope.GeneratedFeatures.Hazards.Any(h => h.Id == target);
            if (generated && scope.FeatureUseProblem(target) is { } problem) throw new InvalidDataException(problem);
            Vector3 point = generated ? scope.FeaturePoint(target) : scope.ItemWorld.LeverPoint(scope.Scene);
            ulong revision = generated ? scope.GeneratedFeatures.Revision : scope.ItemWorld.Revision;
            if (!definitions.Magic.AllowLeverMagic || featureRevision != revision
                || !scope.ItemWorld.Reachable(point, scope.Exploration, scope.Scene) || Vector3.Distance(Aim(scope.Exploration.Position), point) > spell.Range)
                throw new InvalidDataException("No permitted mechanism within reach, or the feature changed.");
        }
    }

    internal GameOutcome BeginSpell(string member, SpellDefinition spell, string targetMember)
    {
        ulong targetId = selectedTarget;
        ulong targetRevision = scope.ItemWorld.Revision;
        if (spell.Target == SpellTarget.Feature)
        {
            var focused = scope.Features().Readout?.Selected;
            targetId = focused is { } selected && (scope.GeneratedFeatures.Gates.Any(g => g.Id == selected.Id)
                || scope.GeneratedFeatures.Hazards.Any(h => h.Id == selected.Id)) ? focused.Value.Id : scope.ItemWorld.LeverId;
            targetRevision = targetId == scope.ItemWorld.LeverId ? scope.ItemWorld.Revision : scope.GeneratedFeatures.Revision;
        }
        if (SpellAvailability(member, spell, targetMember, targetId, targetRevision) is { } reason) return GameOutcome.Reject(reason);
        scope.CancelRest("Rest interrupted by casting.");
        EnemyState? target = spell.Target == SpellTarget.Enemy ? enemies.Single(e => e.Id == selectedTarget) : null;
        ActionOf(Member(member)).Start(new(CombatActionKind.Cast, 0, null, null, spell.Target == SpellTarget.Feature ? targetId : target?.Id ?? 0, targetMember,
            spell.Windup, ActionPhase.Windup, spell.Recovery, target?.Motion.Position,
            target?.Motion.CrowdOffset.X ?? 0, target?.Motion.CrowdOffset.Y ?? 0, spell.Id, SpellCost(member, spell), targetRevision));
        CombatMessage(Member(member).Definition.Name + " prepares " + spell.Name + ".");
        return GameOutcome.Accept();
    }

    private bool TryEnemySpell(EnemyState enemy, float distance)
    {
        if (!definitions.Magic.EnemySpells.TryGetValue(enemy.Definition.Id, out string? id)) return false;
        SpellDefinition spell = definitions.Magic.Spell(id);
        if (enemy.Resource < spell.Cost || distance > spell.Range) return false;
        enemy.Action.Start(new(CombatActionKind.Cast, 0, null, null, scope.PartyId, null,
            spell.Windup, ActionPhase.Windup, spell.Recovery, scope.Exploration.Position, Spell: id, Cost: spell.Cost));
        return true;
    }

    private void CommitSpell(string? memberId, EnemyState? enemy, ActionSnapshot action)
    {
        SpellDefinition spell = definitions.Magic.Spell(action.Spell!);
        if (memberId is not null)
        {
            ValidateSpell(memberId, spell, action.TargetMember!, action.Target, action.FeatureRevision, true);
            if (Member(memberId).Resource < action.Cost) throw new InvalidDataException("Insufficient resource at commit.");
            if (spell.Effect == SpellEffect.Revive) inventory.Consume(new MemberOwner(memberId), definitions.Magic.RevivalItem, 1);
            Member(memberId).SpendResource(action.Cost);
        }
        else
        {
            if (enemy is null || !enemy.Alive || enemy.Resource < action.Cost) return;
            enemy.SpendResource(action.Cost);
        }
        scope.Sound(SoundCue.Spell, enemy is null ? Aim(scope.Exploration.Position) : EnemyAim(enemy));
        if (spell.Target == SpellTarget.Enemy)
        {
            Vector3 source = enemy is null ? Aim(scope.Exploration.Position) : EnemyAim(enemy);
            Vector3 end = Aim(action.AimCell!.Value) + new Vector3(action.AimOffsetX, 0, action.AimOffsetY) * scope.Scene.LogicalCellSize;
            Vector3 offset = end - source;
            if (offset.LengthSquared() == 0) { CombatMessage("Spell dissipated at its origin."); return; }
            Vector3 direction = Vector3.Normalize(offset);
            flights.Add(new FlightState(scope.AllocateIds(), enemy?.Id ?? scope.PartyId, memberId, CombatActionKind.Cast,
                source.X, source.Y, source.Z, direction.X, direction.Y, direction.Z, Math.Min(offset.Length(), spell.Range),
                enemy?.Motion.Position ?? scope.Exploration.Position, null, null, spell.Id));
        }
        else if (spell.Effect == SpellEffect.Lever)
        {
            if (action.Target == scope.ItemWorld.LeverId) scope.ItemWorld.ToggleLever(scope.Exploration, scope.Scene, action.FeatureRevision);
            else CombatMessage(scope.UseFeature(action.Target, action.FeatureRevision));
        }
        else if (spell.Target == SpellTarget.Party) magic.Apply(new PartyTarget(), spell);
        else
        {
            RiflesCharacter target = Member(action.TargetMember!);
            MemberTarget key = new(target.Definition.Id);
            switch (spell.Effect)
            {
                case SpellEffect.Heal: target.Heal(spell.Power); break;
                case SpellEffect.Revive: target.Heal(spell.Power); magic.For(target.Definition.Id).Revivals++; break;
                case SpellEffect.Cleanse: magic.Clear(key, true); break;
                default: magic.Apply(key, spell); break;
            }
        }
        CombatMessage((memberId is null ? enemy!.Definition.Name : Member(memberId).Definition.Name) + " casts " + spell.Name + ".");
    }

    private void ResolveSpellImpact(FlightState flight, SpatialHit hit, Vector3 point)
    {
        SpellDefinition spell = definitions.Magic.Spell(flight.Spell!);
        if (spell.Radius <= 0)
        {
            if (hit.Present && hit.Kind == SpatialHitKind.Entity) ApplySpellHit(hit.Entity, flight.Shooter, spell);
            else CombatMessage(spell.Name + " stopped or missed.");
            return;
        }
        // An explosion starts on the incoming side of its impact surface. Each victim
        // needs its own masonry/furniture visibility ray, independent of other victims.
        Vector3 origin = hit.Present ? new(MathF.BitDecrement(point.X), point.Y, MathF.BitDecrement(point.Z)) : point;
        if (hit.Present)
        {
            float Before(float coordinate, float direction) => direction > 0 ? MathF.BitDecrement(coordinate) : direction < 0 ? MathF.BitIncrement(coordinate) : coordinate;
            origin = new(Before(point.X, flight.DirectionX), point.Y, Before(point.Z, flight.DirectionZ));
        }
        var dressing = scope.Features().Dressing;
        SpatialEntityCollider[] obstacles = CombatBodies().Where(b => b.Entity == dressing.BenchId || b.Entity == dressing.CrateId).ToArray();
        foreach (EnemyState target in enemies.Where(e => e.Alive).ToArray())
            if (Vector3.Distance(origin, EnemyAim(target)) <= spell.Radius && !scope.Scene.Trace(origin, EnemyAim(target), obstacles, 0).Present)
                ApplySpellHit(target.Id, flight.Shooter, spell);
        if (Vector3.Distance(origin, Aim(scope.Exploration.Position)) <= spell.Radius && !scope.Scene.Trace(origin, Aim(scope.Exploration.Position), obstacles, 0).Present)
            ApplySpellHit(scope.PartyId, flight.Shooter, spell);
        CombatMessage(spell.Name + " bursts; walls and closed gates block its spread.");
    }

    internal long Resisted(long power, string definition) => power * (100 - definitions.Magic.Resistances[definition]) / 100;

    private void ApplySpellHit(ulong target, ulong shooter, SpellDefinition spell)
    {
        EnemyState? foe = enemies.SingleOrDefault(e => e.Id == target && e.Alive);
        if (foe is not null)
        {
            if (shooter != scope.PartyId && !Combat.FriendlyFire) return;
            if (spell.Effect == SpellEffect.Damage) DamageEnemy(foe, Resisted(spell.Power, foe.Definition.Id));
            else if (definitions.Magic.Resistances[foe.Definition.Id] < 100) magic.Apply(new EnemyTarget(foe.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)), spell, spell.Duration * (100 - definitions.Magic.Resistances[foe.Definition.Id]) / 100);
            foe.Brain.Observe(null, scope.Exploration.Position);
        }
        else if (target == scope.PartyId && (shooter != scope.PartyId || Combat.FriendlyFire))
        {
            RiflesCharacter? member = party.Members.Where(m => m.IsLiving).OrderBy(m => m.Rank).ThenBy(m => m.Position, StringComparer.Ordinal).FirstOrDefault();
            if (member is null) return;
            scope.CancelRest("Rest interrupted by hostile magic.");
            if (spell.Effect == SpellEffect.Damage) DamageMember(member, Resisted(spell.Power, member.Definition.Archetype));
            else if (definitions.Magic.Resistances[member.Definition.Archetype] < 100) magic.Apply(new MemberTarget(member.Definition.Id), spell, spell.Duration * (100 - definitions.Magic.Resistances[member.Definition.Archetype]) / 100);
        }
        CombatMessage(spell.Name + " struck " + (foe?.Definition.Name ?? "the party") + ".");
    }
}
