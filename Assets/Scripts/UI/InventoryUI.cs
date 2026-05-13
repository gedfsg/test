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
            Image icon = newSlot.transform.Find("Icon").GetComponent<Image>();
            TextMeshProUGUI amountText = newSlot.transform.Find("AmountText").GetComponent<TextMeshProUGUI>();

            // 기본 슬롯 스타일 (빈 슬롯)
            if (slotBg != null)
                slotBg.color = new Color(0.08f, 0.10f, 0.13f, 1f);

            // 테두리 효과용 Outline 추가
            Outline outline = newSlot.GetComponent<Outline>();
            if (outline == null) outline = newSlot.AddComponent<Outline>();
            outline.effectColor    = new Color(0.25f, 0.30f, 0.35f, 0.6f);
            outline.effectDistance = new Vector2(1f, -1f);

            if (i < inventoryManager.inventory.Count)
            {
                InventorySlot slotData = inventoryManager.inventory[i];

                // 아이템 타입별 배경색
                if (slotBg != null)
                {
                    switch (slotData.item.itemType)
                    {
                        case ItemType.Consumable:
                            slotBg.color = new Color(0.10f, 0.20f, 0.12f, 1f); // 초록계열
                            outline.effectColor = new Color(0.20f, 0.60f, 0.25f, 0.7f);
                            break;
                        default:
                            slotBg.color = new Color(0.14f, 0.17f, 0.21f, 1f);
                            outline.effectColor = new Color(0.30f, 0.35f, 0.42f, 0.7f);
                            break;
                    }
                }

                if (slotData.item.icon != null)
                {
                    icon.sprite  = slotData.item.icon;
                    icon.color   = Color.white;
                    icon.enabled = true;
                }
                else
                {
                    icon.enabled = false;
                }

                // 수량 텍스트
                amountText.text  = slotData.amount > 1 ? "x" + slotData.amount : "";
                amountText.color = new Color(0.95f, 0.80f, 0.40f, 1f);

                // 아이템 이름 표시 (AmountText 아래 새 텍스트)
                AddItemNameLabel(newSlot, slotData.item.itemName);

                if (slotData.item.itemType == ItemType.Consumable)
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
                icon.enabled    = false;
                amountText.text = "";
            }
        }
    }

    private void AddItemNameLabel(GameObject slot, string itemName)
    {
        // 이미 있으면 스킵
        Transform existing = slot.transform.Find("ItemName");
        if (existing != null) return;

        GameObject nameObj = new GameObject("ItemName");
        nameObj.transform.SetParent(slot.transform, false);

        RectTransform rt = nameObj.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 0f);
        rt.anchorMax        = new Vector2(1f, 0.28f);
        rt.offsetMin        = Vector2.zero;
        rt.offsetMax        = Vector2.zero;

        TextMeshProUGUI txt = nameObj.AddComponent<TextMeshProUGUI>();
        txt.text         = itemName.Length > 8 ? itemName.Substring(0, 8) : itemName;
        txt.fontSize     = 8f;
        txt.color        = new Color(0.80f, 0.83f, 0.87f, 1f);
        txt.alignment    = TextAlignmentOptions.Center;
        txt.overflowMode = TextOverflowModes.Ellipsis;
    }

    private void EquipWeapon(WeaponData newWeapon, string slotId)
    {
        if (playerController == null)
        {
            Debug.LogWarning("PlayerController를 찾을 수 없습니다.");
            return;
        }

        WeaponType lookupType = (newWeapon.type == WeaponType.Melee)
            ? WeaponType.Melee
            : WeaponType.Ranged;

        WeaponData currentWeapon = playerController.GetCurrentWeaponData(lookupType);

        // 새 무기 슬롯의 인덱스와 탄수 저장
        int slotIndex = inventoryManager.GetSlotIndex(slotId);
        InventorySlot newSlot = inventoryManager.GetSlotById(slotId);
        int savedAmmo = newSlot?.currentAmmo ?? -1;

        // 새 무기 슬롯 제거
        inventoryManager.RemoveItemById(slotId);

        if (currentWeapon != null)
        {
            // 현재 무기를 새 무기가 있던 자리에 삽입
            int currentAmmo = playerController.GetCurrentAmmo();
            inventoryManager.InsertItemAt(slotIndex, currentWeapon, 1, currentAmmo);
        }

        playerController.SwapWeaponData(newWeapon, savedAmmo);

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