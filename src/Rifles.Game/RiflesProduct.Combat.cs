using System.Numerics;
using Rifles.Game.Combat;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Items;
using Rifles.Game.Party;
using Rifles.Game.Presentation;
using Rifles.Procgen.Generation;
using Rusty.Engine;

namespace Rifles.Game;

public sealed partial class RiflesProduct
{
    private EnemyState[] enemies = [];
    private Dictionary<string, ActionState> actions = [];
    private readonly HashSet<ulong> loadedWeapons = [];
    private readonly List<FlightSnapshot> flights = [];
    private readonly Dictionary<string, GridPoint> drops = [];
    private readonly Dictionary<ulong, PartyMemberState> allies = [];
    private readonly Queue<string> combatLog = [];
    private ulong selectedTarget;
    private WorldArt? combatArt;
    private Appearance? boltAppearance;
    private CombatDefinition Combat => definitions.Combat;
    private int pathCursor;
    private bool Defeated => party.Members.All(m => !m.IsLiving);
    private Vector3 Aim(GridPoint cell) => scene!.Eye(cell) with { Y = scene.GroundHeight + Combat.AimHeight };
    private static string EnemyOwner(ulong id) => "combat:enemy:" + id;
    private void CombatMessage(string message)
    {
        feedback = message; combatLog.Enqueue(message);
        while (combatLog.Count > Combat.LogLength) combatLog.Dequeue();
    }
    private void StartCombat()
    {
        ConfigureEnemyClearance(movement!, itemWorld!.Capture().Door);
        loadedWeapons.Clear(); flights.Clear(); drops.Clear(); allies.Clear(); combatLog.Clear(); selectedTarget = 0;
        actions = party.Members.ToDictionary(m => m.Definition.Id, _ => new ActionState());
        foreach (ulong id in new[] { actor!.Id, features!.Capture().Dressing.ObserverId })
            allies.Add(id, new PartyMemberState(new MemberDefinition(id.ToString(), "Garrison ally", FormationSlot.FrontLeft, Combat.AllyVitality)));
        List<EnemyState> created = [];
        foreach (EnemySpawnDefinition spawn in Combat.Encounter)
        {
            EnemyDefinition definition = Combat.Enemy(spawn.Enemy);
            GridPoint cell = floor.Cells.Where(c => movement!.CanFit(c, definition.Footprint, definition.Faction, definition.Share) && c != itemWorld!.Capture().Door && c != floor.Exit)
                .OrderBy(c => Math.Abs(c.ManhattanDistance(floor.Entrance) - spawn.Distance))
                .ThenBy(c => c.Y).ThenBy(c => c.X).First();
            ulong id = AllocateId(); string owner = EnemyOwner(id);
            inventory!.RegisterOwner(new PackOwner(AllocateId(), owner, Combat.DropCapacity.Mass, Combat.DropCapacity.Space));
            foreach (StartingItem loot in definition.Loot) inventory.Grant(owner, loot.Definition, loot.Quantity, AllocateId);
            ExplorationState motion = new(cell, definitions.Exploration with { StepSeconds = definition.StepSeconds });
            EnemyState enemy = new(new EnemySnapshot(id, definition.Id, motion.Capture(), definition.Vitality, null, 0, false, false, owner, new EnemyBrain(definition.Brain, cell, PatrolRoute(cell, spawn)).Capture(), spawn.Id), definition, floor, definitions.Exploration);
            enemy.Motion.Bind(movement!, id, definition.Footprint, definition.Faction, definition.Share); created.Add(enemy);
        }
        enemies = created.ToArray();
        CombatMessage("Rifles start empty. Load with T; select a visible foe and attack with Space.");
    }
    private SpatialEntityCollider[] CombatBodies()
    {
        List<SpatialEntityCollider> bodies = [];
        void Add(ulong id, GridPoint cell, Vector2 offset = default, float width = 0, float depth = 0)
        {
            Vector3 center = Aim(cell) + new Vector3(offset.X, 0, offset.Y) * scene!.LogicalCellSize;
            width = width > 0 ? width : Combat.BodyWidth; depth = depth > 0 ? depth : Combat.BodyWidth;
            Vector3 min = new(center.X - width / 2, scene!.GroundHeight, center.Z - depth / 2);
            bodies.Add(new(id, min, min + new Vector3(width, Combat.BodyHeight, depth), 0, 0, true, false, false));
        }
        if (!Defeated) Add(partyId, exploration.Position);
        foreach (EnemyState enemy in enemies.Where(e => e.Alive))
        {
            var slot = movement!.Placement(enemy.Id);
            Add(enemy.Id, enemy.Motion.Position, enemy.Motion.CrowdOffset, slot.Width * scene!.LogicalCellSize, slot.Depth * scene.LogicalCellSize);
        }
        if (allies.GetValueOrDefault(actor!.Id)?.IsLiving == true) Add(actor.Id, actor.Motion.Position);
        RoomDressing dressing = features!.Capture().Dressing;
        if (allies.GetValueOrDefault(dressing.ObserverId)?.IsLiving == true) Add(dressing.ObserverId, dressing.Observer);
        Add(dressing.BenchId, dressing.Bench); Add(dressing.CrateId, dressing.Crate);
        return bodies.ToArray();
    }
    private bool Visible(EnemyState enemy)
    {
        if (!enemy.Alive) return false;
        Vector3 direction = EnemyAim(enemy) - Aim(exploration.Position);
        var facing = exploration.Facing.Offset();
        if (direction.Length() > Combat.Action(CombatActionKind.Fire).Range
            || Vector3.Dot(Vector3.Normalize(direction), new Vector3(facing.X, 0, facing.Y)) < Math.Cos(Combat.TargetAngle * Math.PI / 180)) return false;
        SpatialHit hit = scene!.Trace(Aim(exploration.Position), EnemyAim(enemy), CombatBodies(), partyId);
        return hit.Present && hit.Kind == SpatialHitKind.Entity && hit.Entity == enemy.Id;
    }
    private CarriedItem? Weapon(string member) => inventory!.Items("member:" + member).SingleOrDefault(i => i.Slots.Contains("main-hand"));
    private ulong Ammo(string owner) => inventory!.Items(owner).SingleOrDefault(i => i.Definition == Combat.AmmunitionItem)?.Quantity ?? 0;
    private ActionSnapshot NewAction(CombatActionKind kind, ulong weapon = 0, string? token = null, string? source = null,
        ulong target = 0, string? member = null, GridPoint? aim = null)
    {
        ActionDefinition tuning = Combat.Action(kind);
        return new(kind, weapon, token, source, target, member, tuning.Windup, ActionPhase.Windup, tuning.Recovery, aim);
    }
    private void BeginCombat(SessionCommand command)
    {
        if (command.Action == "target")
        {
            EnemyState target = enemies.SingleOrDefault(e => e.Id == command.Target && Visible(e)) ?? throw new InvalidDataException("Target is not visible.");
            selectedTarget = target.Id; CombatMessage("Target: " + target.Definition.Name); return;
        }
        if (paused || Defeated) throw new InvalidDataException("Resume with a living party before acting.");
        PartyMemberState member = Member(selectedMember);
        if (!member.IsLiving) throw new InvalidDataException("Choose a living character.");
        ActionState state = actions[selectedMember];
        if (command.Action == "interrupt")
        {
            if (state.Current?.Phase == ActionPhase.Recovery) throw new InvalidDataException("Committed actions must finish recovery.");
            state.Cancel(); CombatMessage(member.Definition.Name + " interrupted; no cost committed."); return;
        }
        if (state.Busy) throw new InvalidDataException("That character is still acting.");
        CarriedItem? weapon = Weapon(selectedMember);
        string owner = "member:" + selectedMember;
        CombatActionKind kind = command.Action switch
        {
            "reload" => CombatActionKind.Reload, "bolt" => CombatActionKind.Bolt, "throw" => CombatActionKind.Throw,
            "consume" => CombatActionKind.Consume,
            _ => weapon is not null && definitions.Items.Item(weapon.Definition).Ammunition.Length > 0 ? CombatActionKind.Fire : CombatActionKind.Melee,
        };
        if (kind is CombatActionKind.Reload or CombatActionKind.Fire)
        {
            if (weapon is null || definitions.Items.Item(weapon.Definition).Ammunition.Length == 0) throw new InvalidDataException("Equip a rifle first.");
            if (kind == CombatActionKind.Fire && !loadedWeapons.Contains(weapon.Entity)) throw new InvalidDataException("Dry rifle — reload first.");
            if (kind == CombatActionKind.Reload && loadedWeapons.Contains(weapon.Entity)) throw new InvalidDataException("Rifle already loaded.");
            if (kind == CombatActionKind.Reload && Ammo(owner) == 0) throw new InvalidDataException("No rifle shot in this character's pack.");
        }
        if (kind == CombatActionKind.Melee && !party.CanUseReach(selectedMember, PartyReach.Melee)) throw new InvalidDataException("Only the front row can reach with melee.");
        if (kind == CombatActionKind.Bolt && member.Resource < Combat.Action(kind).ResourceCost) throw new InvalidDataException("Not enough resource for a bolt.");
        string? token = null, source = null, targetMember = null;
        GridPoint? aim = null; ulong targetId = 0;
        if (kind is CombatActionKind.Throw or CombatActionKind.Consume)
        {
            if (command.InventoryRevision != inventory!.Revision.ToString()) throw new InvalidDataException("Inventory changed; select the item again.");
            source = command.Source ?? owner; RequireItemAccess(source);
            token = command.Item ?? throw new InvalidDataException("Select an item.");
            _ = inventory.Find(source, token);
            if (kind == CombatActionKind.Consume)
            {
                targetMember = command.Member ?? selectedMember;
                ValidateRemedy(source, token, targetMember);
            }
            else if (command.Destination == "plate") aim = itemWorld!.Anchor("plate").Cell;
        }
        if (kind is CombatActionKind.Fire or CombatActionKind.Melee or CombatActionKind.Bolt or CombatActionKind.Throw && aim is null)
        {
            targetId = command.Target ?? selectedTarget;
            EnemyState target = enemies.SingleOrDefault(e => e.Id == targetId && Visible(e)) ?? throw new InvalidDataException("Select a visible enemy.");
            aim = target.Motion.Position;
        }
        if (aim is { } cell && Vector3.Distance(Aim(exploration.Position), Aim(cell)) > Combat.Action(kind).Range)
            throw new InvalidDataException("Target is out of range.");
        ActionSnapshot prepared = NewAction(kind, kind is CombatActionKind.Melee or CombatActionKind.Fire or CombatActionKind.Reload ? weapon?.Entity ?? 0 : 0,
            token, source, targetId, targetMember, aim);
        EnemyState? aimedEnemy = enemies.SingleOrDefault(e => e.Id == targetId);
        if (aimedEnemy is not null) prepared = prepared with { AimOffsetX = aimedEnemy.Motion.CrowdOffset.X, AimOffsetY = aimedEnemy.Motion.CrowdOffset.Y };
        state.Start(prepared);
        CombatMessage(member.Definition.Name + ": " + kind + " windup");
    }
    private void ValidateRemedy(string source, string token, string targetId)
    {
        PartyMemberState target = Member(targetId);
        GearDefinition use = definitions.Items.Item(inventory!.Find(source, token).Definition);
        if (!target.IsLiving || use.Use is not (ItemUse.Vitality or ItemUse.Resource)) throw new InvalidDataException("Choose a living character and a remedy.");
        if (use.Use == ItemUse.Vitality && target.Vitality == target.MaximumVitality || use.Use == ItemUse.Resource && target.Resource == target.MaximumResource)
            throw new InvalidDataException("That character needs no restoration.");
    }
    private void CommitMember(string memberId, ActionSnapshot action)
    {
        PartyMemberState member = Member(memberId);
        if (!member.IsLiving) return;
        string owner = "member:" + memberId;
        if (action.Kind is CombatActionKind.Fire or CombatActionKind.Reload or CombatActionKind.Melee
            && (Weapon(memberId)?.Entity ?? 0) != action.Weapon) throw new InvalidDataException("Equipment changed; action interrupted.");
        if (action.Kind == CombatActionKind.Reload)
        {
            inventory!.Consume(owner, Combat.AmmunitionItem, 1); loadedWeapons.Add(action.Weapon);
            CombatMessage(member.Definition.Name + " loaded one round."); return;
        }
        if (action.Kind == CombatActionKind.Consume)
        {
            RequireItemAccess(action.SourceOwner!); ValidateRemedy(action.SourceOwner!, action.ItemToken!, action.TargetMember!);
            GearDefinition use = definitions.Items.Item(inventory!.Find(action.SourceOwner!, action.ItemToken!).Definition);
            inventory.PrepareUse(action.SourceOwner!, action.ItemToken!, inventory.Revision).Publish();
            PartyMemberState target = Member(action.TargetMember!);
            long restored = use.Use == ItemUse.Vitality ? target.Heal(use.Effect) : target.RecoverResource(use.Effect);
            CombatMessage(target.Definition.Name + " restored " + restored + " " + use.Use); return;
        }
        if (action.Kind == CombatActionKind.Melee && !party.CanUseReach(memberId, PartyReach.Melee)) throw new InvalidDataException("Formation changed; melee interrupted.");
        if (action.Kind == CombatActionKind.Fire && !loadedWeapons.Remove(action.Weapon)) throw new InvalidDataException("Dry rifle.");
        if (action.Kind == CombatActionKind.Fire) EmitNoise(exploration.Position, NoiseKind.Gunfire);
        if (action.Kind == CombatActionKind.Bolt)
        {
            long cost = Combat.Action(action.Kind).ResourceCost;
            if (member.Resource < cost) throw new InvalidDataException("Insufficient resource.");
            member.SpendResource(cost);
        }
        GridPoint cell = action.AimCell!.Value;
        if (action.Kind is CombatActionKind.Throw or CombatActionKind.Bolt)
        {
            string? flightOwner = null;
            if (action.Kind == CombatActionKind.Throw)
            {
                RequireItemAccess(action.SourceOwner!);
                _ = inventory!.Find(action.SourceOwner!, action.ItemToken!);
                ulong pack = AllocateId(); flightOwner = "combat:flight:" + pack;
                inventory!.RegisterOwner(new(pack, flightOwner, Combat.DropCapacity.Mass, Combat.DropCapacity.Space));
                inventory.Transfer(action.SourceOwner!, flightOwner, action.ItemToken!, 1, inventory.Revision); ApplyEquipment();
            }
            Launch(partyId, memberId, action.Kind, exploration.Position, cell, flightOwner, action.Target == 0 ? "plate" : null, new(action.AimOffsetX, action.AimOffsetY));
            CombatMessage(member.Definition.Name + " released " + action.Kind); return;
        }
        Vector3 start = Aim(exploration.Position), end = Aim(cell) + new Vector3(action.AimOffsetX, 0, action.AimOffsetY) * scene!.LogicalCellSize;
        if (Vector3.Distance(start, end) > Combat.Action(action.Kind).Range) { CombatMessage("Attack missed — target outside reach."); return; }
        SpatialHit hit = scene!.Trace(start, end, CombatBodies(), partyId);
        ResolveHit(hit, action.Kind, memberId, partyId);
    }
    private void AdvanceCombat(double seconds)
    {
        if (seconds <= 0) return;
        AdvanceFlights(seconds);
        foreach ((string id, ActionState state) in actions)
        {
            PartyMemberState member = Member(id);
            if (!member.IsLiving) { state.Cancel(); continue; }
            if (state.Current is { Phase: ActionPhase.Windup } pending
                && pending.Kind is CombatActionKind.Melee or CombatActionKind.Fire or CombatActionKind.Reload
                && (Weapon(id)?.Entity ?? 0) != pending.Weapon)
            { state.Cancel(); CombatMessage(member.Definition.Name + " interrupted by equipment change."); }
            try { state.Advance(seconds, action => CommitMember(id, action)); }
            catch (InvalidDataException error) { CombatMessage(error.Message); }
            catch (InvalidOperationException error) { CombatMessage("Action interrupted: " + error.Message); }
        }
        AdvanceEnemies(seconds);
        if (Defeated)
        {
            exploration.Stop(); movement!.Remove(partyId); exploration.Detach();
            foreach (ActionState action in actions.Values) action.Cancel();
        }
    }
    private void CommitEnemy(EnemyState enemy, ActionSnapshot action)
    {
        if (!enemy.Alive || Defeated) return;
        if (action.Kind == CombatActionKind.Reload)
        {
            inventory!.Consume(enemy.Owner, Combat.AmmunitionItem, 1); enemy.Loaded = true;
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
        ResolveHit(scene!.Trace(start, end, CombatBodies(), enemy.Id), action.Kind, null, enemy.Id);
    }
    private void ResolveHit(SpatialHit hit, CombatActionKind kind, string? member, ulong shooter)
    {
        if (!hit.Present) { CombatMessage(kind + " missed."); return; }
        if (hit.Kind != SpatialHitKind.Entity) { CombatMessage(kind + " blocked by masonry or a closed gate."); return; }
        long damage = Combat.Action(kind).Damage;
        EnemyState? enemy = enemies.SingleOrDefault(e => e.Id == hit.Entity && e.Alive);
        if (enemy is not null)
        {
            if (shooter != partyId && !Combat.FriendlyFire) { CombatMessage("Shot stopped by a friendly body."); return; }
            long applied = enemy.Damage(Math.Max(Combat.MinimumDamage, damage - enemy.Definition.Defense));
            enemy.Brain.Observe(null, exploration.Position); CombatMessage(enemy.Definition.Name + " took " + applied + " damage.");
            if (!enemy.Alive)
            {
                enemy.Action.Cancel(); enemy.Motion.Stop(); movement!.Remove(enemy.Id); enemy.Motion.Detach();
                drops[enemy.Owner] = enemy.Motion.Position;
                // An enemy's loaded round stays with its unique rifle on death.
                if (enemy.Loaded)
                {
                    CarriedItem? rifle = inventory!.Items(enemy.Owner).FirstOrDefault(i => definitions.Items.Item(i.Definition).Ammunition.Length > 0 && i.Entity != 0);
                    if (rifle is not null) loadedWeapons.Add(rifle.Entity);
                    enemy.Loaded = false;
                }
                CombatMessage(enemy.Definition.Name + " fell. Belongings can be recovered.");
            }
            return;
        }
        if (hit.Entity == partyId)
        {
            if (shooter == partyId && !Combat.FriendlyFire) return;
            PartyMemberState? target = party.Members.Where(m => m.IsLiving).OrderBy(m => m.Slot).FirstOrDefault();
            if (target is null) return;
            long applied = target.ApplyDamage(Math.Max(Combat.MinimumDamage, damage - target.Defense));
            CombatMessage(target.Definition.Name + " took " + applied + " damage" + (target.IsLiving ? "." : " and died. Pack retained."));
            if (!target.IsLiving) actions[target.Definition.Id].Cancel();
            return;
        }
        if (allies.TryGetValue(hit.Entity, out PartyMemberState? ally) && Combat.FriendlyFire)
        {
            ally.ApplyDamage(damage);
            if (!ally.IsLiving) { if (hit.Entity == actor!.Id) { actor.Motion.Stop(); actor.Motion.Detach(); } movement!.Remove(hit.Entity); }
            CombatMessage("Garrison ally " + (ally.IsLiving ? "wounded." : "fell."));
        }
        else CombatMessage("Attack stopped by a world body; friendly damage disabled.");
    }
    private void Launch(ulong shooter, string? member, CombatActionKind kind, GridPoint source, GridPoint target, string? owner, string? destination, Vector2 targetOffset = default)
    {
        Vector3 position = Aim(source), offset = Aim(target) + new Vector3(targetOffset.X, 0, targetOffset.Y) * scene!.LogicalCellSize - position;
        if (offset.LengthSquared() == 0) offset = new Vector3(exploration.Facing.Offset().X, 0, exploration.Facing.Offset().Y);
        Vector3 direction = Vector3.Normalize(offset);
        float distance = Math.Min(offset.Length(), Combat.Action(kind).Range);
        flights.Add(new(AllocateId(), shooter, member, kind, position.X, position.Y, position.Z, direction.X, direction.Y, direction.Z,
            distance, source, owner, destination));
    }
    private void AdvanceFlights(double seconds)
    {
        foreach (FlightSnapshot flight in flights.ToArray())
        {
            Vector3 start = new(flight.X, flight.Y, flight.Z);
            float travel = Math.Min(flight.Remaining, (float)(Combat.Action(flight.Kind).Speed * seconds));
            Vector3 end = start + new Vector3(flight.DirectionX, flight.DirectionY, flight.DirectionZ) * travel;
            SpatialHit hit = scene!.Trace(start, end, CombatBodies(), flight.Shooter);
            GridPoint landed = new((int)MathF.Floor(end.X / scene.LogicalCellSize), (int)MathF.Floor(end.Z / scene.LogicalCellSize));
            if (hit.Present)
            {
                if (hit.Kind == SpatialHitKind.Entity) landed = new((int)MathF.Floor(hit.Point.X / scene.LogicalCellSize), (int)MathF.Floor(hit.Point.Z / scene.LogicalCellSize));
                else
                {
                    // Move one representable float toward the incoming segment, outside the wall boundary.
                    float Before(float value, float direction) => direction > 0 ? MathF.BitDecrement(value) : direction < 0 ? MathF.BitIncrement(value) : value;
                    landed = new((int)MathF.Floor(Before(hit.Point.X, flight.DirectionX) / scene.LogicalCellSize),
                        (int)MathF.Floor(Before(hit.Point.Z, flight.DirectionZ) / scene.LogicalCellSize));
                }
                ResolveHit(hit, flight.Kind, flight.Member, flight.Shooter);
            }
            if (!floor.Cells.Contains(landed) || landed == itemWorld!.Capture().Door && !itemWorld.Capture().DoorOpen) landed = flight.LastCell;
            flights.Remove(flight);
            if (hit.Present || travel >= flight.Remaining)
            {
                if (flight.Owner is not null)
                {
                    drops[flight.Owner] = landed;
                    if (!Combat.RecoverThrownItems)
                    {
                        CarriedItem consumed = inventory!.Items(flight.Owner).Single();
                        inventory.Destroy(flight.Owner, consumed.Token); loadedWeapons.Remove(consumed.Entity);
                        CombatMessage("Thrown item consumed on impact."); continue;
                    }
                    if (landed == itemWorld!.Anchor("plate").Cell)
                    {
                        CarriedItem item = inventory!.Items(flight.Owner).Single();
                        try { inventory.Transfer(flight.Owner, "plate", item.Token, item.Quantity, inventory.Revision); }
                        catch (InvalidDataException) { /* Occupied plate: keep the recoverable pile at this cell. */ }
                    }
                    CombatMessage("Thrown item landed; recover it from nearby ground inventory.");
                }
            }
            else flights.Add(flight with { X = end.X, Y = end.Y, Z = end.Z, Remaining = flight.Remaining - travel, LastCell = landed });
        }
    }
    private bool DropReachable(string owner) => drops.TryGetValue(owner, out GridPoint cell)
        && itemWorld!.Reachable(Aim(cell), exploration, scene!);
    private void RequireItemAccess(string owner)
    {
        if (!owner.StartsWith("combat:", StringComparison.Ordinal)) { itemWorld!.RequireAccess(owner, exploration, scene!); return; }
        if (!DropReachable(owner)) throw new InvalidDataException("Those belongings are not within reach.");
    }
}
