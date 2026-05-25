using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class InventorySlot
{
    public string slotId;
    public ItemData item;
    public int amount;
    public int currentAmmo = -1;

    public InventorySlot(ItemData item, int amount)
    {
        this.slotId = System.Guid.NewGuid().ToString();
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
        if (itemToAdd.itemType == ItemType.Ammo)
        {
            AmmoData ad = itemToAdd as AmmoData;
            if (ad != null && AmmoInventory.Instance != null)
            {
                AmmoInventory.Instance.AddAmmo(ad.weaponType, ad.ammoAmount * amountToAdd);
                return true;
            }
        }

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
                        Debug.Log(itemToAdd.itemName + " " + amountToAdd + "개 획득");
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

    public bool AddItemWithAmmo(ItemData itemToAdd, int amountToAdd, int ammo)
    {
        bool result = AddItem(itemToAdd, amountToAdd);
        if (result && inventory.Count > 0)
        {
            InventorySlot slot = inventory[inventory.Count - 1];
            slot.currentAmmo = ammo;
        }
        return result;
    }

    public InventorySlot GetSlotById(string id)
        => inventory.Find(s => s.slotId == id);

    public void RemoveItemById(string id)
    {
        InventorySlot slot = GetSlotById(id);
        if (slot == null) return;
        slot.amount--;
        if (slot.amount <= 0) inventory.Remove(slot);
        RefreshUI();
    }

    // 슬롯을 직접 지정해서 사용 (InventoryUI에서 호출)
    public void UseConsumable(InventorySlot slot)
    {
        ConsumableData potion = slot.item as ConsumableData;
        if (potion == null) return;

        PlayerBuffManager buffMgr = GetComponent<PlayerBuffManager>();
        Health health = GetComponent<Health>();

        if (buffMgr != null && health != null)
            buffMgr.ApplyConsumable(potion, health);
        else if (health != null)
            health.Heal(potion.healAmount);

        slot.amount--;
        Debug.Log($"{potion.itemName} 사용! (남은 개수: {slot.amount})");
        if (slot.amount <= 0) inventory.Remove(slot);

        RefreshUI();
    }

    // H키용 — 인벤토리에서 첫 번째 소모품 자동 사용 (기존 호환)
    public void UseConsumable()
    {
        InventorySlot slot = inventory.Find(s =>
            s.item.itemType == ItemType.Consumable && s.amount > 0);

        if (slot != null) UseConsumable(slot);
        else Debug.LogWarning("가방에 먹을 수 있는 소비품이 없어!");
    }

    public void IncreaseCapacity(int extraSlots)
    {
        maxCapacity += extraSlots;
        Debug.Log("가방 용량이 " + maxCapacity + "칸으로 늘어났어!");
    }

    public void RemoveItem(ItemData itemToRemove)
    {
        for (int i = 0; i < inventory.Count; i++)
        {
            if (inventory[i].item == itemToRemove)
            {
                inventory[i].amount--;
                if (inventory[i].amount <= 0) inventory.RemoveAt(i);
                RefreshUI();
                return;
            }
        }
    }

    public void InsertItemAt(int index, ItemData item, int amount, int ammo = -1)
    {
        InventorySlot slot = new InventorySlot(item, amount);
        slot.currentAmmo = ammo;
        inventory.Insert(index, slot);
        RefreshUI();
    }

    public int GetSlotIndex(string slotId)
        => inventory.FindIndex(s => s.slotId == slotId);
}