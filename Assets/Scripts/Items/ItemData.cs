using UnityEngine;

public enum ItemType { Weapon, Armor, Consumable, Material}

[CreateAssetMenu(fileName = "NewItemData", menuName = "Inventory/Item Data")]
public class ItemData : ScriptableObject
{
    [Header("Base Item Info")]
    public string itemName;
    public Sprite icon;
    public ItemType itemType;

    [Header("Stacking Info")]
    public bool isStackable = false;
    public int maxStackSize = 1;
}