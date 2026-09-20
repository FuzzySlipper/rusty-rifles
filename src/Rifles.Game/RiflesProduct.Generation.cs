using Rifles.Game.Generation;
using Rifles.Game.Items;
using Rifles.Procgen;
using Rusty.Engine.Interaction;

using Rifles.Game.Magic;

namespace Rifles.Game;

public sealed partial class RiflesProduct
{





    private IEnumerable<InteractionCandidate> GeneratedCandidates()
    {
        foreach (var plate in active.GeneratedFeatures.Plates)
            yield return new InteractionCandidate(new(plate.Id, active.GeneratedFeatures.Revision), definitions.GeneratedFeatures.PlateClue,
                active.Scene.Eye(plate.Cell), definitions.GeneratedFeatures.Reach,
                active.Scene.Visibility(active.Scene.Eye(active.Exploration.Position), active.Scene.Eye(plate.Cell)),
                active.Exploration.Moving ? InteractionAvailability.Unavailable : InteractionAvailability.Available);
        foreach (var hazard in active.GeneratedFeatures.Hazards)
            yield return new InteractionCandidate(new(hazard.Id, active.GeneratedFeatures.Revision),
                hazard.Disabled ? "Drain shut off" : definitions.Hazards.Clue, active.Scene.Eye(hazard.Cell),
                definitions.GeneratedFeatures.Reach, active.Scene.Visibility(active.Scene.Eye(active.Exploration.Position), active.Scene.Eye(hazard.Cell)),
                active.Exploration.Moving ? InteractionAvailability.Unavailable : InteractionAvailability.Available);
        foreach (var gate in active.GeneratedFeatures.Gates)
        {
            string label = gate.Traversal == TraversalKind.Hidden && !gate.Discovered
                ? definitions.GeneratedFeatures.SecretClue
                : gate.Open ? "Garrison passage — open" : definitions.GeneratedFeatures.GateClue;
            var point = active.Scene.Eye(gate.Approach);
            yield return new InteractionCandidate(new(gate.Id, active.GeneratedFeatures.Revision), label, point,
                definitions.GeneratedFeatures.Reach, active.Scene.Visibility(active.Scene.Eye(active.Exploration.Position), point),
                active.Exploration.Moving ? InteractionAvailability.Unavailable : InteractionAvailability.Available);
        }
    }

    private string UseGeneratedFeature(InteractionTarget target)
    {
        var plateTarget = active.GeneratedFeatures.Plates.SingleOrDefault(p => p.Id == target.Id);
        if (plateTarget is not null) return definitions.GeneratedFeatures.PlateClue;
        var hazard = active.GeneratedFeatures.Hazards.SingleOrDefault(h => h.Id == target.Id);
        if (hazard is not null)
        {
            if (target.Revision != active.GeneratedFeatures.Revision || !active.ItemWorld.Reachable(active.Scene.Eye(hazard.Cell), active.Exploration, active.Scene))
                throw new InvalidDataException("Drain shutoff changed or is out of reach.");
            active.UpdateGeneratedFeatures(active.GeneratedFeatures with { Revision = checked(active.GeneratedFeatures.Revision + 1),
                Hazards = active.GeneratedFeatures.Hazards.Select(h => h.Id == target.Id ? h with { Disabled = true } : h).ToArray() });
            return "Drain shut off.";
        }
        int index = Array.FindIndex(active.GeneratedFeatures.Gates, g => g.Id == target.Id);
        var gate = active.GeneratedFeatures.Gates[index];
        if (target.Revision != active.GeneratedFeatures.Revision || !active.ItemWorld.Reachable(active.Scene.Eye(gate.Approach), active.Exploration, active.Scene))
            throw new InvalidDataException("The passage handle changed or is out of reach.");
        if (gate.Open) return "The passage is open.";
        if (!gate.Discovered)
        {
            ReplaceGeneratedGate(index, gate with { Discovered = true });
            return "You found the concealed handle. Use it to open the panel.";
        }
        if (GeneratedUseProblem(gate.Id) is { } problem) return problem;
        active.Scene.SetDoor(gate.Cell, true);
        ReplaceGeneratedGate(index, gate with { Open = true });
        return "Garrison passage opened. The key is retained.";
    }

    private string? GeneratedUseProblem(ulong id)
    {
        if (active.GeneratedFeatures.Hazards.SingleOrDefault(h => h.Id == id) is { } hazard)
            return hazard.Disabled ? "The drain is already shut off." : null;
        var gate = active.GeneratedFeatures.Gates.SingleOrDefault(g => g.Id == id);
        if (gate is null) return "That generated mechanism is unavailable.";
        if (gate.Open) return "The passage is already open.";
        if (!gate.Discovered) return null;
        if (gate.RequiredItem is { } required)
        {
            var key = active.GeneratedFeatures.Keys.Single(k => k.Item == required);
            if (!party.Members.Any(member => active.Inventory.Items("member:" + member.Definition.Id).Any(item => item.Entity == key.Entity)))
                return "Find the matching stamped key. " + definitions.GeneratedFeatures.GateClue;
        }
        var plate = active.GeneratedFeatures.Plates.SingleOrDefault(p => p.GateId == gate.Id);
        return plate is not null && PlateMass(plate) < plate.RequiredWeight
            ? "The counterweight plate needs " + plate.RequiredWeight + " mass. " + definitions.GeneratedFeatures.PlateClue : null;
    }

    private ulong PlateMass(GeneratedPlate plate) => checked(active.Combat.Drops.Where(d => d.Value == plate.Cell)
        .Aggregate(0UL, (mass, drop) => checked(mass + active.Inventory.Mass(drop.Key)))
        + (active.Exploration.Position == plate.Cell ? definitions.ItemExploration.PartyWeight : 0)
        + (active.Actor.Motion.Position == plate.Cell ? definitions.ItemExploration.ActorWeight : 0));

    private IEnumerable<Rusty.Engine.AppearanceFact> GeneratedFeatureFacts()
    {
        foreach (var plate in active.GeneratedFeatures.Plates)
            yield return itemArt!.PlateAt(plate.Id, plate.Cell, active.Scene, definitions.ItemExploration.PlateHeight);
        foreach (var gate in active.GeneratedFeatures.Gates.Where(g => g.Discovered))
            yield return itemArt!.At(gate.Id, active.Scene.Eye(gate.Approach) with { Y = active.Scene.GroundHeight(gate.Approach) }, "lever", 1);
        foreach (var hazard in active.GeneratedFeatures.Hazards)
            yield return itemArt!.At(hazard.Id, active.Scene.Eye(hazard.Cell) with { Y = active.Scene.GroundHeight(hazard.Cell) }, "lever", 1);
    }

    private System.Numerics.Vector3 GeneratedFeaturePoint(ulong id)
    {
        var gate = active.GeneratedFeatures.Gates.SingleOrDefault(g => g.Id == id);
        return active.Scene.Eye(gate is not null ? gate.Approach : active.GeneratedFeatures.Hazards.Single(h => h.Id == id).Cell);
    }

    private void AdvanceGeneratedHazards(double seconds)
    {
        if (active.Magic.Has(new PartyTarget(), Rifles.Game.Magic.SpellEffect.Reveal))
        {
            float radius = active.Magic.Radius(new PartyTarget(), Rifles.Game.Magic.SpellEffect.Reveal);
            for (int index = 0; index < active.GeneratedFeatures.Gates.Length; index++)
            {
                var gate = active.GeneratedFeatures.Gates[index];
                if (!gate.Discovered && System.Numerics.Vector3.Distance(Aim(active.Exploration.Position), active.Scene.Eye(gate.Approach)) <= radius
                    && active.Scene.Visibility(Aim(active.Exploration.Position), active.Scene.Eye(gate.Approach)) == InteractionVisibility.Visible)
                    ReplaceGeneratedGate(index, gate with { Discovered = true });
            }
        }
        active.UpdateGeneratedFeatures(active.GeneratedFeatures with { Hazards = active.GeneratedFeatures.Hazards.Select(h =>
            GeneratedHazards.Advance(h, definitions.Hazards, seconds, active.Exploration.Position, damage =>
            {
                foreach (var member in party.Members.Where(m => m.IsLiving)) active.Combat.DamageMember(member, damage);
                CombatMessage("Scalding drain: the party takes " + damage + " damage.");
            })).ToArray() });
    }

    private void ReplaceGeneratedGate(int index, GeneratedGate gate)
    {
        var gates = active.GeneratedFeatures.Gates.ToArray(); gates[index] = gate;
        active.UpdateGeneratedFeatures(active.GeneratedFeatures with { Gates = gates, Revision = checked(active.GeneratedFeatures.Revision + 1) });
    }
}
