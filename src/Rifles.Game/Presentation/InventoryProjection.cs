using Rifles.Game.Dungeon;
using Rifles.Game.Items;
using Rifles.Game.Party;

namespace Rifles.Game.Presentation;

internal static class InventoryProjection
{
    internal static uint Build(SessionValueBuilder value, ItemInventory inventory, ExplorationItems world,
        DungeonScene scene, ExplorationState exploration, PartyState party, string selectedMember, ItemArtDefinition art, Func<string, bool> dropReachable)
    {
        uint owners = value.Object(inventory.Owners.Select(owner =>
        {
            bool member = ItemInventory.IsMember(owner.Key), container = owner.Key == "crate";
            bool drop = owner.Key.StartsWith("combat:", StringComparison.Ordinal);
            bool reachable = member || (drop ? dropReachable(owner.Key) : world.Reachable(owner.Key, exploration, scene));
            bool opened = !container || world.OpenContainer == owner.Key;
            string name = member ? party.Members.Single(m => "member:" + m.Definition.Id == owner.Key).Definition.Name
                : drop ? "Ground belongings" : world.AnchorDefinition(owner.Key).Name;
            var view = inventory.View(owner.Key);
            uint items = value.Object((opened && reachable ? inventory.Items(owner.Key) : []).Select(item =>
            {
                GearDefinition definition = inventory.Definitions.Item(item.Definition);
                int imageIndex = Array.FindIndex(art.Images, image => image.Id == definition.Image);
                return (item.Token, value.Object(("name", value.String(definition.Name)), ("definition", value.String(definition.Id)),
                    ("entity", value.String(item.Entity.ToString())), ("quantity", value.Number(item.Quantity)),
                    ("slots", value.String(string.Join(',', item.Slots))), ("allowedSlots", value.String(string.Join(',', definition.Slots))),
                    ("power", value.Number(definition.Power)), ("defense", value.Number(definition.Defense)),
                    ("minimumPower", value.Number(definition.MinimumPower)), ("ammunition", value.String(definition.Ammunition)),
                    ("use", value.String(definition.Use.ToString())), ("image", value.String("/product-ui/items.png")), ("imageIndex", value.Number(imageIndex))));
            }).ToArray());
            return (owner.Key, value.Object(("name", value.String(name)), ("id", value.String(owner.Id.ToString())),
                ("revision", value.String(world.Revision.ToString())), ("reachable", value.Number(reachable ? 1 : 0)),
                ("opened", value.Number(opened ? 1 : 0)), ("kind", value.String(member ? "member" : container ? "container" : drop ? "ground" : "anchor")),
                ("mass", value.Number(view.Capacity.Single(c => c.Metric == ItemInventory.MassMetric).Used)),
                ("maxMass", value.Number(owner.MassCapacity)), ("space", value.Number(view.Capacity.Single(c => c.Metric == ItemInventory.SpaceMetric).Used)),
                ("maxSpace", value.Number(owner.SpaceCapacity)), ("items", items)));
        }).ToArray());
        return value.Object(("revision", value.String(inventory.Revision.ToString())),
            ("selectedOwner", value.String("member:" + selectedMember)), ("owners", owners));
    }
    internal static uint Equipment(SessionValueBuilder value, ItemInventory inventory, string selectedMember) =>
        value.Object(inventory.Definitions.EquipmentSlots.Select(slot =>
        {
            CarriedItem? item = inventory.Items("member:" + selectedMember).SingleOrDefault(i => i.Slots.Contains(slot));
            return (slot, value.Object(("token", value.String(item?.Token ?? "")),
                ("name", value.String(item is null ? "Empty" : inventory.Definitions.Item(item.Definition).Name))));
        }).ToArray());
}
