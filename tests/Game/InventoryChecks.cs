using Rifles.Game.Characters;
using Rifles.Game.Content;
using Rifles.Game.Dungeon;
using Rifles.Game.Items;
using Rifles.Game.Party;
using Rifles.Procgen.Generation;

internal static class InventoryChecks
{
    internal static void Run(GameDefinitions definitions)
    {
        ItemInventory inventory = CreateInventory(definitions);

        VerifyGrantTransferAndStaleProposals(definitions.Items, inventory);
        VerifyPartySlots(inventory);
        VerifyPartyFull(definitions.Items);
        VerifyEquipmentViewsAndStats(definitions, inventory);
        VerifyCapacityFailureKeepsEquipment(definitions.Items);
        VerifySaveRestore(definitions.Items, inventory);
        VerifyOldSavesRejected(definitions);
        VerifyPartyFormationAndAuthoredResources(definitions);
        VerifyExplorationCreation(definitions);

        Console.WriteLine("Inventory checks passed: Engine item ledger, equipment, saves, party state, and world anchors.");
    }

    private static ItemInventory CreateInventory(GameDefinitions definitions)
    {
        ulong ownerId = 1;
        HashSet<string> registered = definitions.Items.StartingItems.Select(item => item.Owner).ToHashSet(StringComparer.Ordinal);
        // Product floors register a pack for every roster member, not just
        // grant owners: the bind step requires complete member coverage.
        foreach (string member in definitions.Characters.ResolvePreset(definitions.Characters.DefaultPresetId).Select(m => m.Id))
            registered.Add("member:" + member);
        PackOwner[] owners = registered
            .Select(key =>
            {
                PackDefinition capacity = ItemInventory.IsMember(key) ? definitions.Items.Backpack
                    : key == "party" ? definitions.Items.Party
                    : key == "crate" ? definitions.Items.Container : definitions.Items.Anchor;
                return new PackOwner(ownerId++, key, capacity.Mass, capacity.Space);
            }).ToArray();
        ItemInventory inventory = new(definitions.Items, owners);
        ulong itemId = 100;
        inventory.GrantStarting(() => itemId++);
        return inventory;
    }

    private static void VerifyGrantTransferAndStaleProposals(ItemDefinitions definitions, ItemInventory inventory)
    {
        Require(FungibleQuantity(inventory, "shot") == 14, "Starting fungible quantities are admitted once.");
        CarriedItem[] unique = inventory.Owners.SelectMany(owner => inventory.Items(owner.Key))
            .Where(item => item.Entity != 0).ToArray();
        Require(unique.Select(item => item.Entity).Distinct().Count() == unique.Length, "Engine materializes unique item identities.");
        Require(inventory.Items("party").All(item => inventory.SlotOf(item.Token) >= 0), "Loose starting kits arrive slotted in the party inventory.");

        ulong revision = inventory.Revision;
        inventory.RegisterOwner(new PackOwner(900, "test-anchor", definitions.Anchor.Mass, definitions.Anchor.Space));
        revision = inventory.Revision;
        inventory.Transfer(ItemRef.Parse("party", "s:shot"), InventoryOwner.Parse("test-anchor"), 3, revision);
        Require(FungibleQuantity(inventory, "shot") == 14, "Split transfer conserves fungible items.");
        Require(ItemQuantity(inventory, "party", "shot") == 11 && ItemQuantity(inventory, "test-anchor", "shot") == 3,
            "Split transfer leaves the source remainder and starts the destination stack.");
        RequireRejected(() => inventory.Transfer(ItemRef.Parse("party", "s:tonic"), InventoryOwner.Parse("test-anchor"), 1, inventory.Revision),
            "Anchors hold one item kind: mixing stacks is rejected.");

        inventory.Transfer(ItemRef.Parse("test-anchor", "s:shot"), InventoryOwner.Parse("party"), 3, inventory.Revision);
        Require(ItemQuantity(inventory, "party", "shot") == 14 && !inventory.Items("test-anchor").Any(),
            "Merged stack can be transferred back without changing the total.");

        RequireRejected(() => inventory.Transfer(ItemRef.Parse("party", "s:shot"), InventoryOwner.Parse("member:warden"), 1, inventory.Revision),
            "Characters carry only equipped gear: loose transfers into member packs are rejected.");

        string beforeStaleAttempt = Describe(inventory);
        RequireRejected(() => inventory.Transfer(ItemRef.Parse("party", "s:shot"), InventoryOwner.Parse("test-anchor"), 1, revision),
            "A proposal from an earlier inventory revision is rejected.");
        Require(Describe(inventory) == beforeStaleAttempt, "A stale proposal does not alter Engine inventory state.");
    }

    private static void VerifyPartySlots(ItemInventory inventory)
    {
        const string shot = "s:shot";
        int home = inventory.SlotOf(shot);
        Require(home >= 0, "Party stacks occupy grid slots.");
        inventory.Arrange(shot, 20, inventory.Revision);
        Require(inventory.SlotOf(shot) == 20, "Arrange moves a token to the chosen grid slot.");
        string rifle = inventory.Items("party").Single(item => item.Definition == "rifle").Token;
        int rifleHome = inventory.SlotOf(rifle);
        inventory.Arrange(rifle, 20, inventory.Revision);
        Require(inventory.SlotOf(rifle) == 20 && inventory.SlotOf(shot) == rifleHome,
            "Arranging onto an occupied slot swaps the two tokens.");
        int last = inventory.Definitions.PartySlots - 1;
        inventory.Arrange(shot, last, inventory.Revision);
        Require(inventory.SlotOf(shot) == last, "The last grid slot is usable.");
        RequireRejected(() => inventory.Arrange(shot, inventory.Definitions.PartySlots, inventory.Revision),
            "Out-of-range grid slots are rejected.");
        RequireRejected(() => inventory.Arrange("s:no-such-item", 0, inventory.Revision),
            "Arranging an item outside the party inventory is rejected.");
        RequireRejected(() => inventory.Arrange(shot, 0, 0),
            "Arrange proposals from an earlier inventory revision are rejected.");
    }

    private static void VerifyPartyFull(ItemDefinitions definitions)
    {
        ItemDefinitions small = definitions with { PartySlots = 2 };
        ItemInventory inventory = new(small, [new PackOwner(1, "party", 1000, 1000)]);
        ulong itemId = 10;
        inventory.Grant(InventoryOwner.Parse("party"), "tonic", 1, () => itemId++);
        inventory.Grant(InventoryOwner.Parse("party"), "knife", 1, () => itemId++);
        inventory.Grant(InventoryOwner.Parse("party"), "tonic", 2, () => itemId++);
        Require(ItemQuantity(inventory, "party", "tonic") == 3, "Merging into an existing stack needs no fresh slot.");
        RequireRejected(() => inventory.Grant(InventoryOwner.Parse("party"), "cordial", 2, () => itemId++),
            "A full party grid rejects fresh tokens.");
    }

    private static void VerifyEquipmentViewsAndStats(GameDefinitions definitions, ItemInventory inventory)
    {
        PartyState party = new(definitions.Party.Positions, definitions.Party.MaxPartySize, definitions.Characters, definitions.Characters.DefaultPresetId);
        RiflesCharacter warden = Member(party, "warden");
        inventory.BindMembers(party.Entities, party.Members);
        inventory.BindRemaining(party.Entities);
        Require(inventory.Owners.All(o => inventory.IsBound(o.Key)),
            "Every registered pack — members, party, and containers — binds to an entity facade.");
        Require(warden.EquipmentBonuses == new EquipmentStatBonuses(6, 4) && warden.Power == warden.Definition.BasePower + 6
            && warden.Defense == warden.Definition.BaseDefense + 4, "Engine-backed equipment sources contribute to member statistics.");

        string spareRifle = inventory.Items("party").Single(item => item.Definition == "rifle").Token;
        ulong spareId = inventory.Find("party", spareRifle).Entity;
        CarriedItem wornRifle = inventory.Items("member:warden").Single(item => item.Definition == "rifle");
        inventory.Equip(ItemRef.Parse("party", spareRifle), "main-hand", warden.Power, inventory.Revision, new MemberOwner("warden"));
        CarriedItem nowWorn = inventory.Items("member:warden").Single(item => item.Definition == "rifle");
        Require(nowWorn.Entity == spareId && inventory.SlotOf(spareRifle) < 0,
            "Equipping from the party grid vacates the grid slot.");
        CarriedItem displacedRifle = inventory.Items("party").Single(item => item.Entity == wornRifle.Entity);
        Require(displacedRifle.Slots.Length == 0 && inventory.SlotOf(displacedRifle.Token) >= 0,
            "Displaced gear returns to the party grid instead of the member pack.");

        inventory.Transfer(ItemRef.Parse("member:warden", nowWorn.Token), InventoryOwner.Parse("party"), 1, inventory.Revision);
        CarriedItem bankedRifle = inventory.Items("party").Single(item => item.Entity == nowWorn.Entity);
        Require(bankedRifle.Slots.Length == 0 && inventory.SlotOf(bankedRifle.Token) >= 0,
            "Dragging worn gear back to the grid unequips it into a grid slot.");
        Require(inventory.View("member:warden").UniqueItems.Count == 1, "Member packs retain only worn gear.");

        Require(warden.EquipmentBonuses == new EquipmentStatBonuses(0, 4)
            && warden.Power == warden.Definition.BasePower && warden.Defense == warden.Definition.BaseDefense + 4,
            "Equipment bonuses follow current Engine assignments rather than a parallel item ledger.");
    }

    private static void VerifyCapacityFailureKeepsEquipment(ItemDefinitions definitions)
    {
        ItemDefinitions rifleOnly = definitions with
        {
            StartingItems = [new StartingItem("member:source", "rifle", 1, true)],
            PartySlots = 1,
        };
        ItemInventory inventory = new(rifleOnly,
        [
            new PackOwner(1, "member:source", 100, 100),
            new PackOwner(2, "party", 1000, 1000),
        ]);
        ulong itemId = 10;
        inventory.GrantStarting(() => itemId++);
        inventory.Grant(InventoryOwner.Parse("party"), "knife", 1, () => itemId++);

        string rifle = inventory.Items("member:source").Single().Token;
        ulong revision = inventory.Revision;
        RequireRejected(() => inventory.Transfer(ItemRef.Parse("member:source", rifle), InventoryOwner.Parse("member:target"), 1, revision),
            "Loose transfers into member packs are rejected even before capacity is consulted.");
        RequireRejected(() => inventory.Grant(InventoryOwner.Parse("party"), "tonic", 1, () => itemId++),
            "A full party grid rejects fresh tokens without touching the ledger.");

        // Swapping grid gear onto the member is net-zero: the equipped rifle
        // displaces back into the vacated slot, so full grids still equip.
        string knife = inventory.Items("party").Single(i => i.Definition == "knife").Token;
        inventory.Equip(ItemRef.Parse("party", knife), "main-hand", definitions.Item("knife").MinimumPower, inventory.Revision, new MemberOwner("source"));
        Require(inventory.Items("member:source").Single(i => i.Definition == "knife").Slots.Contains("main-hand")
            && inventory.Items("party").Single(i => i.Definition == "rifle").Slots.Length == 0,
            "A full grid still equips by swapping the displaced gear home.");
        Require(inventory.Revision != revision,
            "The successful equip advanced inventory state.");
    }

    private static void VerifySaveRestore(ItemDefinitions definitions, ItemInventory inventory)
    {
        InventorySnapshot saved = inventory.Capture();
        string expected = Describe(inventory);

        ItemInventory restored = ItemInventory.Restore(definitions, saved);
        Require(Describe(restored) == expected, "Save restoration reconstructs exact item ids, quantities, and equipment assignments.");
        Require(restored.Items("party").All(item => restored.SlotOf(item.Token) >= 0),
            "Restored party items keep grid slots.");
        ulong revision = restored.Revision;
        restored.Transfer(ItemRef.Parse("crate", "s:tonic"), new PartyOwner(), 1, revision);
        string beforeStaleRestore = Describe(restored);
        RequireRejected(() => restored.Transfer(ItemRef.Parse("crate", "s:tonic"), new PartyOwner(), 1, revision),
            "A proposal from before a reconstructed-world mutation is rejected.");
        Require(Describe(restored) == beforeStaleRestore, "A stale reconstructed-world proposal leaves inventory untouched.");
    }

    private static void VerifyOldSavesRejected(GameDefinitions definitions)
    {
        // A pre-party snapshot keeps loose items in member packs: loud reject.
        ItemInventory modern = CreateInventory(definitions);
        InventorySnapshot saved = modern.Capture();
        SavedPack[] packs = saved.Packs.Select(pack => pack.Owner.Key == "party"
            ? pack with { Stacks = [], Items = [], Slots = [] }
            : pack).ToArray();
        SavedPack warden = packs.Single(pack => pack.Owner.Key == "member:warden");
        packs[Array.IndexOf(packs, warden)] = warden with
        {
            Stacks = [new SavedStack("shot", 3)],
            Items = [new SavedItem(9001, "knife")],
        };
        RequireRejected(() => ItemInventory.Restore(definitions.Items, saved with { Packs = packs }),
            "Saves that predate the shared party inventory are rejected loudly, never migrated silently.");
    }

    private static void VerifyPartyFormationAndAuthoredResources(GameDefinitions definitions)
    {
        StarterPartyPresetDefinition preset = definitions.Characters.GetPreset(definitions.Characters.DefaultPresetId);
        PartyState party = new(definitions.Party.Positions, definitions.Party.MaxPartySize, definitions.Characters, preset.Id);
        Require(party.Members.All(member => member.Vitality == member.Definition.InitialVitality
            && member.Resource == member.Definition.InitialResource), "Authored starting injuries and resources initialize party state.");
        Require(party.Members.Any(member => member.Vitality < member.MaximumVitality)
            && party.Members.Any(member => member.Resource < member.MaximumResource), "The selected preset contains authored injury and resource state.");

        RiflesCharacter warden = Member(party, "warden");
        RiflesCharacter blade = Member(party, "blade");
        RiflesCharacter seeker = Member(party, "seeker");
        string seekerPosition = seeker.Position;
        Require(party.SwapFormation("warden", "seeker") && warden.Position == seekerPosition,
            "Living members can change the authored formation.");
        string wardenPosition = warden.Position;
        blade.ApplyDamage(long.MaxValue);
        Require(!party.SwapFormation("warden", "blade") && warden.Position == wardenPosition && !party.CanUseReach("blade", PartyReach.Melee),
            "Dead members cannot change formation or become eligible for actions.");
        Require(!party.EligibleMembers(PartyReach.Melee).Any(member => member.Definition.Id == "blade"),
            "Dead members are excluded from reach eligibility.");

        PartyState openParty = new(definitions.Party.Positions, definitions.Party.MaxPartySize, definitions.Characters.ResolvePreset(preset.Id));
        Require(openParty.MoveFormation("warden", "r0c0") && Member(openParty, "warden").Position == "r0c0",
            "Living members can move into an unoccupied formation position.");
        Require(!openParty.MoveFormation("warden", "r0c3") && !openParty.MoveFormation("no-such-member", "r0c0")
            && !openParty.MoveFormation("warden", "no-such-position") && !openParty.MoveFormation("warden", "r2c2"),
            "Occupied, unknown-member, unknown-position, and reserved-center formation moves are rejected.");
        Member(openParty, "blade").ApplyDamage(long.MaxValue);
        Require(!openParty.MoveFormation("blade", "r0c2"),
            "Dead members cannot change formation positions.");

        IReadOnlyList<MemberSnapshot> saved = party.Capture();
        warden.ApplyDamage(long.MaxValue);
        party.Restore(saved);
        Require(party.Capture().SequenceEqual(saved), "Party restore preserves formation, injury, and resource values exactly.");
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

    private static RiflesCharacter Member(PartyState party, string id) => party.Members.Single(member => member.Definition.Id == id);

    private static ulong ItemQuantity(ItemInventory inventory, string owner, string definition) => inventory.Items(owner)
        .Where(item => item.Definition == definition).Aggregate(0UL, (total, item) => checked(total + item.Quantity));

    private static ulong FungibleQuantity(ItemInventory inventory, string definition) => inventory.Owners
        .Aggregate(0UL, (total, owner) => checked(total + ItemQuantity(inventory, owner.Key, definition)));

    private static string Describe(ItemInventory inventory) => string.Join("|", inventory.Owners.OrderBy(owner => owner.Key, StringComparer.Ordinal)
        .Select(owner => owner.Key + ":" + string.Join(",", inventory.Items(owner.Key).OrderBy(item => item.Token, StringComparer.Ordinal)
            .Select(item => $"{item.Token}:{item.Definition}:{item.Quantity}:{string.Join('+', item.Slots.OrderBy(slot => slot, StringComparer.Ordinal))}@{inventory.SlotOf(item.Token)}"))));

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
