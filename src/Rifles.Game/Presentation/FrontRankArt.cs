using System.Numerics;
using Rifles.Game.Combat;
using Rifles.Game.Dungeon;
using Rifles.Game.Party;
using Rusty.Engine;

namespace Rifles.Game.Presentation;

/// <summary>Front-rank presentation reads the party; Engine owns joints and animation time.</summary>
internal sealed class FrontRankArt : IDisposable
{
    private readonly IEngineContext engine;
    private readonly FrontRankDefinition definition;
    private readonly RenderResource bodyResource, weaponResource;
    private readonly Appearance body, weapon;
    private readonly Dictionary<string, Soldier> soldiers = new(StringComparer.Ordinal);

    private sealed class Soldier(ulong bodyId, ulong weaponId)
    {
        internal ulong BodyId { get; } = bodyId;
        internal ulong WeaponId { get; } = weaponId;
        internal AnimationInstance? Animation;
        internal string? Pose;
    }

    internal FrontRankArt(IEngineContext engine, FrontRankDefinition definition)
    {
        this.engine = engine;
        this.definition = definition;
        using var bodySource = engine.Content.OpenReference(new(definition.Body));
        bodyResource = engine.Animation.OpenAnimatedMeshFromContent(new(bodySource));
        RenderResource? admittedWeapon = null;
        Appearance? admittedBody = null, admittedWeaponAppearance = null;
        try
        {
            var clips = engine.Animation.ReadClips(bodyResource).ToArray();
            foreach (string clip in new[] { definition.IdleClip, definition.WalkClip, definition.FireClip })
                if (!clips.Any(candidate => candidate.Id == clip))
                    throw new InvalidDataException($"Front-rank body '{definition.Body}' is missing clip '{clip}'. Available: {string.Join(", ", clips.Select(candidate => candidate.Id))}");
            using var weaponSource = engine.Content.OpenReference(new(definition.Weapon));
            admittedWeapon = engine.Animation.OpenAnimatedMeshFromContent(new(weaponSource));
            admittedBody = engine.Animation.CreateAnimatedMeshAppearance(new(bodyResource));
            admittedWeaponAppearance = engine.Animation.CreateAnimatedMeshAppearance(new(admittedWeapon));
            weaponResource = admittedWeapon;
            body = admittedBody;
            weapon = admittedWeaponAppearance;
        }
        catch
        {
            admittedWeaponAppearance?.Dispose();
            admittedBody?.Dispose();
            admittedWeapon?.Dispose();
            bodyResource.Dispose();
            throw;
        }
    }

    internal IEnumerable<AppearanceFact> Facts(PartyState party, FormationDefinition formation,
        ExplorationState exploration, DungeonScene scene, RiflesCombat combat, Func<ulong> allocate)
    {
        Vector3 ground = scene.Eye(exploration.VisualCell) with { Y = scene.GroundHeight(exploration.VisualCell) };
        var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)exploration.VisualYaw * MathF.PI / 180);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY,
            (float)(exploration.VisualYaw + definition.FacingOffsetDegrees) * MathF.PI / 180);
        foreach (var member in party.Soldiers)
        {
            if (!soldiers.TryGetValue(member.InstanceId, out Soldier? soldier))
                soldiers.Add(member.InstanceId, soldier = new(allocate(), allocate()));
            FormationCellDefinition cell = formation.Cells.Single(cell => cell.Id == member.Position);
            bool visible = definition.Enabled && member.IsLiving && cell.Forward > 0;
            float forward = definition.ForwardOffset + (cell.Forward - 1) * definition.RankSpacing;
            Vector3 offset = new(-cell.Left * definition.LaneSpacing, 0, -forward);
            yield return new(soldier.BodyId, false, 0,
                new(ground + Vector3.Transform(offset, yaw), rotation, new(definition.HeightScale)), body, visible, RenderLayer.Scene);
            yield return WeaponFact(soldier, visible && combat.WeaponCapabilities(member.InstanceId) is { FireDamage: > 0 });
        }
    }

    private AppearanceFact WeaponFact(Soldier soldier, bool visible) => new(soldier.WeaponId, true, soldier.BodyId,
        new(Vector(definition.GripPosition), new(definition.GripRotation[0], definition.GripRotation[1], definition.GripRotation[2], definition.GripRotation[3]),
            Vector(definition.GripScale)), weapon, visible, RenderLayer.Scene);

    // The ordinary scene publication admits bodies and children before attachment.
    internal void Animate(PartyState party, FormationDefinition formation, ExplorationState exploration, RiflesCombat combat)
    {
        foreach (var member in party.Soldiers)
        {
            Soldier soldier = soldiers[member.InstanceId];
            if (soldier.Animation is null)
            {
                engine.Graphics.PublishChanges(new(new[] { WeaponFact(soldier, definition.Enabled && member.IsLiving
                    && formation.Cell(member.Position).Forward > 0 && combat.WeaponCapabilities(member.InstanceId) is { FireDamage: > 0 }) }, ReadOnlyMemory<ulong>.Empty,
                    new MeshJointAttachment[] { new(soldier.WeaponId, definition.Joint) }));
                soldier.Animation = engine.Animation.CreateInstance(new(body, soldier.BodyId));
            }
            ActionSnapshot? action = combat.ActionOf(member).Current;
            bool firing = member.IsLiving && action?.Kind == CombatActionKind.Fire;
            string pose = firing ? definition.FireClip : exploration.Moving ? definition.WalkClip : definition.IdleClip;
            if (pose != soldier.Pose)
            {
                engine.Animation.SetPlayback(new(soldier.Animation, AnimationPlaybackKind.Play, pose,
                    firing ? AnimationLoopMode.Once : AnimationLoopMode.Repeat, 1, 1, true, 0, false, 0));
                soldier.Pose = pose;
            }
            if (firing)
            {
                float normalized = action!.Phase == ActionPhase.Windup ? definition.FirePose :
                    definition.FirePose + (1 - definition.FirePose) * (float)(1 - action.Remaining / action.RecoverySeconds);
                engine.Animation.SetPlayback(new(soldier.Animation, AnimationPlaybackKind.Sample, definition.FireClip,
                    AnimationLoopMode.Once, 1, 1, false, 0, false, normalized));
            }
        }
    }

    private static Vector3 Vector(float[] values) => new(values[0], values[1], values[2]);

    internal void ClearFloor()
    {
        foreach (Soldier soldier in soldiers.Values) soldier.Animation?.Dispose();
        soldiers.Clear();
    }

    public void Dispose()
    {
        ClearFloor();
        weapon.Dispose();
        body.Dispose();
        weaponResource.Dispose();
        bodyResource.Dispose();
    }
}
