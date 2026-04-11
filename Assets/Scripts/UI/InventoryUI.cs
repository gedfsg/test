using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

public class InventoryUI : MonoBehaviour
{
    [Header("References")]
    public InventoryManager inventoryManager;
    public GameObject inventoryWindow;
    public Transform slotGrid;
    public GameObject slotPrefab;

    public bool isInventoryOpen = false;

    private PlayerInputActions inputActions;
    private PlayerController playerController;

    void Awake()
    {
        inputActions = new PlayerInputActions();
    }

    void Start()
    {
        playerController = FindAnyObjectByType<PlayerController>();
    }

    void OnEnable()
    {
        inputActions.Enable();
        inputActions.Player.Inventory.performed += OnInventoryPerformed;
    }

    void OnDisable()
    {
        inputActions.Disable();
        inputActions.Player.Inventory.performed -= OnInventoryPerformed;
    }

    private void OnInventoryPerformed(InputAction.CallbackContext context)
    {
        ToggleInventory();
    }

    public void ToggleInventory()
    {
        isInventoryOpen = !isInventoryOpen;
        inventoryWindow.SetActive(isInventoryOpen);

        if (isInventoryOpen)
            UpdateUI();
    }

    public void UpdateUI()
    {
        foreach (Transform child in slotGrid)
            Destroy(child.gameObject);

        for (int i = 0; i < inventoryManager.maxCapacity; i++)
        {
            GameObject newSlot = Instantiate(slotPrefab, slotGrid);

            Image slotBg = newSlot.GetComponent<Image>();
            if (slotBg != null)
                slotBg.color = new Color(0.12f, 0.14f, 0.17f, 1f);

            Image icon = newSlot.transform.Find("Icon").GetComponent<Image>();
            TextMeshProUGUI amountText = newSlot.transform.Find("AmountText").GetComponent<TextMeshProUGUI>();

            if (i < inventoryManager.inventory.Count)
            {
                InventorySlot slotData = inventoryManager.inventory[i];

                if (slotBg != null)
                    slotBg.color = new Color(0.18f, 0.22f, 0.27f, 1f);

                if (slotData.item.icon != null)
                {
                    icon.sprite = slotData.item.icon;
                    icon.color = Color.white;
                    icon.enabled = true;
                }
                else
                {
                    icon.enabled = false;
                }

                amountText.text = slotData.amount > 1 ? slotData.amount.ToString() : "";
                amountText.color = new Color(0.89f, 0.73f, 0.43f, 1f);

                // 무기 슬롯: 클릭 시 장착
                if (slotData.item.itemType == ItemType.Weapon)
                {
                    WeaponData weaponData = slotData.item as WeaponData;
                    if (weaponData != null)
                    {
                        // 무기 슬롯은 주황빛 배경으로 구분
                        if (slotBg != null)
                            slotBg.color = new Color(0.28f, 0.20f, 0.10f, 1f);

                        WeaponData captured = weaponData;
                        AddClickEvent(newSlot, () => EquipWeapon(captured));
                    }
                }
                // 소비 아이템 슬롯: 클릭 시 사용
                else if (slotData.item.itemType == ItemType.Consumable)
                {
                    AddClickEvent(newSlot, () =>
                    {
                        inventoryManager.UseConsumable();
                        UpdateUI();
                    });
                }
            }
            else
            {
                icon.enabled = false;
                amountText.text = "";
            }
        }
    }

    // 무기 장착: 현재 장착 무기는 인벤토리로, 클릭한 무기는 장착
    private void EquipWeapon(WeaponData newWeapon)
    {
        if (playerController == null)
        {
            Debug.LogWarning("PlayerController를 찾을 수 없습니다.");
            return;
        }

        // 1. 현재 장착 중인 무기 데이터를 가져옴
        WeaponData currentWeapon = playerController.GetCurrentWeaponData(newWeapon.type);

        // 2. 현재 무기가 있으면 인벤토리에 추가, 클릭한 무기는 인벤토리에서 제거
        if (currentWeapon != null)
        {
            // 인벤토리에서 새 무기 제거
            inventoryManager.RemoveItem(newWeapon);
            // 현재 무기를 인벤토리에 추가
            inventoryManager.AddItem(currentWeapon, 1);
        }
        else
        {
            // 현재 장착 무기가 없으면 그냥 인벤토리에서만 제거
            inventoryManager.RemoveItem(newWeapon);
        }

        // 3. 새 무기 장착
        playerController.SwapWeaponData(newWeapon);
        Debug.Log($"[인벤토리] '{newWeapon.itemName}' 장착! / '{currentWeapon?.itemName}' 인벤토리로");

        // 4. 장착 후 인벤토리 닫기
        ToggleInventory();
    }

    // GameObject에 PointerClick 이벤트 추가
    private void AddClickEvent(GameObject target, System.Action action)
    {
        EventTrigger trigger = target.GetComponent<EventTrigger>();
        if (trigger == null)
            trigger = target.AddComponent<EventTrigger>();

        EventTrigger.Entry entry = new EventTrigger.Entry();
        entry.eventID = EventTriggerType.PointerClick;
        entry.callback.AddListener((_) => action?.Invoke());
        trigger.triggers.Add(entry);
    }
}