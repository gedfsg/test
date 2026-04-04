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
    public int maxCapacity = 10; // 초기 가방 칸 수.
    
    // 실제 아이템들이 담기는 가방 리스트임.
    public List<InventorySlot> inventory = new List<InventorySlot>();

    // 아이템을 가방에 넣는 핵심 함수임.
    public bool AddItem(ItemData itemToAdd, int amountToAdd)
    {
        // 1. 겹칠 수 있는(Stackable) 아이템인지 확인함.
        if (itemToAdd.isStackable)
        {
            // 가방을 뒤져서 같은 아이템이 있고, 최대치까지 꽉 차지 않은 슬롯을 찾음.
            foreach (InventorySlot slot in inventory)
            {
                if (slot.item == itemToAdd && slot.amount < itemToAdd.maxStackSize)
                {
                    int spaceLeft = itemToAdd.maxStackSize - slot.amount;

                    // 여유 공간이 충분하다면 전부 넣음.
                    if (spaceLeft >= amountToAdd)
                    {
                        slot.amount += amountToAdd;
                        Debug.Log(itemToAdd.itemName + " " + amountToAdd + "개 획득 (누적: " + slot.amount + ")");
                        return true;
                    }
                    else
                    {
                        // 자리가 모자라면 채울 수 있는 만큼만 채우고, 남은 건 다음 코드로 넘겨서 새 칸을 파게 함.
                        slot.amount += spaceLeft;
                        amountToAdd -= spaceLeft;
                    }
                }
            }
        }

        // 2. 겹칠 수 없거나(무기 등) 기존 슬롯이 다 차서 남은 경우, 새 슬롯을 파야 함.
        while (amountToAdd > 0)
        {
            // 가방이 터지기 직전인지 확인함.
            if (inventory.Count >= maxCapacity)
            {
                Debug.LogWarning("가방이 꽉 찼어! 짐 좀 버려, 조빱아!");
                return false;
            }

            // 한 칸에 들어갈 수 있는 최대치만큼만 묶어서 새 슬롯에 넣음.
            int amountForNewSlot = Mathf.Min(amountToAdd, itemToAdd.maxStackSize);
            inventory.Add(new InventorySlot(itemToAdd, amountForNewSlot));
            Debug.Log(itemToAdd.itemName + " " + amountForNewSlot + "개 새 슬롯에 추가됨.");
            
            amountToAdd -= amountForNewSlot;
        }

        return true;
    }

    // 나중에 큰 배낭 아이템을 먹었을 때 이 함수를 부르면 가방 칸 수가 늘어남
    public void IncreaseCapacity(int extraSlots)
    {
        maxCapacity += extraSlots;
        Debug.Log("가방 용량이 " + maxCapacity + "칸으로 늘어났어!");
    }

    void Update()
    {
        // UI가 없으므로 임시로 H 키를 누르면 소비품을 사용하도록 단축키를 배정함.
        if (UnityEngine.InputSystem.Keyboard.current.hKey.wasPressedThisFrame)
        {
            UseConsumable();
        }
    }

    // 가방에서 소비품을 찾아 사용하는 로직임.
    public void UseConsumable()
    {
        for (int i = 0; i < inventory.Count; i++)
        {
            InventorySlot slot = inventory[i];
            
            // 칸에 있는 아이템이 소비품이고, 개수가 1개 이상인지 확인함.
            if (slot.item.itemType == ItemType.Consumable && slot.amount > 0)
            {
                ConsumableData potion = slot.item as ConsumableData;
                if (potion != null)
                {
                    // 플레이어의 Health 스크립트를 찾아 회복 함수를 실행함.
                    Health playerHealth = GetComponent<Health>();
                    if (playerHealth != null)
                    {
                        playerHealth.Heal(potion.healAmount);
                    }

                    // 사용했으므로 개수를 1개 차감함.
                    slot.amount--;
                    Debug.Log(potion.itemName + " 사용! (남은 개수: " + slot.amount + ")");
                    
                    // 다 먹어서 0개가 되면 가방에서 그 칸을 아예 삭제함.
                    if (slot.amount <= 0)
                    {
                        inventory.RemoveAt(i);
                    }
                    return; // 하나만 먹고 함수를 종료함.
                }
            }
        }
        Debug.LogWarning("가방에 먹을 수 있는 소비품이 없어, 조빱아!");
    }
}