using System.Numerics;
using Rifles.Game.Combat;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Items;
using Rifles.Game.Magic;
using Rifles.Game.Party;
using Rifles.Procgen;
using Rifles.Procgen.Generation;
using Rusty.Engine.Interaction;

namespace Rifles.Game.Generation;

/// <summary>Floor-owned generated mechanisms and their live revision.</summary>
internal sealed class GeneratedFeatureState(
    GeneratedFeatureSnapshot snapshot, GameDefinitions definitions, DungeonScene scene,
    ExplorationState exploration, ExplorationItems itemWorld, PatrolActor actor,
    ItemInventory inventory, PartyState party, MagicState magic, ItemArt itemArt,
    Action<string> message, Func<GridPoint, Vector3> aim)
{
    private GeneratedFeaturePresentation Text => definitions.GeneratedFeatures.Presentation;
    private RiflesCombat combat = null!;
    internal GeneratedFeatureSnapshot Snapshot { get; private set; } = snapshot;
    internal void BindCombat(RiflesCombat value) => combat = value;
    private void Update(GeneratedFeatureSnapshot value) => Snapshot = value;

    internal IEnumerable<InteractionCandidate> Candidates()
    {
        foreach (var plate in Snapshot.Plates)
            yield return new InteractionCandidate(new(plate.Id, Snapshot.Revision), definitions.GeneratedFeatures.PlateClue,
                scene.Eye(plate.Cell), definitions.GeneratedFeatures.Reach,
                scene.Visibility(scene.Eye(exploration.Position), scene.Eye(plate.Cell)),
                exploration.Moving ? InteractionAvailability.Unavailable : InteractionAvailability.Available);
        foreach (var hazard in Snapshot.Hazards)
            yield return new InteractionCandidate(new(hazard.Id, Snapshot.Revision),
                hazard.Disabled ? Text.DrainOffLabel : definitions.Hazards.Clue, scene.Eye(hazard.Cell),
                definitions.GeneratedFeatures.Reach, scene.Visibility(scene.Eye(exploration.Position), scene.Eye(hazard.Cell)),
                exploration.Moving ? InteractionAvailability.Unavailable : InteractionAvailability.Available);
        foreach (var gate in Snapshot.Gates)
        {
            string label = gate.Traversal == TraversalKind.Hidden && !gate.Discovered
                ? definitions.GeneratedFeatures.SecretClue
                : gate.Open ? Text.OpenGateLabel : definitions.GeneratedFeatures.GateClue;
            var point = scene.Eye(gate.Approach);
            yield return new InteractionCandidate(new(gate.Id, Snapshot.Revision), label, point,
                definitions.GeneratedFeatures.Reach, scene.Visibility(scene.Eye(exploration.Position), point),
                exploration.Moving ? InteractionAvailability.Unavailable : InteractionAvailability.Available);
        }
    }

    internal string Use(InteractionTarget target)
    {
        var plateTarget = Snapshot.Plates.SingleOrDefault(p => p.Id == target.Id);
        if (plateTarget is not null) return definitions.GeneratedFeatures.PlateClue;
        var hazard = Snapshot.Hazards.SingleOrDefault(h => h.Id == target.Id);
        if (hazard is not null)
        {
            if (target.Revision != Snapshot.Revision || !itemWorld.Reachable(scene.Eye(hazard.Cell), exploration, scene))
                throw new InvalidDataException(Text.DrainChanged);
            Update(Snapshot with { Revision = checked(Snapshot.Revision + 1),
                Hazards = Snapshot.Hazards.Select(h => h.Id == target.Id ? h with { Disabled = true } : h).ToArray() });
            return Text.DrainOffLabel;
        }
        var result = UseGate(Snapshot, target, Text,
            gate => itemWorld.Reachable(scene.Eye(gate.Approach), exploration, scene),
            UseProblem, cell => scene.SetDoor(cell, true));
        Update(result.Snapshot);
        return result.Message;
    }

    internal static (GeneratedFeatureSnapshot Snapshot, string Message) UseGate(
        GeneratedFeatureSnapshot current, InteractionTarget target, GeneratedFeaturePresentation text,
        Func<GeneratedGate, bool> reachable, Func<ulong, string?> problem, Action<GridPoint> openDoor)
    {
        int index = Array.FindIndex(current.Gates, gate => gate.Id == target.Id);
        if (index < 0) throw new InvalidDataException(text.Unavailable);
        GeneratedGate gate = current.Gates[index];
        if (target.Revision != current.Revision || !reachable(gate)) throw new InvalidDataException(text.HandleChanged);
        if (gate.Open) return (current, text.PassageOpen);
        if (gate.Discovered && problem(gate.Id) is { } rejection) return (current, rejection);
        bool discovering = !gate.Discovered;
        if (!discovering) openDoor(gate.Cell);
        var gates = current.Gates.ToArray();
        gates[index] = gate with { Discovered = true, Open = !discovering };
        return (current with { Gates = gates, Revision = checked(current.Revision + 1) },
            discovering ? text.FoundHandle : text.PassageOpened);
    }

    internal string? UseProblem(ulong id)
    {
        if (Snapshot.Hazards.SingleOrDefault(h => h.Id == id) is { } hazard)
            return hazard.Disabled ? Text.AlreadyOff : null;
        var gate = Snapshot.Gates.SingleOrDefault(g => g.Id == id);
        if (gate is null) return Text.Unavailable;
        if (gate.Open) return Text.PassageOpen;
        if (!gate.Discovered) return null;
        if (gate.RequiredItem is { } required)
        {
            var key = Snapshot.Keys.Single(k => k.Item == required);
            if (!party.Members.Any(member => inventory.Items("member:" + member.Definition.Id).Any(item => item.Entity == key.Entity)))
                return string.Format(Text.FindKey, definitions.GeneratedFeatures.GateClue);
        }
        var plate = Snapshot.Plates.SingleOrDefault(p => p.GateId == gate.Id);
        return plate is not null && PlateMass(plate) < plate.RequiredWeight
            ? string.Format(Text.PlateMass, plate.RequiredWeight, definitions.GeneratedFeatures.PlateClue) : null;
    }

    private ulong PlateMass(GeneratedPlate plate) => checked(combat.Drops.Where(d => d.Value == plate.Cell)
        .Aggregate(0UL, (mass, drop) => checked(mass + inventory.Mass(drop.Key)))
        + (exploration.Position == plate.Cell ? definitions.ItemExploration.PartyWeight : 0)
        + (actor.Motion.Position == plate.Cell ? definitions.ItemExploration.ActorWeight : 0));

    internal IEnumerable<Rusty.Engine.AppearanceFact> Facts()
    {
        foreach (var plate in Snapshot.Plates)
            yield return itemArt.PlateAt(plate.Id, plate.Cell, scene, definitions.ItemExploration.PlateHeight);
        foreach (var gate in Snapshot.Gates.Where(g => g.Discovered))
            yield return itemArt.At(gate.Id, scene.Eye(gate.Approach) with { Y = scene.GroundHeight(gate.Approach) }, Text.GateImage, 1);
        foreach (var hazard in Snapshot.Hazards)
            yield return itemArt.At(hazard.Id, scene.Eye(hazard.Cell) with { Y = scene.GroundHeight(hazard.Cell) }, Text.HazardImage, 1);
    }

    internal System.Numerics.Vector3 Point(ulong id)
    {
        var gate = Snapshot.Gates.SingleOrDefault(g => g.Id == id);
        return scene.Eye(gate is not null ? gate.Approach : Snapshot.Hazards.Single(h => h.Id == id).Cell);
    }

    internal void Advance(double seconds)
    {
        if (magic.Has(new PartyTarget(), Rifles.Game.Magic.SpellEffect.Reveal))
        {
            float radius = magic.Radius(new PartyTarget(), Rifles.Game.Magic.SpellEffect.Reveal);
            for (int index = 0; index < Snapshot.Gates.Length; index++)
            {
                var gate = Snapshot.Gates[index];
                if (!gate.Discovered && System.Numerics.Vector3.Distance(aim(exploration.Position), scene.Eye(gate.Approach)) <= radius
                    && scene.Visibility(aim(exploration.Position), scene.Eye(gate.Approach)) == InteractionVisibility.Visible)
                    ReplaceGeneratedGate(index, gate with { Discovered = true });
            }
        }
        Update(Snapshot with { Hazards = Snapshot.Hazards.Select(h =>
            GeneratedHazards.Advance(h, definitions.Hazards, seconds, exploration.Position, damage =>
            {
                foreach (var member in party.Members.Where(m => m.IsLiving)) combat.DamageMember(member, damage);
                message(string.Format(Text.DrainDamage, damage));
            })).ToArray() });
    }

    private void ReplaceGeneratedGate(int index, GeneratedGate gate)
    {
        var gates = Snapshot.Gates.ToArray(); gates[index] = gate;
        Update(Snapshot with { Gates = gates, Revision = checked(Snapshot.Revision + 1) });
    }
}
