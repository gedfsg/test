using UnityEngine;

/// <summary>
/// 가방 + 등 캐리 비주얼 (플레이어에 자동으로 붙음 - PlayerController.Start)
///  - 가방은 1종(24칸). 시작 시 없음(10칸). 바닥의 가방을 F로 주우면 바로 등에 멤 (TryPickup)
///    이미 메고 있으면 무시, 벗을 수 없음
///  - 장착 시: 인벤토리 칸 수 변경 + 인벤토리 창 크기 조정 + 등(가슴 본)에 가방 모델
///  - 등 무기: 핫바 1,2 중 "손에 안 든" 무기를 등에 멤. 가방 있으면 가방 위, 없으면 등 가운데.
///    슬롯 1↔2 전환하면 손↔등 즉시 교체 (애니메이션 없음)
/// 장착점(BackpackSlot / WeaponHolsterSlot)은 시작할 때 캐릭터 기준 위치로 한 번 계산해서 가슴 본에 붙여둠
/// → 이후 애니메이션에 따라 몸과 같이 움직임.
/// </summary>
public class BackpackManager : MonoBehaviour
{
    public static BackpackManager Instance { get; private set; }

    [Header("가방")]
    [Tooltip("가방 없을 때 인벤토리 칸 수")]
    public int noBackpackCapacity = 10;
    [Tooltip("시작 시 지급할 가방 (Resources 경로). 비우면 가방 없이 시작")]
    public string startingBackpackResource = "";

    [Header("장착점 (캐릭터 기준, m)")]
    public string chestBoneName = "spine.002";
    [Tooltip("장착점: 가슴 본에서 뒤로 이만큼 (등 표면이 본에서 약 0.15m 뒤 - 메시 실측)")]
    public float backOffset = 0.12f;
    [Tooltip("가방 모델을 몸 쪽으로 당기는 양 (가방 높이 배수). 프리팹 원점이 어깨끈 고리 끝이라 몸체가 뒤로 떠 있음 → 실측 0.7")]
    public float bagForwardShift = 0.7f;
    [Tooltip("가방 없을 때 등에 멘 총의 뒤쪽 거리")]
    public float holsterNoBagOffset = 0.2f;

    public BackpackData Current { get; private set; }

    Transform backpackSlot, holsterSlot, bagVisual, holsterVisual;
    InventoryManager inventory;
    InventoryUI inventoryUI;
    int shownHolsterSlot = -1; ItemData shownHolsterItem;

    void Awake() => Instance = this;

    void Start()
    {
        inventory = GetComponent<InventoryManager>();
        inventoryUI = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        // 장착점은 애니메이션 포즈가 적용된 뒤(첫 LateUpdate)에 만듦 - Start 시점은 기본 자세라 본 방향이 다름

        var start = string.IsNullOrEmpty(startingBackpackResource) ? null : Resources.Load<BackpackData>(startingBackpackResource);
        if (start != null) Equip(start, null);
        else SetCapacity(noBackpackCapacity);
    }

    void CreateSlots()
    {
        Transform bone = FindDeep(transform, chestBoneName) ?? transform;
        // 상체 기준 회전(정면 = +Z)으로 만들어 본에 붙임 → 이후 로컬 좌표가 상체 기준과 같음
        backpackSlot = new GameObject("BackpackSlot").transform;
        // 상체 정면 = 가슴 본의 up축 (메시 실측으로 확인. 조준 자세에서 루트 정면보다 ~54° 돌아가 있음)
        Vector3 torsoFwd = bone != transform ? Vector3.ProjectOnPlane(bone.up, Vector3.up).normalized : transform.forward;
        if (torsoFwd.sqrMagnitude < 0.01f) torsoFwd = transform.forward;
        backpackSlot.SetPositionAndRotation(bone.position - torsoFwd * backOffset, Quaternion.LookRotation(torsoFwd, Vector3.up));
        backpackSlot.SetParent(bone, true);

        holsterSlot = new GameObject("WeaponHolsterSlot").transform;
        holsterSlot.SetParent(backpackSlot, false);
        PlaceHolster();
    }

    // 가방 있으면 가방 바깥면 위쪽(총이 가방 위에 걸침), 없으면 등 가운데
    void PlaceHolster()
    {
        if (holsterSlot == null) return;
        if (Current != null)
            holsterSlot.localPosition = new Vector3(0f, Current.visualHeight * 0.5f + 0.08f, -Current.visualHeight * 0.4f); // 가방 바깥면 위 (실측)
        else
            holsterSlot.localPosition = new Vector3(0f, 0f, backOffset - holsterNoBagOffset);
        holsterSlot.localRotation = Quaternion.identity;
        shownHolsterSlot = -1; // 위치 바뀌었으니 다시 그림
    }

    // ── 가방 장착 ──
    /// <summary>가방 장착. fromSlot = 인벤토리에서 꺼내 쓰는 경우 그 칸 (null이면 시작 지급 등)</summary>
    public bool Equip(BackpackData bag, InventorySlot fromSlot)
    {
        if (bag == null || inventory == null) return false;
        int itemsAfter = inventory.inventory.Count - (fromSlot != null ? 1 : 0);
        if (itemsAfter > bag.capacity)
        {
            PickupFeedbackUI.Instance?.ShowWarning($"{bag.itemName}: 아이템이 너무 많음 ({itemsAfter}/{bag.capacity})");
            return false;
        }

        if (fromSlot != null) inventory.RemoveItemById(fromSlot.slotId);
        if (Current != null) WorldItemDrop.Spawn(transform.position - transform.forward * 1f, Current, 1); // 이전 가방은 바닥에

        Current = bag;
        SetCapacity(bag.capacity);
        RebuildBagVisual();
        PlaceHolster();
        Debug.Log($"[가방] {bag.itemName} 장착 → {bag.capacity}칸");
        return true;
    }

    /// <summary>바닥 가방 줍기: 가방이 없으면 바로 장착하고 true, 이미 있으면 무시(false, 바닥에 그대로)</summary>
    public bool TryPickup(BackpackData bag)
    {
        if (bag == null) return false;
        if (Current != null)
        {
            PickupFeedbackUI.Instance?.ShowWarning("이미 가방을 메고 있음");
            return false;
        }
        bool ok = Equip(bag, null);
        if (ok) PickupFeedbackUI.Instance?.Show($"{bag.itemName} 장착 ({bag.capacity}칸)");
        return ok;
    }

    /// <summary>가방 벗기 (바닥에 내려놓음). 아이템이 기본 칸 수보다 많으면 거부. ※ 현재 게임 규칙상 호출하는 곳 없음(벗을 수 없음)</summary>
    public bool Unequip()
    {
        if (Current == null) return false;
        if (inventory.inventory.Count > noBackpackCapacity)
        {
            PickupFeedbackUI.Instance?.ShowWarning("아이템이 너무 많아서 가방을 벗을 수 없음");
            return false;
        }
        WorldItemDrop.Spawn(transform.position - transform.forward * 1f, Current, 1);
        Current = null;
        SetCapacity(noBackpackCapacity);
        RebuildBagVisual();
        PlaceHolster();
        return true;
    }

    void SetCapacity(int capacity)
    {
        if (inventory != null) inventory.maxCapacity = capacity;
        if (inventoryUI != null)
        {
            InventoryLayoutFitter.Apply(inventoryUI, capacity);
            inventoryUI.UpdateUI();
        }
    }

    void RebuildBagVisual()
    {
        if (bagVisual != null) Destroy(bagVisual.gameObject);
        bagVisual = null;
        if (Current == null || Current.model == null || backpackSlot == null) return;
        bagVisual = Instantiate(Current.model, backpackSlot).transform;
        bagVisual.localPosition = new Vector3(0f, 0f, Current.visualHeight * bagForwardShift);
        bagVisual.localRotation = Quaternion.identity;
        bagVisual.localScale = Vector3.one * Current.visualHeight; // 프리팹은 높이 1m 기준
        foreach (var c in bagVisual.GetComponentsInChildren<Collider>()) Destroy(c);
    }

    // ── 등 무기 ──
    void LateUpdate()
    {
        if (backpackSlot == null)
        {
            CreateSlots();
            RebuildBagVisual();
        }

        var hb = WeaponHotbarUI.Instance;
        if (hb == null || holsterSlot == null) return;

        int other = hb.GetActiveSlot() == 0 ? 1 : 0; // 손에 안 든 무기 슬롯
        ItemData item = hb.GetItem(other);
        if (other == shownHolsterSlot && item == shownHolsterItem) return;
        shownHolsterSlot = other; shownHolsterItem = item;

        if (holsterVisual != null) Destroy(holsterVisual.gameObject);
        holsterVisual = null;
        if (!(item is WeaponData wd) || wd.weaponPrefab == null) return;

        holsterVisual = Instantiate(wd.weaponPrefab, holsterSlot).transform;
        holsterVisual.name = "Holster_" + wd.itemName;
        holsterVisual.localRotation = Quaternion.Euler(wd.holsterRotation != Vector3.zero ? wd.holsterRotation : DefaultHolsterRotation);
        holsterVisual.localPosition = Vector3.zero;
        // 무기 모델 중심이 장착점에 오게 (프리팹 원점은 손잡이라 한쪽으로 치우침)
        Bounds b = new Bounds(holsterVisual.position, Vector3.zero); bool first = true;
        foreach (var r in holsterVisual.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
        holsterVisual.localPosition = -holsterSlot.InverseTransformPoint(b.center);
        foreach (var c in holsterVisual.GetComponentsInChildren<Collider>()) Destroy(c);
    }

    /// <summary>기본 등 무기 회전: 총구가 왼쪽 위 대각선, 총 윗면이 뒤쪽 (캐릭터 기준)</summary>
    public static Vector3 DefaultHolsterRotation => Quaternion.LookRotation(new Vector3(-0.6f, 0.8f, 0f), Vector3.back).eulerAngles;

    static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform c in parent)
        {
            if (c.name == name) return c;
            var f = FindDeep(c, name);
            if (f != null) return f;
        }
        return null;
    }
}
