using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 인벤토리 창 UI. 슬롯은 SlotView 프리팹을 사용하며(핫바와 동일 컴포넌트),
/// 이 스크립트는 데이터 바인딩/입력 처리만 담당한다.
/// </summary>
public class InventoryUI : MonoBehaviour
{
    [Header("References")]
    public InventoryManager inventoryManager;
    public GameObject inventoryWindow;
    public Transform slotGrid;
    public SlotView slotPrefab;
    public UITheme theme;

    [Header("탄약 패널 (선택)")]
    [Tooltip("비워두면 탄약 패널을 표시하지 않는다.")]
    public Transform ammoPanelContainer;
    public GameObject ammoCellPrefab; // TypeLabel(TMP) + CountLabel(TMP)를 가진 프리팹, 선택사항

    public bool isInventoryOpen = false;

    private PlayerInputActions inputActions;
    private PlayerController playerController;

    void Awake() => inputActions = new PlayerInputActions();

    void Start() => playerController = FindAnyObjectByType<PlayerController>();

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
        Debug.Log($"[InventoryUI] UpdateUI 호출, 아이템 수={inventoryManager.inventory.Count}");
        UIUtils.ClearChildren(slotGrid);

        for (int i = 0; i < inventoryManager.maxCapacity; i++)
        {
            SlotView slot = Instantiate(slotPrefab, slotGrid);
            Debug.Log($"[생성] i={i}, 형제순번={slot.transform.GetSiblingIndex()}");
            if (i < inventoryManager.inventory.Count)
            {
                InventorySlot slotData = inventoryManager.inventory[i];
                slot.SetItem(slotData.item, slotData.amount, theme);

                var captured = slotData; // 클로저 캡처
                slot.RightClicked += _ => OpenContextMenu(captured);
            }
            else
            {
                slot.SetEmpty(theme);
            }
        }

        UpdateAmmoPanel();
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)slotGrid);
    }

    // ── 컨텍스트 메뉴 ─────────────────────────────

    private void OpenContextMenu(InventorySlot slot)
    {
        var actions = ItemActionProvider.GetActions(slot);
        Vector2 mousePos = Mouse.current.position.ReadValue();

        ContextMenuUI.Instance?.Show(mousePos, actions, actionType => ExecuteAction(actionType, slot));
    }

    /// <summary>액션 실행. 새 ActionType 추가 시 여기에 케이스 추가.</summary>
    private void ExecuteAction(ActionType actionType, InventorySlot slot)
    {
        switch (actionType)
        {
            case ActionType.Use:
                inventoryManager.UseConsumable(slot);
                break;
            case ActionType.Equip:
                if (slot.item is WeaponData wd) EquipWeapon(wd, slot.slotId);
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

    // ── 장착 로직 ─────────────────────────────────

    private void EquipWeapon(WeaponData newWeapon, string slotId)
    {
        if (playerController == null) return;

        var hotbar = WeaponHotbarUI.Instance;
        if (hotbar == null) return;

        int targetSlot = hotbar.GetActiveSlot();

        InventorySlot invSlot = inventoryManager.GetSlotById(slotId);
        int savedAmmo = invSlot?.currentAmmo ?? -1;
        int insertIndex = inventoryManager.GetSlotIndex(slotId);

        ItemData currentItem = hotbar.GetActiveItem();
        if (currentItem is WeaponData currentWeapon)
        {
            int currentAmmo = playerController.GetCurrentAmmo();
            inventoryManager.InsertItemAt(insertIndex, currentWeapon, 1, currentAmmo);
        }

        inventoryManager.RemoveItemById(slotId);

        int finalAmmo = savedAmmo >= 0 ? savedAmmo : newWeapon.maxAmmo;
        hotbar.SetSlotOnly(targetSlot, newWeapon, finalAmmo);
        playerController.SwapWeaponData(newWeapon, finalAmmo);

        Debug.Log($"[인벤토리] '{newWeapon.itemName}' → 핫바 슬롯 {targetSlot + 1}에 장착! (탄수: {finalAmmo})");
        ToggleInventory();
    }

    private void EquipToHotbar(InventorySlot slot)
    {
        var hotbar = WeaponHotbarUI.Instance;
        if (hotbar == null) return;

        int targetSlot = hotbar.GetActiveSlot();

        int savedAmmo = slot.currentAmmo;
        int insertIndex = inventoryManager.GetSlotIndex(slot.slotId);

        ItemData currentItem = hotbar.GetActiveItem();
        if (currentItem != null)
        {
            int currentAmmo = currentItem is WeaponData
                ? playerController.GetCurrentAmmo()
                : hotbar.GetActiveAmmo();
            inventoryManager.InsertItemAt(insertIndex, currentItem, 1, currentAmmo);
        }

        inventoryManager.RemoveItemById(slot.slotId);

        int finalCount = slot.item is WeaponData wd
            ? (savedAmmo >= 0 ? savedAmmo : wd.maxAmmo)
            : slot.amount;

        hotbar.SetSlotOnly(targetSlot, slot.item, finalCount);

        if (slot.item is WeaponData wd2)
            playerController.SwapWeaponData(wd2, finalCount);

        Debug.Log($"[인벤토리] '{slot.item.itemName}' 핫바 슬롯 {targetSlot + 1}에 등록! (탄수/개수: {finalCount})");
    }

    // ── 탄약 패널 ─────────────────────────────────

    public void UpdateAmmoPanel()
    {
        if (ammoPanelContainer == null || ammoCellPrefab == null) return;

        UIUtils.ClearChildren(ammoPanelContainer);

        if (AmmoInventory.Instance == null) return;

        var ammoTypes = new (WeaponType type, string label)[]
        {
            (WeaponType.Pistol, "P"),
            (WeaponType.AR, "AR"),
            (WeaponType.Shotgun, "SG"),
            (WeaponType.Sniper, "SR"),
        };

        foreach (var (type, label) in ammoTypes)
            CreateAmmoCell(type, label);
    }

    private void CreateAmmoCell(WeaponType type, string typeLabel)
    {
        int current = AmmoInventory.Instance.GetAmmo(type);
        bool hasAmmo = current > 0;

        GameObject cell = Instantiate(ammoCellPrefab, ammoPanelContainer);

        var texts = cell.GetComponentsInChildren<TextMeshProUGUI>();
        // 프리팹 규약: 자식 TMP 순서 [0]=타입 라벨, [1]=수치
        if (texts.Length > 0)
        {
            texts[0].text = typeLabel;
            texts[0].color = theme != null ? theme.textAmmoLabel : new Color(0.85f, 0.72f, 0.32f, 1f);
        }
        if (texts.Length > 1)
        {
            texts[1].text = current.ToString();
            texts[1].color = hasAmmo
                ? (theme != null ? theme.textAmmoAvailable : Color.white)
                : (theme != null ? theme.textAmmoEmpty : new Color(0.40f, 0.40f, 0.40f, 1f));
            texts[1].fontStyle = hasAmmo ? FontStyles.Bold : FontStyles.Normal;
        }
    }
}