using System.Collections.Generic;

public static class ItemActionProvider
{
    public static List<ItemAction> GetActions(InventorySlot slot)
    {
        var actions = new List<ItemAction>();

        switch (slot.item.itemType)
        {
            case ItemType.Consumable:
                actions.Add(new ItemAction("Use", ActionType.Use));
                actions.Add(new ItemAction("Equip to Hotbar", ActionType.EquipToHotbar));
                actions.Add(new ItemAction("Drop", ActionType.Drop));
                break;

            case ItemType.Weapon:
                actions.Add(new ItemAction("Equip to Hotbar", ActionType.EquipToHotbar));
                actions.Add(new ItemAction("Drop", ActionType.Drop));
                break;

            case ItemType.Armor:
                actions.Add(new ItemAction("Equip", ActionType.Equip));
                actions.Add(new ItemAction("Drop", ActionType.Drop));
                break;

            case ItemType.Material:
                actions.Add(new ItemAction("Drop", ActionType.Drop));
                break;

            default:
                actions.Add(new ItemAction("Drop", ActionType.Drop));
                break;
        }

        return actions;
    }
}

public enum ActionType { Use, Equip, EquipToHotbar, Drop }

public class ItemAction
{
    public string label;
    public ActionType type;

    public ItemAction(string label, ActionType type)
    {
        this.label = label;
        this.type = type;
    }
}