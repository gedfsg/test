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

    [Tooltip("탄약 칸 아이콘 (순서: P, AR, SG, SR). 칸 프리팹에 'Icon' 이름의 Image가 있으면 적용된다.")]
    public Sprite[] ammoIcons;

    [Header("소지품 개수 표시 (선택)")]
    [Tooltip("'현재 개수 / 최대 칸' 텍스트 (예: 0 / 24). 비워두면 표시하지 않는다.")]
    public TextMeshProUGUI capacityText;

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
        if (AudioManager.Library != null)
            AudioManager.PlayUI(SoundLibrary.Pick(isInventoryOpen ? AudioManager.Library.inventoryOpen : AudioManager.Library.inventoryClose), AudioManager.Vol.UI);

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
            slot.Owner = SlotView.SlotOwner.Inventory; slot.Index = i; // 드래그 앤 드롭용
            Debug.Log($"[생성] i={i}, 형제순번={slot.transform.GetSiblingIndex()}");
            if (i < inventoryManager.inventory.Count)
            {
                InventorySlot slotData = inventoryManager.inventory[i];
                slot.SetItem(slotData.item, slotData.amount, theme);

                var captured = slotData; // 클로저 캡처
                var view = slot;
                slot.RightClicked += _ => OpenContextMenu(captured, view);
            }
            else
            {
                slot.SetEmpty(theme);
            }
        }

        if (capacityText != null)
            capacityText.text = $"{inventoryManager.inventory.Count} / {inventoryManager.maxCapacity}";

        UpdateAmmoPanel();
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)slotGrid);
    }

    // ── 컨텍스트 메뉴 ─────────────────────────────

    private void OpenContextMenu(InventorySlot slot, SlotView view = null)
    {
        var actions = ItemActionProvider.GetActions(slot);
        Vector2 mousePos = Mouse.current.position.ReadValue();

        // 슬롯 오른쪽 옆에 띄워서 아이템을 가리지 않게
        ContextMenuUI.Instance?.Show(mousePos, actions, actionType => ExecuteAction(actionType, slot),
                                     view != null ? (RectTransform)view.transform : null);
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
                else if (slot.item is BackpackData bag) BackpackManager.Instance?.Equip(bag, slot); // 가방 교체
                break;
            case ActionType.EquipToHotbar:
                EquipToHotbar(slot);
                break;
            case ActionType.Drop:
                DropItem(slot);
                break;
            case ActionType.Craft:
                Craft(slot);
                break;
        }

        UpdateUI();
    }

    /// <summary>재료 레시피로 제작: 인벤토리 전체에서 재료 N개를 빼고 결과물 추가. 모자라거나 자리가 없으면 취소</summary>
    private void Craft(InventorySlot slot)
    {
        var recipe = CraftingRecipe.FindByIngredient(slot.item);
        if (recipe == null) return;

        int have = 0;
        foreach (var s in inventoryManager.inventory) if (s.item == recipe.ingredient) have += s.amount;
        if (have < recipe.ingredientCount)
        {
            PickupFeedbackUI.Instance?.ShowWarning($"{recipe.ingredient.itemName} {recipe.ingredientCount}개 필요 (현재 {have}개)");
            return;
        }

        // 재료 차감 (여러 칸에 나뉘어 있어도 됨) → 결과물 추가 (핫바에 같은 아이템이 있으면 거기로 합쳐짐)
        for (int i = 0; i < recipe.ingredientCount; i++) inventoryManager.RemoveItem(recipe.ingredient);
        if (!inventoryManager.AddItem(recipe.result, recipe.resultCount))
        {
            inventoryManager.AddItem(recipe.ingredient, recipe.ingredientCount); // 가방이 꽉 차서 실패 → 재료 돌려줌
            PickupFeedbackUI.Instance?.ShowWarning("가방이 가득 차서 제작할 수 없음");
            return;
        }
        PickupFeedbackUI.Instance?.Show($"{recipe.result.itemName} {recipe.resultCount}개 제작 ({recipe.ingredient.itemName} -{recipe.ingredientCount})");
        Debug.Log($"[제작] {recipe.ingredient.itemName} {recipe.ingredientCount}개 → {recipe.result.itemName} {recipe.resultCount}개");
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

        // 슬롯 타입 제한: 총 → 선택된 총 슬롯(1/2), 소비 아이템 → 같은 아이템 칸 or 빈 소비 칸(3~5)
        int targetSlot = hotbar.FindTargetSlot(slot.item);
        if (targetSlot < 0)
        {
            Debug.Log($"[인벤토리] '{slot.item.itemName}' 핫바 등록 불가 (총은 1,2번 / 소비 아이템은 3~5번 빈 칸만)");
            return; // 인벤토리에서 빼기 전에 막아서 아이템 손실 없음
        }

        int savedAmmo = slot.currentAmmo;
        int insertIndex = inventoryManager.GetSlotIndex(slot.slotId);

        ItemData currentItem = hotbar.GetItem(targetSlot);
        int stackCount = 0; // 같은 소비 아이템이 이미 있으면 개수 합침
        if (currentItem == slot.item && !(slot.item is WeaponData))
        {
            stackCount = hotbar.GetAmmo(targetSlot);
        }
        else if (currentItem != null)
        {
            int currentAmmo = currentItem is WeaponData && targetSlot == hotbar.GetActiveSlot()
                ? playerController.GetCurrentAmmo()
                : hotbar.GetAmmo(targetSlot);
            inventoryManager.InsertItemAt(insertIndex, currentItem, 1, currentAmmo);
        }

        // 총은 그 1자루만 빼고, 소비 아이템은 인벤토리의 같은 종류를 전부 빼서 핫바에 한꺼번에 올림
        // (예전엔 RemoveItemById가 1개만 줄여서 인벤토리에 4개가 남은 채 핫바에도 4개가 표시되던 복제 버그)
        int finalCount;
        if (slot.item is WeaponData wd)
        {
            inventoryManager.RemoveItemById(slot.slotId);
            finalCount = savedAmmo >= 0 ? savedAmmo : wd.maxAmmo;
        }
        else
        {
            finalCount = inventoryManager.TakeAll(slot.item) + stackCount;
        }

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

        for (int i = 0; i < ammoTypes.Length; i++)
            CreateAmmoCell(ammoTypes[i].type, ammoTypes[i].label, i, i == ammoTypes.Length - 1);
    }

    private void CreateAmmoCell(WeaponType type, string typeLabel, int index, bool isLast)
    {
        int current = AmmoInventory.Instance.GetAmmo(type);
        bool hasAmmo = current > 0;

        GameObject cell = Instantiate(ammoCellPrefab, ammoPanelContainer);

        // 선택: 'Icon' Image에 탄약 종류별 아이콘, 마지막 칸은 오른쪽 'Divider' 숨김
        var iconTf = cell.transform.Find("Icon");
        if (iconTf != null && ammoIcons != null && index < ammoIcons.Length && ammoIcons[index] != null)
            iconTf.GetComponent<UnityEngine.UI.Image>().sprite = ammoIcons[index];
        var dividerTf = cell.transform.Find("Divider");
        if (dividerTf != null) dividerTf.gameObject.SetActive(!isLast);

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