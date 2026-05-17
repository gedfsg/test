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

    private void OnInventoryPerformed(InputAction.CallbackContext context) => ToggleInventory();

    public void ToggleInventory()
    {
        isInventoryOpen = !isInventoryOpen;
        inventoryWindow.SetActive(isInventoryOpen);

        if (isInventoryOpen)
            UpdateUI();
        else
            ContextMenuUI.Instance?.Hide();
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

            // 기본 스타일
            if (slotBg != null) slotBg.color = new Color(0.08f, 0.10f, 0.13f, 1f);

            Outline outline = newSlot.GetComponent<Outline>() ?? newSlot.AddComponent<Outline>();
            outline.effectColor = new Color(0.25f, 0.30f, 0.35f, 0.6f);
            outline.effectDistance = new Vector2(1f, -1f);

            if (i < inventoryManager.inventory.Count)
            {
                InventorySlot slotData = inventoryManager.inventory[i];

                // 타입별 배경색
                if (slotBg != null)
                {
                    switch (slotData.item.itemType)
                    {
                        case ItemType.Consumable:
                            slotBg.color = new Color(0.10f, 0.20f, 0.12f, 1f);
                            outline.effectColor = new Color(0.20f, 0.60f, 0.25f, 0.7f);
                            break;
                        case ItemType.Weapon:
                            slotBg.color = new Color(0.20f, 0.12f, 0.10f, 1f);
                            outline.effectColor = new Color(0.70f, 0.25f, 0.20f, 0.7f);
                            break;
                        case ItemType.Material:
                            slotBg.color = new Color(0.15f, 0.14f, 0.10f, 1f);
                            outline.effectColor = new Color(0.60f, 0.55f, 0.20f, 0.7f);
                            break;
                        default:
                            slotBg.color = new Color(0.14f, 0.17f, 0.21f, 1f);
                            outline.effectColor = new Color(0.30f, 0.35f, 0.42f, 0.7f);
                            break;
                    }
                }

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

                amountText.text = slotData.amount > 1 ? "x" + slotData.amount : "";
                amountText.color = new Color(0.95f, 0.80f, 0.40f, 1f);

                AddItemNameLabel(newSlot, slotData.item.itemName);

                // ── 우클릭 컨텍스트 메뉴 연결 ──────────────
                var captured = slotData; // 클로저 캡처
                AddRightClickEvent(newSlot, () => OpenContextMenu(captured));
            }
            else
            {
                icon.enabled = false;
                amountText.text = "";
            }
        }
    }

    // ── 컨텍스트 메뉴 ─────────────────────────────

    private void OpenContextMenu(InventorySlot slot)
    {
        var actions = ItemActionProvider.GetActions(slot);
        Vector2 mousePos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();

        ContextMenuUI.Instance?.Show(mousePos, actions, actionType =>
        {
            ExecuteAction(actionType, slot);
        });
    }

    /// <summary>
    /// 액션 실행. 새 ActionType 추가 시 여기에 케이스 추가.
    /// </summary>
    private void ExecuteAction(ActionType actionType, InventorySlot slot)
    {
        switch (actionType)
        {
            case ActionType.Use:
                inventoryManager.UseConsumable(slot);
                break;

            case ActionType.Equip:
                if (slot.item is WeaponData wd)
                    EquipWeapon(wd, slot.slotId);
                break;

            case ActionType.EquipToHotbar:
                EquipToHotbar(slot);
                break;

            case ActionType.Drop:
                DropItem(slot);
                break;
        }

        UpdateUI();
    }

    private void DropItem(InventorySlot slot)
    {
        Debug.Log($"[인벤토리] '{slot.item.itemName}' 버림");
        inventoryManager.RemoveItemById(slot.slotId);
        // TODO: 나중에 월드에 PickupItem 스폰 추가 가능
    }

    // ── 기존 기능 ─────────────────────────────────

    private void AddItemNameLabel(GameObject slot, string itemName)
    {
        Transform existing = slot.transform.Find("ItemName");
        if (existing != null) return;

        GameObject nameObj = new GameObject("ItemName");
        nameObj.transform.SetParent(slot.transform, false);

        RectTransform rt = nameObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0.28f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        TextMeshProUGUI txt = nameObj.AddComponent<TextMeshProUGUI>();
        txt.text = itemName.Length > 8 ? itemName[..8] : itemName;
        txt.fontSize = 8f;
        txt.color = new Color(0.80f, 0.83f, 0.87f, 1f);
        txt.alignment = TextAlignmentOptions.Center;
        txt.overflowMode = TextOverflowModes.Truncate;
    }

    private void EquipWeapon(WeaponData newWeapon, string slotId)
    {
        if (playerController == null) return;

        var hotbar = WeaponHotbarUI.Instance;
        if (hotbar == null) return;

        int targetSlot = hotbar.GetActiveSlot();

        // 1. 인벤토리에서 새 무기 슬롯 정보 미리 가져오기 (Remove 전에!)
        InventorySlot invSlot = inventoryManager.GetSlotById(slotId);
        int savedAmmo = invSlot?.currentAmmo ?? -1;
        int insertIndex = inventoryManager.GetSlotIndex(slotId);

        // 2. 현재 핫바 슬롯 아이템을 인벤토리로 돌려보내기
        ItemData currentItem = hotbar.GetActiveItem();
        if (currentItem is WeaponData currentWeapon)
        {
            int currentAmmo = playerController.GetCurrentAmmo();
            inventoryManager.InsertItemAt(insertIndex, currentWeapon, 1, currentAmmo);
        }

        // 3. 새 무기를 인벤토리에서 제거
        inventoryManager.RemoveItemById(slotId);

        // 4. 핫바에 직접 세팅 (SelectSlot 호출 안 함)
        int finalAmmo = savedAmmo >= 0 ? savedAmmo : newWeapon.maxAmmo;
        hotbar.SetSlotOnly(targetSlot, newWeapon, finalAmmo);

        // 5. 플레이어에 장착
        playerController.SwapWeaponData(newWeapon, finalAmmo);

        Debug.Log($"[인벤토리] '{newWeapon.itemName}' → 핫바 슬롯 {targetSlot + 1}에 장착! (탄수: {finalAmmo})");
        ToggleInventory();
    }

    private void EquipToHotbar(InventorySlot slot)
    {
        var hotbar = WeaponHotbarUI.Instance;
        if (hotbar == null) return;

        int targetSlot = hotbar.GetActiveSlot();

        // 1. 인벤토리 슬롯 정보 미리 가져오기 (Remove 전에!)
        int savedAmmo = slot.currentAmmo;
        int insertIndex = inventoryManager.GetSlotIndex(slot.slotId);

        // 2. 현재 핫바 슬롯 아이템을 인벤토리로 돌려보내기
        ItemData currentItem = hotbar.GetActiveItem();
        if (currentItem != null)
        {
            int currentAmmo = currentItem is WeaponData
                ? playerController.GetCurrentAmmo()
                : hotbar.GetActiveAmmo();
            inventoryManager.InsertItemAt(insertIndex, currentItem, 1, currentAmmo);
        }

        // 3. 새 아이템을 인벤토리에서 제거
        inventoryManager.RemoveItemById(slot.slotId);

        // 4. 핫바에 직접 세팅 (SelectSlot 호출 안 함)
        int finalCount = slot.item is WeaponData wd
            ? (savedAmmo >= 0 ? savedAmmo : wd.maxAmmo)
            : slot.amount;

        hotbar.SetSlotOnly(targetSlot, slot.item, finalCount);

        // 5. 무기면 플레이어에 장착
        if (slot.item is WeaponData wd2)
            playerController.SwapWeaponData(wd2, finalCount);

        Debug.Log($"[인벤토리] '{slot.item.itemName}' 핫바 슬롯 {targetSlot + 1}에 등록! (탄수/개수: {finalCount})");
    }

    private void AddRightClickEvent(GameObject target, System.Action action)
    {
        EventTrigger trigger = target.GetComponent<EventTrigger>() ?? target.AddComponent<EventTrigger>();

        var entry = new EventTrigger.Entry();
        entry.eventID = EventTriggerType.PointerClick;
        entry.callback.AddListener(data =>
        {
            var pointerData = data as UnityEngine.EventSystems.PointerEventData;
            if (pointerData?.button == UnityEngine.EventSystems.PointerEventData.InputButton.Right)
                action?.Invoke();
        });
        trigger.triggers.Add(entry);
    }
}