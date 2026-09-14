using Rifles.Game.Generation;
using Rifles.Game.Items;
using Rifles.Procgen;
using Rusty.Engine.Interaction;

namespace Rifles.Game;

public sealed partial class RiflesProduct
{
    private GeneratedFeatureSnapshot generatedFeatures = new(1, [], [], [], [], []);

    private void StartGeneratedFeatures()
    {
        // Retire closed volumes from the previous run before resolving new object identities.
        foreach (var old in generatedFeatures.Gates) scene!.SetDoor(old.Cell, true);
        var gates = GeneratedFeatures.Resolve(floor, AllocateId);
        List<GeneratedKey> keys = [];
        foreach (var grant in GeneratedFeatures.RequiredKeys(floor))
        {
            ulong packId = AllocateId();
            string owner = "combat:flight:" + packId;
            inventory!.RegisterOwner(new PackOwner(packId, owner, Combat.DropCapacity.Mass, Combat.DropCapacity.Space));
            ulong entity = AllocateId();
            inventory.Grant(owner, definitions.GeneratedFeatures.KeyItem, 1, () => entity);
            drops.Add(owner, grant.Cell);
            keys.Add(new(grant.Item, entity, owner, grant.Cell));
        }
        List<GeneratedPlate> plates = [];
        foreach (var gate in gates.Where(g => g.Traversal == TraversalKind.Locked))
        {
            var route = floor.Routes.Single(r => r.Id == gate.RouteId);
            var key = keys.Single(k => k.Item == gate.RequiredItem);
            ulong packId = AllocateId(); string owner = "combat:flight:" + packId;
            ulong id = AllocateId();
            inventory!.RegisterOwner(new PackOwner(packId, owner, Combat.DropCapacity.Mass, Combat.DropCapacity.Space, "Counterweight plate"));
            drops.Add(owner, route.Cells[0]);
            inventory.Grant(key.Owner, definitions.GeneratedFeatures.WeightItem, 1, AllocateId);
            plates.Add(new(id, gate.Id, owner, route.Cells[0], key.Cell, definitions.GeneratedFeatures.PlateWeight));
        }
        var hazardNodes = expedition.Floors.Single(f => f.Id == floor.IntentFloorId).Candidate.Graph.Nodes
            .Where(n => n.Kind == NodeKind.Hazard).Select(n => n.Id).ToHashSet();
        var hazards = floor.Rooms.Where(r => hazardNodes.Contains(r.NodeId)).Select(r =>
            new GeneratedHazard(AllocateId(), r.NodeId, r.Cells.OrderBy(c => c.ManhattanDistance(r.Cells[r.Cells.Length / 2])).First(),
                definitions.Hazards.ActiveSeconds, false, false)).ToArray();
        generatedFeatures = new(1, gates, keys.ToArray(), [], hazards, plates.ToArray());
        var progression = FloorProgression.Inspect(floor, gates, generatedFeatures.Plates);
        if (!progression.Accepted) throw new InvalidDataException("Generated progression rejected: " + string.Join(", ", progression.Diagnostics));
        foreach (var gate in gates) scene!.SetDoor(gate.Cell, false);
    }

    private void StartGeneratedSupplies()
    {
        var supplies = RouteSupplies.Resolve(floor, definitions.RouteSupplies,
            encounterPlacement!.Instances.Sum(e => Combat.Enemy(e.EnemyId).Vitality));
        foreach (var supply in supplies)
        {
            ulong packId = AllocateId(); string owner = "combat:flight:" + packId;
            inventory!.RegisterOwner(new PackOwner(packId, owner, Combat.DropCapacity.Mass, Combat.DropCapacity.Space));
            inventory.Grant(owner, supply.Item, supply.Quantity, AllocateId);
            drops.Add(owner, supply.Cell);
        }
        generatedFeatures = generatedFeatures with { Supplies = supplies };
    }

    private IEnumerable<InteractionCandidate> GeneratedCandidates()
    {
        foreach (var plate in generatedFeatures.Plates)
            yield return new InteractionCandidate(new(plate.Id, generatedFeatures.Revision), definitions.GeneratedFeatures.PlateClue,
                scene!.Eye(plate.Cell), definitions.GeneratedFeatures.Reach,
                scene.Visibility(scene.Eye(exploration.Position), scene.Eye(plate.Cell)),
                exploration.Moving ? InteractionAvailability.Unavailable : InteractionAvailability.Available);
        foreach (var hazard in generatedFeatures.Hazards)
            yield return new InteractionCandidate(new(hazard.Id, generatedFeatures.Revision),
                hazard.Disabled ? "Drain shut off" : definitions.Hazards.Clue, scene!.Eye(hazard.Cell),
                definitions.GeneratedFeatures.Reach, scene.Visibility(scene.Eye(exploration.Position), scene.Eye(hazard.Cell)),
                exploration.Moving ? InteractionAvailability.Unavailable : InteractionAvailability.Available);
        foreach (var gate in generatedFeatures.Gates)
        {
            string label = gate.Traversal == TraversalKind.Hidden && !gate.Discovered
                ? definitions.GeneratedFeatures.SecretClue
                : gate.Open ? "Garrison passage — open" : definitions.GeneratedFeatures.GateClue;
            var point = scene!.Eye(gate.Approach);
            yield return new InteractionCandidate(new(gate.Id, generatedFeatures.Revision), label, point,
                definitions.GeneratedFeatures.Reach, scene.Visibility(scene.Eye(exploration.Position), point),
                exploration.Moving ? InteractionAvailability.Unavailable : InteractionAvailability.Available);
        }
    }

    private string UseGeneratedFeature(InteractionTarget target)
    {
        var plateTarget = generatedFeatures.Plates.SingleOrDefault(p => p.Id == target.Id);
        if (plateTarget is not null) return definitions.GeneratedFeatures.PlateClue;
        var hazard = generatedFeatures.Hazards.SingleOrDefault(h => h.Id == target.Id);
        if (hazard is not null)
        {
            if (target.Revision != generatedFeatures.Revision || !itemWorld!.Reachable(scene!.Eye(hazard.Cell), exploration, scene))
                throw new InvalidDataException("Drain shutoff changed or is out of reach.");
            generatedFeatures = generatedFeatures with { Revision = checked(generatedFeatures.Revision + 1),
                Hazards = generatedFeatures.Hazards.Select(h => h.Id == target.Id ? h with { Disabled = true } : h).ToArray() };
            return "Drain shut off.";
        }
        int index = Array.FindIndex(generatedFeatures.Gates, g => g.Id == target.Id);
        var gate = generatedFeatures.Gates[index];
        if (target.Revision != generatedFeatures.Revision || !itemWorld!.Reachable(scene!.Eye(gate.Approach), exploration, scene))
            throw new InvalidDataException("The passage handle changed or is out of reach.");
        if (gate.Open) return "The passage is open.";
        if (!gate.Discovered)
        {
            ReplaceGeneratedGate(index, gate with { Discovered = true });
            return "You found the concealed handle. Use it to open the panel.";
        }
        if (GeneratedUseProblem(gate.Id) is { } problem) return problem;
        scene!.SetDoor(gate.Cell, true);
        ReplaceGeneratedGate(index, gate with { Open = true });
        return "Garrison passage opened. The key is retained.";
    }

    private string? GeneratedUseProblem(ulong id)
    {
        if (generatedFeatures.Hazards.SingleOrDefault(h => h.Id == id) is { } hazard)
            return hazard.Disabled ? "The drain is already shut off." : null;
        var gate = generatedFeatures.Gates.SingleOrDefault(g => g.Id == id);
        if (gate is null) return "That generated mechanism is unavailable.";
        if (gate.Open) return "The passage is already open.";
        if (!gate.Discovered) return null;
        if (gate.RequiredItem is { } required)
        {
            var key = generatedFeatures.Keys.Single(k => k.Item == required);
            if (!party.Members.Any(member => inventory!.Items("member:" + member.Definition.Id).Any(item => item.Entity == key.Entity)))
                return "Find the matching stamped key. " + definitions.GeneratedFeatures.GateClue;
        }
        var plate = generatedFeatures.Plates.SingleOrDefault(p => p.GateId == gate.Id);
        return plate is not null && PlateMass(plate) < plate.RequiredWeight
            ? "The counterweight plate needs " + plate.RequiredWeight + " mass. " + definitions.GeneratedFeatures.PlateClue : null;
    }

    private ulong PlateMass(GeneratedPlate plate) => checked(drops.Where(d => d.Value == plate.Cell)
        .Aggregate(0UL, (mass, drop) => checked(mass + inventory!.Mass(drop.Key)))
        + (exploration.Position == plate.Cell ? definitions.ItemExploration.PartyWeight : 0)
        + (actor!.Motion.Position == plate.Cell ? definitions.ItemExploration.ActorWeight : 0));

    private IEnumerable<Rusty.Engine.AppearanceFact> GeneratedFeatureFacts()
    {
        foreach (var plate in generatedFeatures.Plates)
            yield return itemArt!.PlateAt(plate.Id, plate.Cell, scene!, definitions.ItemExploration.PlateHeight);
        foreach (var gate in generatedFeatures.Gates.Where(g => g.Discovered))
            yield return itemArt!.At(gate.Id, scene!.Eye(gate.Approach) with { Y = scene.GroundHeight(gate.Approach) }, "lever", 1);
        foreach (var hazard in generatedFeatures.Hazards)
            yield return itemArt!.At(hazard.Id, scene!.Eye(hazard.Cell) with { Y = scene.GroundHeight(hazard.Cell) }, "lever", 1);
    }

    private System.Numerics.Vector3 GeneratedFeaturePoint(ulong id)
    {
        var gate = generatedFeatures.Gates.SingleOrDefault(g => g.Id == id);
        return scene!.Eye(gate is not null ? gate.Approach : generatedFeatures.Hazards.Single(h => h.Id == id).Cell);
    }

    private void AdvanceGeneratedHazards(double seconds)
    {
        if (magic!.Has("party", Rifles.Game.Magic.SpellEffect.Reveal))
        {
            float radius = magic.Radius("party", Rifles.Game.Magic.SpellEffect.Reveal);
            for (int index = 0; index < generatedFeatures.Gates.Length; index++)
            {
                var gate = generatedFeatures.Gates[index];
                if (!gate.Discovered && System.Numerics.Vector3.Distance(Aim(exploration.Position), scene!.Eye(gate.Approach)) <= radius
                    && scene.Visibility(Aim(exploration.Position), scene.Eye(gate.Approach)) == InteractionVisibility.Visible)
                    ReplaceGeneratedGate(index, gate with { Discovered = true });
            }
        }
        generatedFeatures = generatedFeatures with { Hazards = generatedFeatures.Hazards.Select(h =>
            GeneratedHazards.Advance(h, definitions.Hazards, seconds, exploration.Position, damage =>
            {
                foreach (var member in party.Members.Where(m => m.IsLiving)) DamageMember(member, damage);
                CombatMessage("Scalding drain: the party takes " + damage + " damage.");
            })).ToArray() };
    }

    private void ReplaceGeneratedGate(int index, GeneratedGate gate)
    {
        var gates = generatedFeatures.Gates.ToArray(); gates[index] = gate;
        generatedFeatures = generatedFeatures with { Gates = gates, Revision = checked(generatedFeatures.Revision + 1) };
    }
}
