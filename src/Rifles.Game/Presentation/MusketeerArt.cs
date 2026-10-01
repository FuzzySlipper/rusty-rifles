using System.Numerics;
using Rifles.Game.Combat;
using Rifles.Game.Dungeon;
using Rusty.Engine;

namespace Rifles.Game.Presentation;

/// <summary>Engine-owned skeletal meshes and playback, bound to concrete Rifles combat states.</summary>
internal sealed class MusketeerArt : IDisposable
{
    private readonly IEngineContext engine;
    private readonly MusketeerDefinition definition;
    private readonly RenderResource bodyResource, weaponResource;
    private readonly Appearance body, weapon;
    private readonly Dictionary<ulong, Soldier> soldiers = [];
    private sealed class Soldier(ulong weaponId)
    {
        internal ulong WeaponId { get; } = weaponId;
        internal AnimationInstance? Animation;
        internal string? Pose;
        internal bool WasAlive;
    }

    internal MusketeerArt(IEngineContext engine, MusketeerDefinition definition)
    {
        this.engine = engine; this.definition = definition;
        using var bodySource = engine.Content.OpenReference(new(definition.Body));
        bodyResource = engine.Animation.OpenAnimatedMeshFromContent(new(bodySource));
        RenderResource? admittedWeapon = null;
        Appearance? admittedBody = null, admittedWeaponAppearance = null;
        try
        {
            var clips = engine.Animation.ReadClips(bodyResource).ToArray();
            foreach (string clip in new[] { definition.IdleClip, definition.WalkClip, definition.FireClip, definition.DefeatClip })
                if (!clips.Any(c => c.Id == clip)) throw new InvalidDataException($"Musketeer body '{definition.Body}' is missing clip '{clip}'. Available: {string.Join(", ", clips.Select(c => c.Id))}");
            using var weaponSource = engine.Content.OpenReference(new(definition.Weapon));
            admittedWeapon = engine.Animation.OpenAnimatedMeshFromContent(new(weaponSource));
            admittedBody = engine.Animation.CreateAnimatedMeshAppearance(new(bodyResource));
            admittedWeaponAppearance = engine.Animation.CreateAnimatedMeshAppearance(new(admittedWeapon));
            weaponResource = admittedWeapon; body = admittedBody; weapon = admittedWeaponAppearance;
        }
        catch
        {
            admittedWeaponAppearance?.Dispose(); admittedBody?.Dispose(); admittedWeapon?.Dispose(); bodyResource.Dispose();
            throw;
        }
    }

    internal bool Supports(EnemyState enemy) => enemy.Definition.Id == definition.Enemy;
    internal IEnumerable<AppearanceFact> Facts(EnemyState enemy, DungeonScene scene, Func<ulong> allocate)
    {
        if (!soldiers.TryGetValue(enemy.Id, out Soldier? soldier)) soldiers.Add(enemy.Id, soldier = new(allocate()));
        Vector3 position = scene.Eye(enemy.Motion.VisualCell + enemy.Motion.VisualCrowdOffset) with { Y = scene.GroundHeight(enemy.Motion.VisualCell) };
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(enemy.Motion.VisualYaw + definition.FacingOffsetDegrees) * MathF.PI / 180);
        yield return new(enemy.Id, false, 0, new(position, rotation, new(definition.HeightScale)), body, true, RenderLayer.Scene);
        yield return WeaponFact(enemy, soldier);
    }

    private AppearanceFact WeaponFact(EnemyState enemy, Soldier soldier) => new(soldier.WeaponId, true, enemy.Id,
        new(Vector(definition.GripPosition), new(definition.GripRotation[0], definition.GripRotation[1], definition.GripRotation[2], definition.GripRotation[3]), Vector(definition.GripScale)),
        weapon, true, RenderLayer.Scene);

    // Call after the ordinary floor snapshot has admitted the body and child.
    internal void Animate(IEnumerable<EnemyState> enemies)
    {
        foreach (EnemyState enemy in enemies.Where(Supports))
        {
            Soldier soldier = soldiers[enemy.Id];
            if (soldier.Animation is null)
            {
                engine.Graphics.PublishChanges(new(new[] { WeaponFact(enemy, soldier) }, ReadOnlyMemory<ulong>.Empty,
                    new MeshJointAttachment[] { new(soldier.WeaponId, definition.Joint) }));
                soldier.Animation = engine.Animation.CreateInstance(new(body, enemy.Id));
            }
            bool firing = enemy.Action.Current?.Kind == CombatActionKind.Fire;
            string pose = !enemy.Alive ? definition.DefeatClip : firing ? definition.FireClip : enemy.Motion.Moving ? definition.WalkClip : definition.IdleClip;
            if (pose != soldier.Pose)
            {
                bool sampleCorpse = !enemy.Alive && !soldier.WasAlive;
                engine.Animation.SetPlayback(new(soldier.Animation, sampleCorpse ? AnimationPlaybackKind.Sample : AnimationPlaybackKind.Play,
                    pose, !enemy.Alive || firing ? AnimationLoopMode.Once : AnimationLoopMode.Repeat,
                    1, 1, true, 0, false, sampleCorpse ? 1 : 0));
                soldier.Pose = pose;
            }
            if (firing)
            {
                ActionSnapshot action = enemy.Action.Current!;
                float normalized = action.Phase == ActionPhase.Windup ? definition.FirePose :
                    definition.FirePose + (1 - definition.FirePose) * (float)(1 - action.Remaining / action.RecoverySeconds);
                engine.Animation.SetPlayback(new(soldier.Animation, AnimationPlaybackKind.Sample, definition.FireClip,
                    AnimationLoopMode.Once, 1, 1, false, 0, false, normalized));
            }
            soldier.WasAlive = enemy.Alive;
        }
    }

    internal void Fire(EnemyState enemy, DungeonScene scene)
    {
        if (!Supports(enemy)) return;
        var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)enemy.Motion.VisualYaw * MathF.PI / 180);
        Vector3 ground = scene.Eye(enemy.Motion.VisualCell + enemy.Motion.VisualCrowdOffset) with { Y = scene.GroundHeight(enemy.Motion.VisualCell) };
        Vector3 muzzle = ground + Vector3.Transform(Vector(definition.MuzzleOffset), yaw);
        Emit(enemy.Id, "rifles.musket.flash", muzzle, definition.Flash);
        Emit(enemy.Id, "rifles.musket.smoke", muzzle, definition.Smoke);
    }
    private void Emit(ulong id, string signal, Vector3 point, MusketBurstDefinition burst)
    {
        engine.Presentation.EmitParticles(new()
        {
            SignalId = signal, Anchor = new() { Kind = PresentationAnchorKind.World, Position = point },
            Visual = PresentationParticleVisual.Cube, BurstCount = burst.Count, MaxParticles = burst.Count,
            LifetimeMinSeconds = burst.LifetimeMin, LifetimeMaxSeconds = burst.LifetimeMax,
            VelocityMin = Vector(burst.VelocityMin), VelocityMax = Vector(burst.VelocityMax), Acceleration = Vector(burst.Acceleration),
            SizeCurve = new PresentationParticleScalarKey[] { new(0, burst.StartSize), new(1, burst.EndSize) },
            ColorCurve = new PresentationParticleColorKey[] { new(0, Color(burst.StartColor)), new(1, Color(burst.EndColor)) },
            Seed = id, Visible = true,
        });
    }
    private static Vector3 Vector(float[] v) => new(v[0], v[1], v[2]);
    private static Color Color(float[] c) => new(c[0], c[1], c[2], c[3]);
    internal void ClearFloor()
    {
        foreach (Soldier soldier in soldiers.Values) soldier.Animation?.Dispose();
        soldiers.Clear();
    }
    public void Dispose() { ClearFloor(); weapon.Dispose(); body.Dispose(); weaponResource.Dispose(); bodyResource.Dispose(); }
}
