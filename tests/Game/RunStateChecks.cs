using System.Buffers;
using Rifles.Game.Content;
using Rifles.Game.Expedition;
using Rifles.Game.Items;
using Rifles.Game.Magic;
using Rifles.Game.Party;

internal static class RunStateChecks
{
    internal static void Run(GameDefinitions definitions, ExpeditionSnapshot fixture)
    {
        VerifySingleFloorRoundTrip(definitions, fixture);
        VerifyRetainedFloorKeepsOnlyFloorState(definitions, fixture);
        VerifyInvalidRunsAreRejected(definitions, fixture);
        Console.WriteLine("Run state checks passed: retained floors, travelling party state, and run validation.");
    }

    private static void VerifySingleFloorRoundTrip(GameDefinitions definitions, ExpeditionSnapshot fixture)
    {
        RunSnapshot run = new(fixture, []);
        RunCodec.Validate(run, definitions);

        RunCodec codec = new();
        ArrayBufferWriter<byte> bytes = new();
        codec.Encode(run, bytes);
        RunSnapshot decoded = codec.Decode(bytes.WrittenSpan);
        RunCodec.Validate(decoded, definitions);

        Require(decoded.Inactive.Length == 0 && decoded.Active.Id == fixture.Id
            && decoded.Active.Floor.GenerationIdentity == fixture.Floor.GenerationIdentity,
            "A one-floor run round-trips and validates without changing its active floor.");
    }

    private static void VerifyRetainedFloorKeepsOnlyFloorState(GameDefinitions definitions, ExpeditionSnapshot fixture)
    {
        RetainedFloor retained = RetainedFloor.Capture(fixture);
        Require(retained.Inventory.Packs.All(pack => !ItemInventory.IsMember(pack.Owner.Key)),
            "Retained floors exclude travelling member packs.");

        ExpeditionSnapshot current = ChangedPartyState(definitions, fixture);
        ExpeditionSnapshot joined = retained.Join(current, retained.Departure);

        Require(DescribeMemberPacks(joined.Inventory) == DescribeMemberPacks(current.Inventory),
            "Returning to a retained floor uses the current travelling member inventory.");
        Require(joined.Members.SequenceEqual(current.Members),
            "Returning to a retained floor uses current travelling member vitality and formation state.");
        Require(joined.Combat.Magic!.Books.SequenceEqual(current.Combat.Magic!.Books),
            "Returning to a retained floor uses current travelling spellbooks.");
        Require(joined.ItemWorld.Door == retained.ItemWorld.Door && joined.ItemWorld.DoorOpen == retained.ItemWorld.DoorOpen
            && joined.Actor == retained.Actor && joined.Combat.Enemies.SequenceEqual(retained.Enemies),
            "Returning to a retained floor preserves its door, patrol actor, and enemy facts.");

        RunCodec.Validate(new RunSnapshot(joined, []), definitions);
    }

    private static ExpeditionSnapshot ChangedPartyState(GameDefinitions definitions, ExpeditionSnapshot fixture)
    {
        ItemInventory inventory = ItemInventory.Restore(definitions.Items, fixture.Inventory);
        string source = fixture.Roster[0].Id;
        string destination = fixture.Roster[1].Id;
        string sourcePack = "member:" + source;
        string destinationPack = "member:" + destination;
        CarriedItem item = inventory.Items(sourcePack).First();
        inventory.Transfer(sourcePack, destinationPack, item.Token, 1, inventory.Revision);

        MemberSnapshot[] members = fixture.Members.Select((member, index) => index == 0
            ? member with { Vitality = Math.Max(1, member.Vitality - 1) }
            : member).ToArray();
        MagicState magic = new(definitions.Magic, fixture.Roster.Select(member => member.Id));
        MagicBookSnapshot firstBook = magic.Capture().Books[0];
        magic.Select(firstBook.Member, firstBook.Known[0]);

        return fixture with
        {
            Members = members,
            Inventory = inventory.Capture(),
            Combat = fixture.Combat with { Magic = magic.Capture() },
        };
    }

    private static void VerifyInvalidRunsAreRejected(GameDefinitions definitions, ExpeditionSnapshot fixture)
    {
        RetainedFloor sameFloor = RetainedFloor.Capture(fixture);
        RequireRejected(() => RunCodec.Validate(new RunSnapshot(fixture, [sameFloor]), definitions),
            "A run cannot retain a second copy of the active floor identity.");

        string otherFloor = fixture.Intent.Floors.First(floor => floor.Id != fixture.Floor.IntentFloorId).Id;
        RetainedFloor withMemberPack = sameFloor with
        {
            Floor = sameFloor.Floor with { IntentFloorId = otherFloor },
            Inventory = fixture.Inventory,
        };
        RequireRejected(() => RunCodec.Validate(new RunSnapshot(fixture, [withMemberPack]), definitions),
            "A retained floor cannot carry travelling member packs.");
    }

    private static string DescribeMemberPacks(InventorySnapshot inventory) => string.Join("|", inventory.Packs
        .Where(pack => ItemInventory.IsMember(pack.Owner.Key)).OrderBy(pack => pack.Owner.Key, StringComparer.Ordinal)
        .Select(pack => pack.Owner.Key + ":" + string.Join(",", pack.Stacks.OrderBy(stack => stack.Definition, StringComparer.Ordinal)
            .Select(stack => stack.Definition + ":" + stack.Quantity)) + ";" + string.Join(",", pack.Items
            .OrderBy(item => item.Id).Select(item => item.Id + ":" + item.Definition))));

    private static void RequireRejected(Action action, string message)
    {
        try { action(); }
        catch (Exception) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
