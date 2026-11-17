using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 5슬롯 핫바 (PUBG 스타일). G키로 현재 슬롯 버리기, 슬롯 우클릭으로도 버릴 수 있다.
/// (이전) 슬롯 1,2 = 무기(총·근접) / 3,4,5 = 소비 아이템 (키 누르면 바로 사용)
/// 2026-11-16 재구성:
///  - 슬롯 1 (index 0): 주력 원거리 무기 (소총·샷건·스나이퍼 등) / 슬롯 2 (index 1): 권총·보조 원거리 무기
///  - 슬롯 3 (index 2): 투척 무기 전용 (수류탄·화염병). 선택하면 손에 쥐고 클릭으로 던짐 (ThrowableController)
///  - 슬롯 4,5 (index 3,4): 자유 — 근접 무기(선택하면 듦, 시작 도끼 = 4번) 또는 소비 아이템(키 누르면 바로 사용)
///  - Ctrl+마우스 휠: 1·2·3만 순환 (아래 = 다음 1→2→3→1, 위 = 이전). 4·5는 숫자 키로만. 휠만 굴리면 카메라 줌 (2026-11-17 피드백: 휠 하나에 겹쳐서 무기 교체를 Ctrl+휠로)
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

    /// <summary>슬롯별 허용 타입. 0,1 = 원거리 무기 / 2 = 투척물 / 3,4 = 근접 무기·소비 아이템 (2026-11-16)</summary>
    public static class SlotTypeFilter
    {
        public const int WeaponSlotCount = 2; // 앞 2칸이 총 슬롯
        public const int ThrowSlot = 2;       // 3번 칸 = 투척 전용
        public const int WheelSlotCount = 3;  // 휠로 순환하는 칸 (1·2·3)

        public static bool IsWeaponSlot(int index) => index >= 0 && index < WeaponSlotCount;
        public static bool IsThrowSlot(int index) => index == ThrowSlot;
        public static bool IsFreeSlot(int index) => index > ThrowSlot && index < SLOT_COUNT;

        /// <summary>index 슬롯에 item을 넣을 수 있는지 (null = 비우기라 항상 허용)</summary>
        public static bool CanPlace(int index, ItemData item)
        {
            if (index < 0 || index >= SLOT_COUNT) return false;
            if (item == null) return true;
            // return IsWeaponSlot(index) ? item is WeaponData : item is ConsumableData; // (이전)
            if (IsWeaponSlot(index)) return item is WeaponData w && w.type != WeaponType.Melee;
            if (IsThrowSlot(index)) return item is ThrowableData;
            return (item is WeaponData) || (item is ConsumableData);
        }

        /// <summary>못 넣을 때 토스트 문구</summary>
        public static string RejectMessage(int index) =>
            IsWeaponSlot(index) ? "1, 2번은 원거리 무기 전용" : IsThrowSlot(index) ? "투척물만 장착 가능" : "4, 5번은 근접 무기·소비 아이템 전용";
    }

    [Header("Prefab 참조")]
    [SerializeField] private Transform slotContainer;
    [SerializeField] private SlotView slotPrefab;
    [SerializeField] private UITheme theme;

    private ItemData[] items = new ItemData[SLOT_COUNT];
    private int[] ammos = new int[SLOT_COUNT];
    private int activeSlot = 0; // 기본값 0 (항상 선택 가능)

    private SlotView[] slots = new SlotView[SLOT_COUNT];
    private UnityEngine.UI.Image[] weaponHints = new UnityEngine.UI.Image[SLOT_COUNT]; // 1,2번 빈 칸의 흐린 총 아이콘
    [Tooltip("1,2번 빈 무기 칸에 흐린 총 실루엣 표시. 아이템이 있는 것처럼 보여서(유령 아이콘) 기본 끔")]
    [SerializeField] private bool showWeaponHint = false;

    private PlayerController playerController;
    private int lastGunSlot = 0; // 마지막으로 든 총 칸 (투척물 다 쓰면 여기로 돌아감)

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
        if (playerController != null && playerController.IsRolling) return; // 구르는 중엔 무기 교체/버리기 막음
        if (MenuUI.GameplayBlocked) return; // 일시정지·생존 종료·인벤토리 열림 중엔 1~5/G 무시

        if (kb.digit1Key.wasPressedThisFrame) SelectSlot(0);
        else if (kb.digit2Key.wasPressedThisFrame) SelectSlot(1);
        else if (kb.digit3Key.wasPressedThisFrame) SelectSlot(2);
        else if (kb.digit4Key.wasPressedThisFrame) SelectSlot(3);
        else if (kb.digit5Key.wasPressedThisFrame) SelectSlot(4);

        // Ctrl+마우스 휠: 1·2·3만 순환. (2026-11-16엔 휠만 = 교체, Ctrl+휠 = 줌 → 11-17 피드백으로 반대로) 휠만 굴리면 CameraZoom이 줌
        var mouse = Mouse.current;
        if (mouse != null && kb.ctrlKey.isPressed)
        {
            float wheel = mouse.scroll.ReadValue().y;
            if (wheel != 0f && !(playerController != null && playerController.IsThrowAiming))
            {
                int n = SlotTypeFilter.WheelSlotCount;
                // 4·5번을 들고 있을 때: 아래 = 1번부터, 위 = 3번부터
                int cur = activeSlot < n ? activeSlot : (wheel < 0f ? -1 : n);
                int next = wheel < 0f ? (cur + 1) % n : (cur - 1 + n) % n; // 아래 = 다음, 위 = 이전
                SelectSlot(next);
            }
        }

        if (kb.gKey.wasPressedThisFrame) DropSlot(activeSlot);
    }

    // ── Public API ────────────────────────────

    /// <summary>슬롯 선택 — 총 슬롯(1,2)은 빈 슬롯도 선택 가능, 아이템 있으면 장착.
    /// 소비 슬롯(3~5)은 선택하지 않고 바로 사용 (들고 있는 총 유지)</summary>
    public void SelectSlot(int index)
    {
        if (index < 0 || index >= SLOT_COUNT) return;
        // if (!SlotTypeFilter.IsWeaponSlot(index)) { UseConsumableSlot(index); return; } // (이전) 3~5 = 바로 사용
        // 4·5번: 소비 아이템이면 바로 사용 (들고 있는 것 유지), 근접 무기면 아래에서 듦, 비어 있으면 무시
        if (SlotTypeFilter.IsFreeSlot(index) && !(items[index] is WeaponData))
        {
            if (items[index] is ConsumableData) UseConsumableSlot(index);
            return;
        }

        // 들고 있던 총의 남은 탄수 저장 (근접 무기는 탄수 없음)
        if (activeSlot >= 0 && items[activeSlot] is WeaponData cur && cur.type != WeaponType.Melee && playerController != null)
            ammos[activeSlot] = playerController.GetCurrentAmmo();

        int prevSlot = activeSlot;
        activeSlot = index;

        if (items[index] is WeaponData wd)
        {
            playerController?.SwapWeaponData(wd, ammos[index]);
            if (prevSlot != index) playerController?.PlayWeaponSwitch(); // 1↔2 전환 모션
            if (SlotTypeFilter.IsWeaponSlot(index)) lastGunSlot = index;
        }
        else if (SlotTypeFilter.IsThrowSlot(index))
        {
            // 투척물 쥐기: 총/도끼 모델 숨김 (손에 쥔 투척물은 ThrowableController가 붙임)
            playerController?.HideCurrentWeapon();
            if (prevSlot != index && items[index] != null) playerController?.PlayWeaponSwitch();
        }

        if (prevSlot != index)
        {
            // 전환 피드백: 선택 칸 튀는 애니메이션 + 장비 교체 소리
            if (slots[index] != null) StartCoroutine(PopSlot(slots[index].transform));
            if (AudioManager.Library != null) AudioManager.PlayUI(SoundLibrary.Pick(AudioManager.Library.pickup), AudioManager.Vol.UI);
        }

        RefreshAllSlots();
    }

    System.Collections.IEnumerator PopSlot(Transform t)
    {
        const float dur = 0.15f;
        for (float e = 0f; e < dur; e += Time.unscaledDeltaTime)
        {
            t.localScale = Vector3.one * (1f + 0.15f * Mathf.Sin(e / dur * Mathf.PI)); // 1 → 1.15 → 1
            yield return null;
        }
        t.localScale = Vector3.one;
    }

    /// <summary>이 아이템이 들어 있는 칸 (없으면 -1)</summary>
    public int IndexOf(ItemData item)
    {
        for (int i = 0; i < SLOT_COUNT; i++) if (items[i] == item) return i;
        return -1;
    }

    public void EquipSlot(int index) => SelectSlot(index); // 기존 코드 호환용

    /// <summary>소비 슬롯의 아이템 1개 사용 (체력/수분/포만감 적용 후 개수 차감)</summary>
    public void UseConsumableSlot(int index)
    {
        if (!(items[index] is ConsumableData consumable)) return; // 빈 슬롯이면 무시
        if (playerController == null) return;

        // playerController.UseConsumable(consumable);   // (이전) 즉시 사용
        // ConsumeItem(index);
        // 2026-11-16: 사용 시간(진행 바) 뒤에 효과 + 차감. 맞으면 취소되고 아이템은 그대로
        playerController.BeginConsumable(consumable, () =>
        {
            ConsumeItem(index, consumable);
            Debug.Log($"[핫바] 슬롯 {index + 1} '{consumable.itemName}' 사용 (남은 개수 {ammos[index]})");
        });
    }

    /// <summary>item이 들어갈 슬롯 번호. 총 = 현재 선택된 총 슬롯, 소비 = 같은 아이템 칸 → 빈 소비 칸. 없으면 -1</summary>
    public int FindTargetSlot(ItemData item)
    {
        // (이전) 무기 = 선택된 총 칸 또는 0 / 소비 = 3~5번
        if (item == null) return -1;
        if (item is ThrowableData) return SlotTypeFilter.ThrowSlot;
        if (item is WeaponData w && w.type != WeaponType.Melee)
            return SlotTypeFilter.IsWeaponSlot(activeSlot) ? activeSlot : (items[0] == null ? 0 : items[1] == null ? 1 : 0);
        for (int i = 0; i < SLOT_COUNT; i++) if (items[i] == item && SlotTypeFilter.CanPlace(i, item)) return i;
        for (int i = SlotTypeFilter.ThrowSlot + 1; i < SLOT_COUNT; i++) if (items[i] == null && SlotTypeFilter.CanPlace(i, item)) return i;
        return -1;
    }

    public ItemData GetItem(int index) => index >= 0 && index < SLOT_COUNT ? items[index] : null;

    // ── 드래그 앤 드롭용 API (SlotDragDrop) ──

    /// <summary>들고 있는 총의 현재 탄수를 슬롯 데이터에 저장 (슬롯 내용 옮기기 전에 호출)</summary>
    void SaveActiveAmmo()
    {
        if (items[activeSlot] is WeaponData cur && cur.type != WeaponType.Melee && playerController != null)
            ammos[activeSlot] = playerController.GetCurrentAmmo();
    }

    /// <summary>선택된 슬롯 내용이 바뀌었을 때 손에 든 무기 갱신</summary>
    void ReequipActive()
    {
        if (playerController == null) return;
        if (items[activeSlot] is WeaponData wd) playerController.SwapWeaponData(wd, ammos[activeSlot]);
        else playerController.HideCurrentWeapon();
    }

    /// <summary>핫바 두 칸 교환 (타입이 안 맞으면 false)</summary>
    public bool SwapSlots(int a, int b)
    {
        if (a == b || !SlotTypeFilter.CanPlace(a, items[b]) || !SlotTypeFilter.CanPlace(b, items[a])) return false;
        SaveActiveAmmo();
        (items[a], items[b]) = (items[b], items[a]);
        (ammos[a], ammos[b]) = (ammos[b], ammos[a]);
        if (a == activeSlot || b == activeSlot) ReequipActive();
        RefreshAllSlots();
        return true;
    }

    /// <summary>핫바 칸 비우고 (아이템, 탄수/개수) 반환</summary>
    public (ItemData item, int count) TakeSlot(int index)
    {
        if (index < 0 || index >= SLOT_COUNT || items[index] == null) return (null, 0);
        SaveActiveAmmo();
        var r = (items[index], ammos[index]);
        items[index] = null; ammos[index] = 0;
        if (index == activeSlot) ReequipActive();
        RefreshAllSlots();
        return r;
    }

    /// <summary>핫바 칸에 아이템 넣기 (선택된 칸이면 바로 손에 듦). 타입 안 맞으면 false</summary>
    public bool PutSlot(int index, ItemData item, int count)
    {
        if (!SlotTypeFilter.CanPlace(index, item)) { LogReject(index, item); return false; }
        if (index == activeSlot) SaveActiveAmmo();
        items[index] = item; ammos[index] = count;
        if (index == activeSlot) ReequipActive();
        RefreshAllSlots();
        return true;
    }

    /// <summary>들고 있는 총이면 현재 탄수, 아니면 저장된 값</summary>
    public int GetLiveAmmo(int index)
    {
        if (index == activeSlot && items[index] is WeaponData w && w.type != WeaponType.Melee && playerController != null)
            return playerController.GetCurrentAmmo();
        return GetAmmo(index);
    }
    public int GetAmmo(int index) => index >= 0 && index < SLOT_COUNT ? ammos[index] : 0;

    public bool AddWeapon(WeaponData weapon, int ammo = -1)
        => AddItemToSlot(weapon, ammo >= 0 ? ammo : weapon.maxAmmo);

    /// <summary>투척물 줍기 (2026-11-16): 3번 칸이 같은 것이면 개수 합침, 비어 있으면 넣음. 다른 투척물이 있으면 false (가방으로)</summary>
    public bool TryAddThrowable(ThrowableData throwable, int count)
    {
        int t = SlotTypeFilter.ThrowSlot;
        if (items[t] == throwable) { ammos[t] += count; RefreshSlot(t); return true; }
        if (items[t] != null) return false;
        items[t] = throwable; ammos[t] = count;
        RefreshAllSlots();
        return true;
    }

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

    /// <summary>소비 칸(3~5)에 같은 아이템이 이미 있으면 개수만 더하고 true. 없으면 false (빈 칸에 새로 넣지 않음)</summary>
    public bool TryStackExisting(ItemData item, int count)
    {
        if (item == null || count <= 0) return false;
        for (int i = SlotTypeFilter.WeaponSlotCount; i < SLOT_COUNT; i++)
        {
            if (items[i] != item) continue;
            ammos[i] += count;
            RefreshSlot(i);
            return true;
        }
        return false;
    }

    /// <summary>현재 선택된 슬롯에 아이템 세팅 (Equip to Hotbar용)</summary>
    public bool AddItemToActiveSlot(ItemData item, int count) => SetSlot(activeSlot, item, count);

    public void ConsumeThrowable(int index)
    {
        if (index < 0 || index >= SLOT_COUNT) return;
        if (!(items[index] is ThrowableData)) return;

        ammos[index]--;
        if (ammos[index] <= 0)
        {
            items[index] = null; ammos[index] = 0;
            // 다 던졌으면 마지막 총 칸(1 또는 2)으로 자동 전환 (2026-11-16)
            if (index == activeSlot) { RefreshAllSlots(); SelectSlot(lastGunSlot); return; }
        }
        RefreshAllSlots();
    }

    /// <summary>
    /// 사용이 끝난 시점에 차감 (2026-11-16). 사용 중 슬롯을 옮겼을 수 있어서 index 칸이 그 아이템이 아니면 같은 아이템 칸을 찾아 차감
    /// </summary>
    public void ConsumeItem(int index, ItemData expected)
    {
        if (index < 0 || index >= SLOT_COUNT || items[index] != expected)
        {
            index = -1;
            for (int i = 0; i < SLOT_COUNT; i++) if (items[i] == expected) { index = i; break; }
            if (index < 0) return; // 버렸거나 옮겨서 핫바에 없음 → 효과만 적용됨
        }
        ConsumeItem(index);
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
        // string where = index < 0 ? "빈 슬롯 없음" : $"슬롯 {index + 1}은 {(SlotTypeFilter.IsWeaponSlot(index) ? "무기" : "소비 아이템")} 전용"; // (이전)
        string where = index < 0 ? "빈 슬롯 없음" : SlotTypeFilter.RejectMessage(index);
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
            slot.Owner = SlotView.SlotOwner.Hotbar; slot.Index = i; // 드래그 앤 드롭용
            slots[i] = slot;
            // (이전) 1·2번 "무기" 라벨 + 빨강 테두리 / 3번 "투척" 라벨 + 주황 테두리 + 수류탄 자리 표시 + "비어 있음"
            // if (SlotTypeFilter.IsWeaponSlot(i)) DecorateWeaponSlot(slot, i);
            // else if (SlotTypeFilter.IsThrowSlot(i)) DecorateThrowSlot(slot);
            ApplyMinimalStyle(slot, i); // 2026-11-17 핫바 재작업: 모든 칸 같은 모양, 테두리·라벨 없음
        }
    }

    // ── 2026-11-17 핫바 재작업 ──
    //  - 5칸 모두 가로로 긴 직사각형 110x70, 배경 반투명 검정(0,0,0,0.55) 둥근 모서리 약 6px
    //  - 테두리 없음 (기본 Frame·선택 ActiveBar·등급 테두리 전부 안 그림. 등급은 인벤토리·바닥 아이템에서만)
    //  - 라벨 없음, 숫자 1~5만 좌상단 작게 (흰, 알파 0.6)
    //  - 선택 칸: 배경을 흰 반투명(1,1,1,0.18)으로 밝게 + 아래 2px 흰 밑줄
    static readonly Vector2 SlotSize = new Vector2(110f, 70f);
    static readonly Color SlotBg = new Color(0f, 0f, 0f, 0.55f);
    static readonly Color SlotBgActive = new Color(1f, 1f, 1f, 0.18f);
    static readonly Color SlotNumberColor = new Color(1f, 1f, 1f, 0.6f);
    private readonly UnityEngine.UI.Image[] underlines = new UnityEngine.UI.Image[SLOT_COUNT];

    private void ApplyMinimalStyle(SlotView slot, int i)
    {
        var rt = (RectTransform)slot.transform;
        rt.sizeDelta = SlotSize;
        var bg = slot.GetComponent<UnityEngine.UI.Image>();
        if (bg != null) { bg.pixelsPerUnitMultiplier = 2.7f; bg.color = SlotBg; } // RoundedRect 모서리(16px) / 2.7 ≈ 6px
        var frame = slot.transform.Find("Frame");
        if (frame != null) frame.gameObject.SetActive(false);
        var bar = slot.transform.Find("ActiveBar");
        if (bar != null) bar.gameObject.SetActive(false);

        // 숫자: 좌상단 작게
        var num = slot.transform.Find("NumberLabel") as RectTransform;
        if (num != null)
        {
            num.anchorMin = num.anchorMax = new Vector2(0f, 1f); num.pivot = new Vector2(0f, 1f);
            num.anchoredPosition = new Vector2(6f, -3f); num.sizeDelta = new Vector2(20f, 16f);
            var t = num.GetComponent<TMPro.TextMeshProUGUI>();
            if (t != null) { t.fontSize = 13f; t.alignment = TMPro.TextAlignmentOptions.TopLeft; t.raycastTarget = false; }
        }
        // 아이콘: 직사각형 칸 안에 여백 두고 (가로로 긴 총이 꽉 차게)
        var icon = slot.transform.Find("Icon") as RectTransform;
        if (icon != null) { icon.offsetMin = new Vector2(12f, 8f); icon.offsetMax = new Vector2(-12f, -10f); }
        // 개수(x3): 우하단
        var count = slot.transform.Find("CountLabel") as RectTransform;
        if (count != null)
        {
            count.anchorMin = count.anchorMax = new Vector2(1f, 0f); count.pivot = new Vector2(1f, 0f);
            count.anchoredPosition = new Vector2(-6f, 4f);
        }

        // 선택 밑줄 (아래 2px 흰색)
        var ul = new GameObject("ActiveUnderline", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        var ur = (RectTransform)ul.transform;
        ur.SetParent(slot.transform, false);
        ur.anchorMin = new Vector2(0f, 0f); ur.anchorMax = new Vector2(1f, 0f); ur.pivot = new Vector2(0.5f, 0f);
        ur.offsetMin = new Vector2(6f, 0f); ur.offsetMax = new Vector2(-6f, 2f);
        underlines[i] = ul.GetComponent<UnityEngine.UI.Image>();
        underlines[i].color = Color.white;
        underlines[i].raycastTarget = false;
        underlines[i].enabled = false;
    }

    // SlotView.SetItem/SetEmpty/SetNumber가 테마 색으로 덮어쓰므로 매번 마지막에 다시 칠함
    private void RestyleSlot(int i, bool isActive)
    {
        var slot = slots[i];
        var bg = slot.GetComponent<UnityEngine.UI.Image>();
        if (bg != null) bg.color = isActive ? SlotBgActive : SlotBg;
        if (underlines[i] != null) underlines[i].enabled = isActive;
        var bar = slot.transform.Find("ActiveBar");
        if (bar != null && bar.gameObject.activeSelf) bar.gameObject.SetActive(false); // SetActiveBar가 다시 켬
        var num = slot.transform.Find("NumberLabel")?.GetComponent<TMPro.TextMeshProUGUI>();
        if (num != null) num.color = SlotNumberColor;
    }

    // ── 3번 투척 칸 (2026-11-16): 주황 테두리 + "투척" 라벨 + 빈 칸 흐린 투척물 아이콘 + 아래 "수류탄 x3" / "비어 있음" ──
    static readonly Color ThrowFrameColor = new Color(0.72f, 0.42f, 0.10f, 1f);
    static readonly Color ThrowLabelColor = new Color(1f, 0.70f, 0.30f, 1f);
    UnityEngine.UI.Image throwPlaceholder;
    TMPro.TextMeshProUGUI throwInfo;
    bool throwHover;

    private void DecorateThrowSlot(SlotView slot)
    {
        var frame = slot.transform.Find("Frame")?.GetComponent<UnityEngine.UI.Image>();
        if (frame != null)
        {
            if (HudStyle.RoundedFrameThick != null) frame.sprite = HudStyle.RoundedFrameThick;
            frame.color = ThrowFrameColor;
        }
        var label = HudStyle.NewText(slot.transform, "ThrowLabel", "투척", HudStyle.SemiBold, 13f, ThrowLabelColor,
                                     TMPro.TextAlignmentOptions.Center, Vector2.zero, new Vector2(80f, 18f));
        var lr = label.rectTransform;
        lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 1f); lr.pivot = new Vector2(0.5f, 0f);
        lr.anchoredPosition = new Vector2(0f, 4f);
        label.raycastTarget = false;

        // 빈 칸 자리 표시: 수류탄 아이콘 흐리게 (Resources/Throwables/icon_Grenade - ThrowableBuilder가 만듦)
        var ph = new GameObject("ThrowPlaceholder", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        var pr = (RectTransform)ph.transform;
        pr.SetParent(slot.transform, false); pr.SetSiblingIndex(0);
        pr.anchorMin = new Vector2(0.15f, 0.15f); pr.anchorMax = new Vector2(0.85f, 0.85f); pr.offsetMin = pr.offsetMax = Vector2.zero;
        throwPlaceholder = ph.GetComponent<UnityEngine.UI.Image>();
        throwPlaceholder.sprite = Resources.Load<Sprite>("Throwables/icon_Grenade");
        throwPlaceholder.preserveAspect = true; throwPlaceholder.raycastTarget = false;
        throwPlaceholder.color = new Color(1f, 1f, 1f, 0.18f);
        if (throwPlaceholder.sprite == null) throwPlaceholder.enabled = false;

        // 칸 아래: 이름 x 개수 / 비어 있음 / (마우스 올리면) 투척 무기 전용
        throwInfo = HudStyle.NewText(slot.transform, "ThrowInfo", "비어 있음", HudStyle.SemiBold, 12f, HudStyle.TextDim,
                                     TMPro.TextAlignmentOptions.Center, Vector2.zero, new Vector2(120f, 16f));
        var ir = throwInfo.rectTransform;
        ir.anchorMin = ir.anchorMax = new Vector2(0.5f, 0f); ir.pivot = new Vector2(0.5f, 1f);
        ir.anchoredPosition = new Vector2(0f, -3f);
        throwInfo.raycastTarget = false;

        var hover = slot.gameObject.AddComponent<HoverRelay>();
        hover.onChange = on => { throwHover = on; RefreshSlot(SlotTypeFilter.ThrowSlot); };
    }

    /// <summary>마우스 올림/내림 알림 (3번 칸 "투척 무기 전용" 안내)</summary>
    class HoverRelay : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        public System.Action<bool> onChange;
        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) => onChange?.Invoke(true);
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) => onChange?.Invoke(false);
    }

    // 1,2번 무기 전용 칸 표시: 두꺼운 어두운 빨강 테두리 + 위에 "무기" 라벨 + 빈 칸엔 흐린 총 아이콘
    static readonly Color WeaponFrameColor = new Color(0.62f, 0.15f, 0.12f, 1f);
    static readonly Color WeaponLabelColor = new Color(0.95f, 0.42f, 0.36f, 1f);

    private void DecorateWeaponSlot(SlotView slot, int i)
    {
        var frame = slot.transform.Find("Frame")?.GetComponent<UnityEngine.UI.Image>();
        if (frame != null)
        {
            if (HudStyle.RoundedFrameThick != null) frame.sprite = HudStyle.RoundedFrameThick;
            frame.color = WeaponFrameColor;
        }

        var label = HudStyle.NewText(slot.transform, "WeaponLabel", "무기", HudStyle.SemiBold, 13f, WeaponLabelColor,
                                     TMPro.TextAlignmentOptions.Center, Vector2.zero, new Vector2(80f, 18f));
        var lr = label.rectTransform;
        lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 1f); lr.pivot = new Vector2(0.5f, 0f);
        lr.anchoredPosition = new Vector2(0f, 4f); // 칸 바로 위
        label.raycastTarget = false;

        var hintSprite = showWeaponHint ? Resources.Load<Sprite>("HUD/Sprites/WeaponSilhouette") : null;
        if (hintSprite != null)
        {
            var hint = new GameObject("WeaponHint", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            var hr = (RectTransform)hint.transform;
            hr.SetParent(slot.transform, false);
            hr.SetSiblingIndex(0); // 아이콘/테두리보다 뒤
            hr.anchorMin = new Vector2(0.1f, 0.25f); hr.anchorMax = new Vector2(0.9f, 0.75f); hr.offsetMin = hr.offsetMax = Vector2.zero;
            var img = hint.GetComponent<UnityEngine.UI.Image>();
            img.sprite = hintSprite; img.preserveAspect = true; img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, 0.16f);
            weaponHints[i] = img;
        }
    }

    private void RefreshSlot(int i)
    {
        if (slots == null || i >= slots.Length || slots[i] == null) return;

        bool has = items[i] != null;
        if (weaponHints[i] != null) weaponHints[i].enabled = !has;
        bool isActive = i == activeSlot;
        bool showCount = has && (items[i] is ThrowableData || items[i] is ConsumableData);

        if (has)
            slots[i].SetItem(items[i], showCount ? ammos[i] : 1, theme, isActive);
        else
            slots[i].SetEmpty(theme, isActive);

        slots[i].SetNumber(i + 1, isActive, theme);
        // UpdateRarityFrame(i); // (이전 2026-11-12~16) 핫바 안 등급 테두리 → 2026-11-17 핫바에서는 안 그림
        RestyleSlot(i, isActive);

        if (SlotTypeFilter.IsThrowSlot(i))
        {
            if (throwPlaceholder != null && throwPlaceholder.sprite != null) throwPlaceholder.enabled = !has;
            if (throwInfo != null)
            {
                throwInfo.text = throwHover ? "투척 무기 전용" : has ? $"{items[i].itemName} x{ammos[i]}" : "비어 있음";
                throwInfo.color = throwHover ? ThrowLabelColor : has ? HudStyle.Text : HudStyle.Inactive;
            }
        }
    }

    // 무기 등급 테두리: 칸 안쪽(4px 안)에 등급 색 테두리. 바깥 테두리는 선택 표시(노랑)/무기 칸(빨강)이 쓰고 있어서
    // 같은 자리에 칠하면 선택된 무기의 등급이 가려짐 → 안쪽에 따로 그려 둘 다 보이게
    // 2026-11-17 피드백 "슬롯 테두리와 등급 테두리가 겹침":
    //  - 무기가 들어 있는 칸: 기본 테두리(Frame, 회색/무기 칸 빨강)는 숨기고 등급 테두리만 그 자리(바깥)에 굵게
    //  - 선택된 칸: 노란 선택 테두리(ActiveBar)가 바깥을 쓰므로 등급 테두리는 안쪽(5px)에 얇게 → 색·굵기가 겹치지 않음
    //  - 빈 칸 / 무기 아닌 칸: 기존 테두리 그대로
    const float RarityFrameInset = 4f;     // (이전) 항상 이 안쪽 위치
    const float RarityInsetActive = 5f;
    private readonly UnityEngine.UI.Image[] rarityFrames = new UnityEngine.UI.Image[SLOT_COUNT];

    private void UpdateRarityFrame(int i)
    {
        var wd = items[i] as WeaponData;
        if (rarityFrames[i] == null)
        {
            if (wd == null) return; // 무기가 처음 들어올 때 만듦
            var go = new GameObject("RarityFrame", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(slots[i].transform, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.one * RarityFrameInset; rt.offsetMax = -Vector2.one * RarityFrameInset;
            var frame = slots[i].transform.Find("Frame");
            if (frame != null) rt.SetSiblingIndex(frame.GetSiblingIndex() + 1); // 아이콘 위, 선택 표시/번호 아래
            var img = go.GetComponent<UnityEngine.UI.Image>();
            img.sprite = HudStyle.RoundedFrameThick != null ? HudStyle.RoundedFrameThick : HudStyle.RoundedFrame;
            img.type = UnityEngine.UI.Image.Type.Sliced;
            img.raycastTarget = false;
            rarityFrames[i] = img;
        }
        rarityFrames[i].enabled = wd != null;
        if (wd != null) rarityFrames[i].color = RarityColors.Get(wd.rarity);

        // 기본 테두리 숨김 + 선택 여부에 따라 등급 테두리 위치/굵기
        var baseFrame = slots[i].transform.Find("Frame")?.GetComponent<UnityEngine.UI.Image>();
        if (baseFrame != null) baseFrame.enabled = wd == null;
        if (wd != null)
        {
            bool active = i == activeSlot;
            var rt = rarityFrames[i].rectTransform;
            float inset = active ? RarityInsetActive : 0f;
            rt.offsetMin = Vector2.one * inset; rt.offsetMax = -Vector2.one * inset;
            rarityFrames[i].sprite = active ? HudStyle.RoundedFrame : (HudStyle.RoundedFrameThick != null ? HudStyle.RoundedFrameThick : HudStyle.RoundedFrame);
        }
    }

    private void RefreshAllSlots() { for (int i = 0; i < SLOT_COUNT; i++) RefreshSlot(i); }
}