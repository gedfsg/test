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

                if (slotData.item.itemType == ItemType.Weapon)
                {
                    WeaponData weaponData = slotData.item as WeaponData;
                    if (weaponData != null)
                    {
                        if (slotBg != null)
                            slotBg.color = new Color(0.28f, 0.20f, 0.10f, 1f);

                        // 인덱스 대신 slotId 캡처
                        string capturedId = slotData.slotId;
                        WeaponData captured = weaponData;
                        AddClickEvent(newSlot, () => EquipWeapon(captured, capturedId));
                    }
                }
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

    private void EquipWeapon(WeaponData newWeapon, string slotId)
    {
        if (playerController == null)
        {
            Debug.LogWarning("PlayerController를 찾을 수 없습니다.");
            return;
        }

        WeaponData currentWeapon = playerController.GetCurrentWeaponData(newWeapon.type);

        // slotId로 정확한 슬롯 찾아서 저장된 탄수 가져오기
        InventorySlot newSlot = inventoryManager.GetSlotById(slotId);
        int savedAmmo = newSlot?.currentAmmo ?? -1;

        // slotId로 정확하게 제거
        inventoryManager.RemoveItemById(slotId);

        if (currentWeapon != null)
        {
            // 현재 장착 무기 탄수 저장하면서 인벤토리에 반납
            int currentAmmo = playerController.GetCurrentAmmo();
            inventoryManager.AddItemWithAmmo(currentWeapon, 1, currentAmmo);
        }

        // 저장된 탄수로 새 무기 장착
        playerController.SwapWeaponData(newWeapon);

        Debug.Log($"[인벤토리] '{newWeapon.itemName}' 장착! / '{currentWeapon?.itemName}' 인벤토리로");
        ToggleInventory();
    }

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