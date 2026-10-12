using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 5슬롯 핫바 (PUBG 스타일). G키로 현재 슬롯 버리기, 슬롯 우클릭으로도 버릴 수 있다.
///  - 슬롯 1,2 (index 0,1): 총(WeaponData)만. 1/2 키로 선택해서 들고 다님
///  - 슬롯 3,4,5 (index 2~4): 소비 아이템(ConsumableData: 음식/음료/의료)만. 3~5 키를 누르면 바로 사용
///  - 타입이 안 맞는 아이템은 거부 (SlotTypeFilter.CanPlace)
/// UI 시각 요소는 SlotView 프리팹을 사용하며, 이 스크립트는 데이터/로직만 담당한다.
///
/// 씬 준비물: 이 컴포넌트가 붙은 오브젝트에 slotContainer(가로 배치용 HorizontalLayoutGroup 권장),
/// slotPrefab(SlotView 붙은 프리팹), theme(UITheme 에셋)을 Inspector에서 연결한다.
/// </summary>
public class WeaponHotbarUI : MonoBehaviour
{
    public static WeaponHotbarUI Instance { get; private set; }

    private const int SLOT_COUNT = 5;

    /// <summary>슬롯별 허용 타입. 0,1 = 총 / 2,3,4 = 소비 아이템</summary>
    public static class SlotTypeFilter
    {
        public const int WeaponSlotCount = 2; // 앞 2칸이 총 슬롯

        public static bool IsWeaponSlot(int index) => index >= 0 && index < WeaponSlotCount;

        /// <summary>index 슬롯에 item을 넣을 수 있는지 (null = 비우기라 항상 허용)</summary>
        public static bool CanPlace(int index, ItemData item)
        {
            if (index < 0 || index >= SLOT_COUNT) return false;
            if (item == null) return true;
            return IsWeaponSlot(index) ? item is WeaponData : item is ConsumableData;
        }
    }

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

    /// <summary>슬롯 선택 — 총 슬롯(1,2)은 빈 슬롯도 선택 가능, 아이템 있으면 장착.
    /// 소비 슬롯(3~5)은 선택하지 않고 바로 사용 (들고 있는 총 유지)</summary>
    public void SelectSlot(int index)
    {
        if (index < 0 || index >= SLOT_COUNT) return;
        if (!SlotTypeFilter.IsWeaponSlot(index)) { UseConsumableSlot(index); return; }

        if (activeSlot >= 0 && items[activeSlot] is WeaponData && playerController != null)
            ammos[activeSlot] = playerController.GetCurrentAmmo();

        activeSlot = index;

        if (items[index] is WeaponData wd)
            playerController?.SwapWeaponData(wd, ammos[index]);

        RefreshAllSlots();
    }

    public void EquipSlot(int index) => SelectSlot(index); // 기존 코드 호환용

    /// <summary>소비 슬롯의 아이템 1개 사용 (체력/수분/포만감 적용 후 개수 차감)</summary>
    public void UseConsumableSlot(int index)
    {
        if (!(items[index] is ConsumableData consumable)) return; // 빈 슬롯이면 무시
        if (playerController == null) return;

        playerController.UseConsumable(consumable);
        Debug.Log($"[핫바] 슬롯 {index + 1} '{consumable.itemName}' 사용 (남은 개수 {ammos[index] - 1})");
        ConsumeItem(index);
    }

    /// <summary>item이 들어갈 슬롯 번호. 총 = 현재 선택된 총 슬롯, 소비 = 같은 아이템 칸 → 빈 소비 칸. 없으면 -1</summary>
    public int FindTargetSlot(ItemData item)
    {
        if (item is WeaponData) return SlotTypeFilter.IsWeaponSlot(activeSlot) ? activeSlot : 0;
        if (!(item is ConsumableData)) return -1;
        for (int i = SlotTypeFilter.WeaponSlotCount; i < SLOT_COUNT; i++) if (items[i] == item) return i;
        for (int i = SlotTypeFilter.WeaponSlotCount; i < SLOT_COUNT; i++) if (items[i] == null) return i;
        return -1;
    }

    public ItemData GetItem(int index) => index >= 0 && index < SLOT_COUNT ? items[index] : null;
    public int GetAmmo(int index) => index >= 0 && index < SLOT_COUNT ? ammos[index] : 0;

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
        if (!SlotTypeFilter.CanPlace(index, item)) { LogReject(index, item); return false; }

        items[index] = item;
        ammos[index] = count;
        RefreshSlot(index);

        if (index == activeSlot) SelectSlot(index);
        return true;
    }

    /// <summary>데이터만 세팅, SelectSlot 호출 안 함 (탄수 덮어쓰기 방지). 타입이 안 맞으면 false</summary>
    public bool SetSlotOnly(int index, ItemData item, int count)
    {
        if (index < 0 || index >= SLOT_COUNT) return false;
        if (!SlotTypeFilter.CanPlace(index, item)) { LogReject(index, item); return false; }
        items[index] = item;
        ammos[index] = count;
        RefreshAllSlots();
        return true;
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
        // 슬롯 타입 제한: 허용 안 되는 슬롯은 건너뜀 (투척물 등 어디에도 안 맞으면 거부)
        if (items[activeSlot] == null && SlotTypeFilter.CanPlace(activeSlot, item))
        {
            items[activeSlot] = item;
            ammos[activeSlot] = count;
            RefreshSlot(activeSlot);
            SelectSlot(activeSlot);
            return true;
        }

        for (int i = 0; i < SLOT_COUNT; i++)
        {
            if (items[i] == null && SlotTypeFilter.CanPlace(i, item))
            {
                items[i] = item;
                ammos[i] = count;
                RefreshSlot(i);
                return true;
            }
        }
        LogReject(-1, item);
        return false; // 꽉 참 또는 넣을 수 있는 슬롯 없음
    }

    private static void LogReject(int index, ItemData item)
    {
        string where = index < 0 ? "빈 슬롯 없음" : $"슬롯 {index + 1}은 {(SlotTypeFilter.IsWeaponSlot(index) ? "총" : "소비 아이템")} 전용";
        Debug.Log($"[핫바] '{item?.itemName}' 거부 - {where}");
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