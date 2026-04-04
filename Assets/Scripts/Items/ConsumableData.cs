using UnityEngine;

[CreateAssetMenu(fileName = "New Consumable Data", menuName = "Inventory/Consumable Data")]
public class ConsumableData : ItemData
{
    [Header("Consumable Stats")]
    public float healAmount;
}
