using UnityEngine;

public enum ItemType { Weapon, Armor, Consumable, Material, Ammo}

[CreateAssetMenu(fileName = "NewItemData", menuName = "Inventory/Item Data")]
public class ItemData : ScriptableObject
{
    [Header("Base Item Info")]
    public string itemName;
    public Sprite icon;
    public ItemType itemType;
    public Rarity rarity = Rarity.Common;

    [Header("월드 모델 (바닥에 놓였을 때 3D 모델. 비우면 아이콘 판으로 표시)")]
    public GameObject worldModel;

    [Header("Stacking Info")]
    public bool isStackable = false;
    public int maxStackSize = 1;
}