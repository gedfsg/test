using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 5슬롯 무기 핫바. 1~5 키로 슬롯 선택, G키로 현재 슬롯 버리기, 슬롯 우클릭으로도 버릴 수 있다.
/// UI 시각 요소는 SlotView 프리팹을 사용하며, 이 스크립트는 데이터/로직만 담당한다.
///
/// 씬 준비물: 이 컴포넌트가 붙은 오브젝트에 slotContainer(가로 배치용 HorizontalLayoutGroup 권장),
/// slotPrefab(SlotView 붙은 프리팹), theme(UITheme 에셋)을 Inspector에서 연결한다.
/// </summary>
public class WeaponHotbarUI : MonoBehaviour
{
    public static WeaponHotbarUI Instance { get; private set; }

    private const int SLOT_COUNT = 5;

    [Header("Prefab 참조")]
    [SerializeField] private Transform slotContainer;
    [SerializeField] private SlotView slotPrefab;
    [SerializeField] private UITheme theme;

    private ItemData[] items = new ItemData[SLOT_COUNT];
    private int[] ammos = new int[SLOT_COUNT];
    private int activeSlot = 0; // 기본값 0 (항상 선택 가능)

    private SlotView[] slots = new SlotView[SLOT_COUNT];

    private PlayerController playerController;

    void Awake() => Instance = this;

    void Start()
    {
        playerController = FindAnyObjectByType<PlayerController>();
        BuildSlots();
        RefreshAllSlots();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.digit1Key.wasPressedThisFrame) SelectSlot(0);
        else if (kb.digit2Key.wasPressedThisFrame) SelectSlot(1);
        else if (kb.digit3Key.wasPressedThisFrame) SelectSlot(2);
        else if (kb.digit4Key.wasPressedThisFrame) SelectSlot(3);
        else if (kb.digit5Key.wasPressedThisFrame) SelectSlot(4);

        if (kb.gKey.wasPressedThisFrame) DropSlot(activeSlot);
    }

    // ── Public API ────────────────────────────

    /// <summary>슬롯 선택 — 빈 슬롯도 선택 가능, 아이템 있으면 장착</summary>
    public void SelectSlot(int index)
    {
        if (index < 0 || index >= SLOT_COUNT) return;

        if (activeSlot >= 0 && items[activeSlot] is WeaponData && playerController != null)
            ammos[activeSlot] = playerController.GetCurrentAmmo();

        activeSlot = index;

        if (items[index] is WeaponData wd)
            playerController?.SwapWeaponData(wd, ammos[index]);

        RefreshAllSlots();
    }

    public void EquipSlot(int index) => SelectSlot(index); // 기존 코드 호환용

    public bool AddWeapon(WeaponData weapon, int ammo = -1)
        => AddItemToSlot(weapon, ammo >= 0 ? ammo : weapon.maxAmmo);

    public bool AddThrowable(ThrowableData throwable, int count)
    {
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            if (items[i] == throwable)
            {
                ammos[i] += count;
                RefreshSlot(i);
                return true;
            }
        }
        return AddItemToSlot(throwable, count);
    }

    public bool AddConsumable(ConsumableData consumable, int count)
    {
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            if (items[i] == consumable)
            {
                ammos[i] += count;
                RefreshSlot(i);
                return true;
            }
        }
        return AddItemToSlot(consumable, count);
    }

    /// <summary>현재 선택된 슬롯에 아이템 세팅 (Equip to Hotbar용)</summary>
    public bool AddItemToActiveSlot(ItemData item, int count) => SetSlot(activeSlot, item, count);

    public void ConsumeThrowable(int index)
    {
        if (index < 0 || index >= SLOT_COUNT) return;
        if (!(items[index] is ThrowableData)) return;

        ammos[index]--;
        if (ammos[index] <= 0) { items[index] = null; ammos[index] = 0; }
        RefreshAllSlots();
    }

    public void ConsumeItem(int index)
    {
        if (index < 0 || index >= SLOT_COUNT) return;
        ammos[index]--;
        if (ammos[index] <= 0) { items[index] = null; ammos[index] = 0; }
        RefreshAllSlots();
    }

    public ItemData GetActiveItem() => activeSlot >= 0 ? items[activeSlot] : null;
    public int GetActiveSlot() => activeSlot;
    public int GetActiveAmmo() => activeSlot >= 0 ? ammos[activeSlot] : 0;
    public bool IsFull() { for (int i = 0; i < SLOT_COUNT; i++) if (items[i] == null) return false; return true; }

    public bool HasWeapon(WeaponData weapon)
    {
        for (int i = 0; i < SLOT_COUNT; i++)
            if (items[i] == weapon) return true;
        return false;
    }

    public bool SetSlot(int index, ItemData item, int count)
    {
        if (index < 0 || index >= SLOT_COUNT) return false;

        items[index] = item;
        ammos[index] = count;
        RefreshSlot(index);

        if (index == activeSlot) SelectSlot(index);
        return true;
    }

    /// <summary>데이터만 세팅, SelectSlot 호출 안 함 (탄수 덮어쓰기 방지)</summary>
    public void SetSlotOnly(int index, ItemData item, int count)
    {
        if (index < 0 || index >= SLOT_COUNT) return;
        items[index] = item;
        ammos[index] = count;
        RefreshAllSlots();
    }

    /// <summary>슬롯 아이템 버리기 — 월드에 스폰 후 슬롯 비움</summary>
    public void DropSlot(int index)
    {
        if (index < 0 || index >= SLOT_COUNT || items[index] == null) return;

        ItemData dropped = items[index];

        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            Vector3 pos = player.transform.position + player.transform.forward * 1.5f;
            GameObject obj = new GameObject(dropped.itemName + "_Drop");
            obj.transform.position = pos;
            PickupItem pickup = obj.AddComponent<PickupItem>();
            pickup.itemData = dropped;
            pickup.amount = dropped is WeaponData ? 1 : ammos[index];
            BoxCollider col = obj.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = Vector3.one * 0.5f;
        }

        items[index] = null;
        ammos[index] = 0;
        RefreshAllSlots();

        if (index == activeSlot && dropped is WeaponData)
        {
            PlayerController pc = GameObject.FindWithTag("Player")?.GetComponent<PlayerController>();
            pc?.HideCurrentWeapon();
        }

        Debug.Log($"[핫바] '{dropped.itemName}' 버림");
    }

    // ── 내부 헬퍼 ─────────────────────────────

    private bool AddItemToSlot(ItemData item, int count)
    {
        if (items[activeSlot] == null)
        {
            items[activeSlot] = item;
            ammos[activeSlot] = count;
            RefreshSlot(activeSlot);
            SelectSlot(activeSlot);
            return true;
        }

        for (int i = 0; i < SLOT_COUNT; i++)
        {
            if (items[i] == null)
            {
                items[i] = item;
                ammos[i] = count;
                RefreshSlot(i);
                return true;
            }
        }
        return false; // 꽉 참
    }

    // ── UI ────────────────────────────────────

    private void BuildSlots()
    {
        UIUtils.ClearChildren(slotContainer);

        for (int i = 0; i < SLOT_COUNT; i++)
        {
            SlotView slot = Instantiate(slotPrefab, slotContainer);
            int capturedIndex = i;
            slot.RightClicked += _ => DropSlot(capturedIndex);
            slots[i] = slot;
        }
    }

    private void RefreshSlot(int i)
    {
        if (slots == null || i >= slots.Length || slots[i] == null) return;

        bool has = items[i] != null;
        bool isActive = i == activeSlot;
        bool showCount = has && (items[i] is ThrowableData || items[i] is ConsumableData);

        if (has)
            slots[i].SetItem(items[i], showCount ? ammos[i] : 1, theme, isActive);
        else
            slots[i].SetEmpty(theme, isActive);

        slots[i].SetNumber(i + 1, isActive, theme);
    }

    private void RefreshAllSlots() { for (int i = 0; i < SLOT_COUNT; i++) RefreshSlot(i); }
}