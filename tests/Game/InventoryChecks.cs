using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Items;
using Rifles.Game.Party;
using Rifles.Procgen.Generation;

internal static class InventoryChecks
{
    internal static void Run(GameDefinitions definitions)
    {
        ItemInventory inventory = CreateInventory(definitions.Items);

        VerifyGrantTransferAndStaleProposals(inventory);
        VerifyEquipmentViewsAndStats(definitions, inventory);
        VerifyCapacityFailureKeepsEquipment(definitions.Items);
        VerifySaveRestore(definitions.Items, inventory);
        VerifyPartyFormationAndAuthoredResources(definitions, definitions.Characters.GetPreset(definitions.Characters.DefaultPresetId));
        VerifyExplorationCreation(definitions);

        Console.WriteLine("Inventory checks passed: Engine item ledger, equipment, saves, party state, and world anchors.");
    }

    private static ItemInventory CreateInventory(ItemDefinitions definitions)
    {
        ulong ownerId = 1;
        PackOwner[] owners = definitions.StartingItems.Select(item => item.Owner).Distinct(StringComparer.Ordinal)
            .Select(key =>
            {
                PackDefinition capacity = ItemInventory.IsMember(key) ? definitions.Backpack
                    : key == "crate" ? definitions.Container : definitions.Anchor;
                return new PackOwner(ownerId++, key, capacity.Mass, capacity.Space);
            }).ToArray();
        ItemInventory inventory = new(definitions, owners);
        ulong itemId = 100;
        inventory.GrantStarting(() => itemId++);
        return inventory;
    }

    private static void VerifyGrantTransferAndStaleProposals(ItemInventory inventory)
    {
        Require(FungibleQuantity(inventory, "shot") == 14, "Starting fungible quantities are admitted once.");
        CarriedItem[] unique = inventory.Owners.SelectMany(owner => inventory.Items(owner.Key))
            .Where(item => item.Entity != 0).ToArray();
        Require(unique.Select(item => item.Entity).Distinct().Count() == unique.Length, "Engine materializes unique item identities.");

        ulong revision = inventory.Revision;
        string shot = inventory.Find("member:warden", "s:shot").Token;
        inventory.Transfer("member:warden", "member:seeker", shot, 3, revision);
        Require(FungibleQuantity(inventory, "shot") == 14, "Split transfer conserves fungible items.");
        Require(ItemQuantity(inventory, "member:warden", "shot") == 5 && ItemQuantity(inventory, "member:seeker", "shot") == 9,
            "Split transfer leaves the source remainder and merges into the destination stack.");

        string seekerShot = inventory.Find("member:seeker", "s:shot").Token;
        inventory.Transfer("member:seeker", "member:warden", seekerShot, 3, inventory.Revision);
        Require(ItemQuantity(inventory, "member:warden", "shot") == 8 && ItemQuantity(inventory, "member:seeker", "shot") == 6,
            "Merged stack can be transferred back without changing the total.");

        string beforeStaleAttempt = Describe(inventory);
        RequireRejected(() => inventory.Transfer("member:warden", "member:seeker", "s:shot", 1, revision),
            "A proposal from an earlier inventory revision is rejected.");
        Require(Describe(inventory) == beforeStaleAttempt, "A stale proposal does not alter Engine inventory state.");
    }

    private static void VerifyEquipmentViewsAndStats(GameDefinitions definitions, ItemInventory inventory)
    {
        PartyState party = new(definitions.Party.Positions, definitions.Party.MaxPartySize, definitions.Characters.GetPreset(definitions.Characters.DefaultPresetId));
        PartyMemberState warden = Member(party, "warden");
        ApplyEquipment(inventory, warden);
        Require(warden.EquipmentBonuses == new EquipmentStatBonuses(6, 4) && warden.Power == warden.Definition.BasePower + 6
            && warden.Defense == warden.Definition.BaseDefense + 4, "Engine-backed equipment sources contribute to member statistics.");

        string knife = inventory.Find("member:blade", "i:" + inventory.Items("member:blade").Single(item => item.Definition == "knife").Entity).Token;
        inventory.Transfer("member:blade", "member:warden", knife, 1, inventory.Revision);
        inventory.Equip("member:warden", knife, "main-hand", warden.Definition.BasePower, inventory.Revision);

        CarriedItem rifle = inventory.Items("member:warden").Single(item => item.Definition == "rifle");
        CarriedItem equippedKnife = inventory.Items("member:warden").Single(item => item.Definition == "knife");
        Require(rifle.Slots.Length == 0, "Equipping main-hand gear displaces the two-handed rifle from every occupied slot.");
        Require(equippedKnife.Slots.SequenceEqual(["main-hand"]), "The inventory view exposes the equipped item and its actual slot.");
        Require(inventory.View("member:warden").UniqueItems.Count == 3, "Engine inventory view retains every unique item after equipment changes.");

        ApplyEquipment(inventory, warden);
        Require(inventory.Bonuses("member:warden") == (2L, 4L) && warden.EquipmentBonuses == new EquipmentStatBonuses(2, 4)
            && warden.Power == warden.Definition.BasePower + 2 && warden.Defense == warden.Definition.BaseDefense + 4,
            "Equipment bonuses follow current Engine assignments rather than a parallel item ledger.");
    }

    private static void VerifyCapacityFailureKeepsEquipment(ItemDefinitions definitions)
    {
        ItemDefinitions rifleOnly = definitions with
        {
            StartingItems = [new StartingItem("member:source", "rifle", 1, true)],
        };
        ItemInventory inventory = new(rifleOnly,
        [
            new PackOwner(1, "member:source", 100, 100),
            new PackOwner(2, "member:target", 39, 6),
        ]);
        ulong itemId = 10;
        inventory.GrantStarting(() => itemId++);

        string rifle = inventory.Items("member:source").Single().Token;
        string before = Describe(inventory);
        ulong revision = inventory.Revision;
        RequireRejected(() => inventory.Transfer("member:source", "member:target", rifle, 1, revision),
            "A full destination rejects the unique-item transfer.");
        Require(inventory.Revision == revision && Describe(inventory) == before,
            "A rejected transfer neither partially moves nor auto-unequips the source item.");

        RequireRejected(() => inventory.Equip("member:source", rifle, "main-hand", definitions.Item("rifle").MinimumPower,
            revision, "member:target"), "A full destination rejects a cross-owner equip proposal.");
        Require(inventory.Revision == revision && Describe(inventory) == before,
            "A rejected cross-owner equip neither transfers nor unequips its source item.");
    }

    private static void VerifySaveRestore(ItemDefinitions definitions, ItemInventory inventory)
    {
        InventorySnapshot saved = inventory.Capture();
        string expected = Describe(inventory);

        ItemInventory restored = ItemInventory.Restore(definitions, saved);
        Require(Describe(restored) == expected, "Save restoration reconstructs exact item ids, quantities, and equipment assignments.");
        ulong revision = restored.Revision;
        restored.Transfer("member:warden", "member:seeker", "s:shot", 1, revision);
        string beforeStaleRestore = Describe(restored);
        RequireRejected(() => restored.Transfer("member:warden", "member:seeker", "s:shot", 1, revision),
            "A proposal from before a reconstructed-world mutation is rejected.");
        Require(Describe(restored) == beforeStaleRestore, "A stale reconstructed-world proposal leaves inventory untouched.");
    }

    private static void VerifyPartyFormationAndAuthoredResources(GameDefinitions definitions, StarterPartyPresetDefinition preset)
    {
        PartyState party = new(definitions.Party.Positions, definitions.Party.MaxPartySize, preset);
        Require(party.Members.All(member => member.Vitality == member.Definition.InitialVitality
            && member.Resource == member.Definition.InitialResource), "Authored starting injuries and resources initialize party state.");
        Require(party.Members.Any(member => member.Vitality < member.MaximumVitality)
            && party.Members.Any(member => member.Resource < member.MaximumResource), "The selected preset contains authored injury and resource state.");

        PartyMemberState warden = Member(party, "warden");
        PartyMemberState blade = Member(party, "blade");
        PartyMemberState seeker = Member(party, "seeker");
        string seekerPosition = seeker.Position;
        Require(party.SwapFormation("warden", "seeker") && warden.Position == seekerPosition,
            "Living members can change the authored formation.");
        string wardenPosition = warden.Position;
        blade.ApplyDamage(long.MaxValue);
        Require(!party.SwapFormation("warden", "blade") && warden.Position == wardenPosition && !party.CanUseReach("blade", PartyReach.Melee),
            "Dead members cannot change formation or become eligible for actions.");
        Require(!party.EligibleMembers(PartyReach.Melee).Any(member => member.Definition.Id == "blade"),
            "Dead members are excluded from reach eligibility.");

        List<FormationPositionDefinition> openPositions = [.. definitions.Party.Positions,
            new FormationPositionDefinition("reserve", "Reserve", 1)];
        PartyState openParty = new(openPositions, definitions.Party.MaxPartySize, preset.Members);
        Require(openParty.MoveFormation("warden", "reserve") && Member(openParty, "warden").Position == "reserve",
            "Living members can move into an unoccupied formation position.");
        Require(!openParty.MoveFormation("warden", "rear-left") && !openParty.MoveFormation("no-such-member", "reserve")
            && !openParty.MoveFormation("warden", "no-such-position"),
            "Occupied, unknown-member, and unknown-position formation moves are rejected.");
        Member(openParty, "blade").ApplyDamage(long.MaxValue);
        Require(!openParty.MoveFormation("blade", "front-left"),
            "Dead members cannot change formation positions.");

        IReadOnlyList<MemberSnapshot> saved = party.Capture();
        warden.ApplyDamage(long.MaxValue);
        party.Restore(saved);
        Require(party.Capture().SequenceEqual(saved), "Party restore preserves formation, injury, and resource values exactly.");
    }

    private static void ApplyEquipment(ItemInventory inventory, PartyMemberState member)
    {
        (long power, long defense) = inventory.Bonuses("member:" + member.Definition.Id);
        member.SetEquipmentBonuses(power, defense);
    }

    private static void VerifyExplorationCreation(GameDefinitions definitions)
    {
        foreach (ulong seed in new ulong[] { 0, 1, definitions.Generation.Seed, 83 }.Distinct())
        {
            DungeonFloor floor = DungeonFloor.Generate(seed, definitions.Generation, definitions.Rooms);
            ulong id = 1;
            PatrolActor actor = PatrolActor.Create(id++, floor,
                definitions.Exploration with { StepSeconds = definitions.Features.ActorStepSeconds }, definitions.Features);
            RoomDressing dressing = RoomDressing.Create(floor, actor, definitions.Art, () => id++);
            ExplorationItems world = ExplorationItems.Create(definitions.ItemExploration, floor, dressing, actor, () => id++);
            ItemExplorationSnapshot state = world.Capture();
            world.Validate(floor);

            Require(!state.Unlocked && !state.LeverOn && !state.DoorOpen && !state.CrateOpened && state.Revision == 1,
                "New exploration items begin as a closed, locked, unopened puzzle.");
            Require(state.Anchors.Select(anchor => anchor.Id).Append(state.DoorId).Append(state.LeverId).Append(state.PlateId)
                .Distinct().Count() == state.Anchors.Length + 3, "World item identities are unique.");

            GridPoint plate = world.Anchor("plate").Cell;
            PatrolSnapshot patrol = actor.Capture();
            HashSet<GridPoint> reserved = [floor.Entrance, floor.Exit, dressing.Bench, dressing.Crate, dressing.Observer,
                patrol.Start, patrol.End];
            Require(!reserved.Contains(plate), "The weight plate avoids party, dressing, and patrol cells.");
            Require(!reserved.Contains(state.Door) && state.Door != plate, "The gate avoids all authored obstacle cells.");
            Require(!reserved.Contains(state.Lever) && state.Lever != plate && state.Lever != state.Door,
                "The lever avoids the gate, plate, dressing, and patrol cells.");

            foreach (AnchorDefinition anchorDefinition in definitions.ItemExploration.Anchors)
            {
                GridPoint expected = anchorDefinition.Placement switch
                {
                    AnchorPlacement.Bench => dressing.Bench,
                    AnchorPlacement.Crate => dressing.Crate,
                    AnchorPlacement.Plate => plate,
                    _ => floor.Entrance,
                };
                Require(world.Anchor(anchorDefinition.Key).Cell == expected,
                    "Each resolved anchor remains attached to its authored placement.");
            }

            HashSet<GridPoint> cells = floor.Cells.ToHashSet();
            int gateNeighbours = CardinalDirections.Ordered.Count(direction => cells.Contains(state.Door + direction.Offset()));
            bool straightGate = (cells.Contains(state.Door + CardinalDirection.North.Offset()) && cells.Contains(state.Door + CardinalDirection.South.Offset()))
                || (cells.Contains(state.Door + CardinalDirection.East.Offset()) && cells.Contains(state.Door + CardinalDirection.West.Offset()));
            Require(gateNeighbours == 2 && straightGate && state.Door.ManhattanDistance(state.Lever) == 1,
                "The gate is a straight passage cell with a neighbouring lever.");

            RequireRejected(() => new ExplorationItems(definitions.ItemExploration, state with { Revision = 0 }).Validate(floor),
                "A zero-revision item snapshot is rejected.");
            RequireRejected(() => new ExplorationItems(definitions.ItemExploration, state with { Anchors = state.Anchors.Skip(1).ToArray() }).Validate(floor),
                "A snapshot missing an authored anchor is rejected.");
            GridPoint nonAdjacent = floor.Cells.First(cell => cell.ManhattanDistance(state.Door) != 1);
            RequireRejected(() => new ExplorationItems(definitions.ItemExploration, state with { Lever = nonAdjacent }).Validate(floor),
                "A snapshot with a non-adjacent lever is rejected.");
        }
    }

    private static PartyMemberState Member(PartyState party, string id) => party.Members.Single(member => member.Definition.Id == id);

    private static ulong ItemQuantity(ItemInventory inventory, string owner, string definition) => inventory.Items(owner)
        .Where(item => item.Definition == definition).Aggregate(0UL, (total, item) => checked(total + item.Quantity));

    private static ulong FungibleQuantity(ItemInventory inventory, string definition) => inventory.Owners
        .Aggregate(0UL, (total, owner) => checked(total + ItemQuantity(inventory, owner.Key, definition)));

    private static string Describe(ItemInventory inventory) => string.Join("|", inventory.Owners.OrderBy(owner => owner.Key, StringComparer.Ordinal)
        .Select(owner => owner.Key + ":" + string.Join(",", inventory.Items(owner.Key).OrderBy(item => item.Token, StringComparer.Ordinal)
            .Select(item => $"{item.Token}:{item.Definition}:{item.Quantity}:{string.Join('+', item.Slots.OrderBy(slot => slot, StringComparer.Ordinal))}"))));

    private static void RequireRejected(Action action, string message)
    {
        try
        {
            action();
        }
        catch (Exception) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
