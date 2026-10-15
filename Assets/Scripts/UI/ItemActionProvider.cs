using System.Collections.Generic;

public static class ItemActionProvider
{
    public static List<ItemAction> GetActions(InventorySlot slot)
    {
        var actions = new List<ItemAction>();

        switch (slot.item.itemType)
        {
            case ItemType.Consumable:
                actions.Add(new ItemAction("사용", ActionType.Use));
                actions.Add(new ItemAction("핫바에 등록", ActionType.EquipToHotbar));
                actions.Add(new ItemAction("버리기", ActionType.Drop));
                break;

            case ItemType.Weapon:
                actions.Add(new ItemAction("핫바에 등록", ActionType.EquipToHotbar));
                actions.Add(new ItemAction("버리기", ActionType.Drop));
                break;

            case ItemType.Armor:
                actions.Add(new ItemAction("장착", ActionType.Equip));
                actions.Add(new ItemAction("버리기", ActionType.Drop));
                break;

            case ItemType.Material:
                // 레시피가 있는 재료만 제작 메뉴 (넝마 2개 → 붕대)
                if (CraftingRecipe.FindByIngredient(slot.item) != null)
                    actions.Add(new ItemAction("제작하기", ActionType.Craft));
                actions.Add(new ItemAction("버리기", ActionType.Drop));
                break;

            default:
                actions.Add(new ItemAction("버리기", ActionType.Drop));
                break;
        }

        return actions;
    }
}

public enum ActionType { Use, Equip, EquipToHotbar, Drop, Craft }

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