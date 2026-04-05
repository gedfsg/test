using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class InventorySlot
{
    public ItemData item;
    public int amount;

    public InventorySlot(ItemData item, int amount)
    {
        this.item = item;
        this.amount = amount;
    }
}

public class InventoryManager : MonoBehaviour
{
    [Header("Inventory Settings")]
    public int maxCapacity = 10;

    public List<InventorySlot> inventory = new List<InventorySlot>();

    private InventoryUI inventoryUI;

    void Start()
    {
        inventoryUI = FindAnyObjectByType<InventoryUI>();
    }

    private void RefreshUI()
    {
        if (inventoryUI != null && inventoryUI.isInventoryOpen)
            inventoryUI.UpdateUI();
    }

    public bool AddItem(ItemData itemToAdd, int amountToAdd)
    {
        if (itemToAdd.isStackable)
        {
            foreach (InventorySlot slot in inventory)
            {
                if (slot.item == itemToAdd && slot.amount < itemToAdd.maxStackSize)
                {
                    int spaceLeft = itemToAdd.maxStackSize - slot.amount;

                    if (spaceLeft >= amountToAdd)
                    {
                        slot.amount += amountToAdd;
                        Debug.Log(itemToAdd.itemName + " " + amountToAdd + "개 획득 (누적: " + slot.amount + ")");
                        RefreshUI();
                        return true;
                    }
                    else
                    {
                        slot.amount += spaceLeft;
                        amountToAdd -= spaceLeft;
                    }
                }
            }
        }

        while (amountToAdd > 0)
        {
            if (inventory.Count >= maxCapacity)
            {
                Debug.LogWarning("가방이 꽉 찼어!");
                RefreshUI();
                return false;
            }

            int amountForNewSlot = Mathf.Min(amountToAdd, itemToAdd.maxStackSize);
            inventory.Add(new InventorySlot(itemToAdd, amountForNewSlot));
            Debug.Log(itemToAdd.itemName + " " + amountForNewSlot + "개 새 슬롯에 추가됨.");

            amountToAdd -= amountForNewSlot;
        }

        RefreshUI();
        return true;
    }

    public void IncreaseCapacity(int extraSlots)
    {
        maxCapacity += extraSlots;
        Debug.Log("가방 용량이 " + maxCapacity + "칸으로 늘어났어!");
    }

    void Update()
    {
        if (UnityEngine.InputSystem.Keyboard.current.hKey.wasPressedThisFrame)
        {
            UseConsumable();
        }
    }

    public void UseConsumable()
    {
        for (int i = 0; i < inventory.Count; i++)
        {
            InventorySlot slot = inventory[i];

            if (slot.item.itemType == ItemType.Consumable && slot.amount > 0)
            {
                ConsumableData potion = slot.item as ConsumableData;
                if (potion != null)
                {
                    Health playerHealth = GetComponent<Health>();
                    if (playerHealth != null)
                    {
                        playerHealth.Heal(potion.healAmount);
                    }

                    slot.amount--;
                    Debug.Log(potion.itemName + " 사용! (남은 개수: " + slot.amount + ")");

                    if (slot.amount <= 0)
                    {
                        inventory.RemoveAt(i);
                    }

                    RefreshUI();
                    return;
                }
            }
        }
        Debug.LogWarning("가방에 먹을 수 있는 소비품이 없어!");
    }
}