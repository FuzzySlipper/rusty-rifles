using Rifles.Game.Characters;
using Rifles.Game.Combat;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Generation;
using Rifles.Game.Items;
using Rifles.Procgen;
using Rifles.Procgen.Expeditions;

namespace Rifles.Game.Expedition;

/// <summary>Shared data admission for active and frozen floor facts.</summary>
internal static class FloorFacts
{
    internal static void Validate(DungeonFloor floor, GeneratedFeatureSnapshot generated,
        EncounterPlacementResult placement, EnemySnapshot[] enemies, ResolvedExpedition intent,
        GameDefinitions definitions)
    {
        floor.Validate();
        GameDefinitions.Require(floor.Architecture is not null, "saved architecture artifact");
        placement.Validate(floor, definitions.Combat, definitions.Crowd);
        GameDefinitions.Require(placement.Accepted && enemies.Select(e => e.Spawn).ToHashSet()
            .SetEquals(placement.Instances.Select(e => e.SpawnId)), "saved accepted encounter roster");
        GeneratedFeatures.Validate(generated, floor);
        GameDefinitions.Require(generated.Hazards.All(h => h.Phase < definitions.Hazards.PeriodSeconds), "saved hazard phase");
        Diagnostic[] intentDiagnostics = ExpeditionGenerator.Validate(intent);
        GameDefinitions.Require(!intentDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Fatal),
            "Save expedition intent: " + string.Join(", ", intentDiagnostics.Select(d => d.Code)));
        ResolvedFloorIntent? floorIntent = intent.Floors.SingleOrDefault(f => f.Id == floor.IntentFloorId);
        GameDefinitions.Require(floorIntent is not null && floor.Seed == floorIntent.Candidate.Seed
            && floor.IntentGraphIdentity == CanonicalIdentity.Hash(floorIntent.Candidate), "Save floor intent identity");
        GameDefinitions.Require(floor.Rooms.Select(r => r.NodeId).ToHashSet(StringComparer.Ordinal)
            .SetEquals(floorIntent!.Candidate.Graph.Nodes.Select(n => n.Id)), "Save room graph coverage");
        var hazardNodes = floorIntent.Candidate.Graph.Nodes.Where(n => n.Kind == NodeKind.Hazard).Select(n => n.Id).ToHashSet();
        GameDefinitions.Require(generated.Hazards.Length == hazardNodes.Count
            && generated.Hazards.Select(h => h.NodeId).ToHashSet().SetEquals(hazardNodes)
            && generated.Hazards.All(h => floor.Rooms.Any(r => r.NodeId == h.NodeId && r.Cells.Contains(h.Cell))),
            "saved hazard graph coverage");
    }

    internal static void ValidateAnchors(GeneratedFeatureSnapshot generated, DropSnapshot[] drops,
        ItemInventory inventory, GameDefinitions definitions, IReadOnlyDictionary<ulong, string>? expeditionItems)
    {
        foreach (var plate in generated.Plates)
            GameDefinitions.Require(drops.Any(d => d.Owner == plate.Owner && d.Cell == plate.Cell)
                && inventory.Owner(plate.Owner).Id > 0, "saved counterweight anchor");
        foreach (var key in generated.Keys)
        {
            GameDefinitions.Require(drops.Any(d => d.Owner == key.Owner && d.Cell == key.Cell), "saved key source anchor");
            var retained = inventory.Owners.SelectMany(o => inventory.Items(o.Key)).Where(i => i.Entity == key.Entity).ToArray();
            GameDefinitions.Require(expeditionItems is not null
                ? expeditionItems.GetValueOrDefault(key.Entity) == definitions.GeneratedFeatures.KeyItem
                : retained.Length == 1 && retained[0].Definition == definitions.GeneratedFeatures.KeyItem,
                "saved generated key identity");
        }
    }
    internal static void ValidatePackCapacities(ItemInventory inventory, ExplorationItems itemWorld,
        GameDefinitions definitions)
    {
        foreach (PackOwner owner in inventory.Owners)
        {
            InventoryOwner identity = InventoryOwner.Parse(owner.Key);
            PackDefinition capacity = identity switch
            {
                CombatOwner => definitions.Combat.DropCapacity,
                MemberOwner => definitions.Items.Backpack,
                PartyOwner => definitions.Items.Party,
                AnchorOwner anchor => anchor.Anchor == "crate" ? definitions.Items.Container : definitions.Items.Anchor,
                _ => throw new InvalidDataException("Unknown inventory owner."),
            };
            GameDefinitions.Require(owner.MassCapacity == capacity.Mass && owner.SpaceCapacity == capacity.Space,
                "saved pack capacity");
            if (identity is AnchorOwner anchorOwner)
                GameDefinitions.Require(owner.Id == itemWorld.Anchor(anchorOwner.Anchor).Id, "saved anchor owner");
        }
    }

}
