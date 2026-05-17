using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;

public class WeaponHotbarUI : MonoBehaviour
{
    public static WeaponHotbarUI Instance { get; private set; }

    private const int SLOT_COUNT = 5;

    private ItemData[] items = new ItemData[SLOT_COUNT];
    private int[] ammos = new int[SLOT_COUNT];
    private int activeSlot = 0; // 기본값 0 (항상 선택 가능)

    private GameObject[] slotObjects = new GameObject[SLOT_COUNT];
    private Image[] slotBgs = new Image[SLOT_COUNT];
    private Image[] slotIcons = new Image[SLOT_COUNT];
    private TextMeshProUGUI[] slotNumbers = new TextMeshProUGUI[SLOT_COUNT];
    private TextMeshProUGUI[] slotNames = new TextMeshProUGUI[SLOT_COUNT];
    private TextMeshProUGUI[] slotCounts = new TextMeshProUGUI[SLOT_COUNT];

    [Header("위치 설정")]
    [SerializeField] private float positionY = 90f;

    private PlayerController playerController;

    static readonly Color ColEmpty = new Color(0.06f, 0.08f, 0.11f, 0.88f);
    static readonly Color ColFilled = new Color(0.14f, 0.17f, 0.21f, 0.92f);
    static readonly Color ColActive = new Color(0.00f, 0.45f, 0.70f, 0.95f);
    static readonly Color ColNumOff = new Color(0.55f, 0.60f, 0.65f, 1f);
    static readonly Color ColNameOff = new Color(0.70f, 0.73f, 0.76f, 1f);

    void Awake() { Instance = this; }

    void Start()
    {
        playerController = FindAnyObjectByType<PlayerController>();
        BuildHotbarUI();
        // 시작 시 0번 슬롯 선택 상태로
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
    }

    // ── Public API ────────────────────────────

    /// <summary>슬롯 선택 — 빈 슬롯도 선택 가능, 아이템 있으면 장착</summary>
    public void SelectSlot(int index)
    {
        if (index < 0 || index >= SLOT_COUNT) return;

        // 현재 슬롯이 무기면 탄수 저장
        if (activeSlot >= 0 && items[activeSlot] is WeaponData && playerController != null)
            ammos[activeSlot] = playerController.GetCurrentAmmo();

        activeSlot = index;

        // 무기 슬롯이면 장착, 나머지는 HandleFire()에서 알아서 처리
        if (items[index] is WeaponData wd)
            playerController?.SwapWeaponData(wd, ammos[index]);

        RefreshAllSlots();
    }

    // 기존 코드 호환용
    public void EquipSlot(int index) => SelectSlot(index);

    public bool AddWeapon(WeaponData weapon, int ammo = -1)
        => AddItemToSlot(weapon, ammo >= 0 ? ammo : weapon.maxAmmo);

    public bool AddThrowable(ThrowableData throwable, int count)
    {
        // 이미 같은 아이템이면 개수만 추가
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
        // 이미 같은 아이템이면 개수만 추가
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
    public bool AddItemToActiveSlot(ItemData item, int count)
    {
        return SetSlot(activeSlot, item, count);
    }

    public void ConsumeThrowable(int index)
    {
        if (index < 0 || index >= SLOT_COUNT) return;
        if (!(items[index] is ThrowableData)) return;

        ammos[index]--;
        if (ammos[index] <= 0)
        {
            items[index] = null;
            ammos[index] = 0;
        }
        RefreshAllSlots();
    }

    public void ConsumeItem(int index)
    {
        if (index < 0 || index >= SLOT_COUNT) return;
        ammos[index]--;
        if (ammos[index] <= 0)
        {
            items[index] = null;
            ammos[index] = 0;
        }
        RefreshAllSlots();
    }

    public ItemData GetActiveItem() => activeSlot >= 0 ? items[activeSlot] : null;
    public int GetActiveSlot() => activeSlot;

    public bool HasWeapon(WeaponData weapon)
    {
        for (int i = 0; i < SLOT_COUNT; i++)
            if (items[i] == weapon) return true;
        return false;
    }

    public bool SetSlot(int index, ItemData item, int count)
    {
        if (index < 0 || index >= SLOT_COUNT) return false;

        // 기존 슬롯이 무기였으면 탄수 인벤토리에 돌려줘야 함
        // (EquipToHotbar에서 이미 처리하므로 여기선 핫바 데이터만 갱신)
        items[index] = item;
        ammos[index] = count;
        RefreshSlot(index);

        if (index == activeSlot)
            SelectSlot(index);

        return true;
    }

    // ── 내부 헬퍼 ─────────────────────────────

    /// <summary>빈 슬롯에 아이템 추가 (activeSlot 우선, 없으면 다음 빈 슬롯)</summary>
    private bool AddItemToSlot(ItemData item, int count)
    {
        // 먼저 현재 선택 슬롯이 비어있으면 거기에
        if (items[activeSlot] == null)
        {
            items[activeSlot] = item;
            ammos[activeSlot] = count;
            RefreshSlot(activeSlot);
            SelectSlot(activeSlot);
            return true;
        }

        // 다음 빈 슬롯 탐색
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

    // ── UI 생성 ───────────────────────────────

    void BuildHotbarUI()
    {
        GameObject canvasObj = new GameObject("HotbarCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        float slotW = 70f, slotH = 70f, spacing = 6f;

        GameObject container = new GameObject("WeaponHotbarContainer");
        container.transform.SetParent(canvas.transform, false);

        RectTransform crt = container.AddComponent<RectTransform>();
        crt.anchorMin = new Vector2(0.5f, 0f);
        crt.anchorMax = new Vector2(0.5f, 0f);
        crt.pivot = new Vector2(0.5f, 0f);
        crt.anchoredPosition = new Vector2(0f, positionY);

        float totalW = SLOT_COUNT * slotW + (SLOT_COUNT - 1) * spacing;
        crt.sizeDelta = new Vector2(totalW, slotH);

        for (int i = 0; i < SLOT_COUNT; i++)
        {
            float x = i * (slotW + spacing) - totalW / 2f + slotW / 2f;
            CreateSlot(container.transform, i, slotW, slotH, x);
        }

        RefreshAllSlots();
    }

    void CreateSlot(Transform parent, int index, float w, float h, float localX)
    {
        GameObject slot = new GameObject($"HotbarSlot_{index + 1}");
        slot.transform.SetParent(parent, false);

        RectTransform srt = slot.AddComponent<RectTransform>();
        srt.anchorMin = new Vector2(0.5f, 0.5f);
        srt.anchorMax = new Vector2(0.5f, 0.5f);
        srt.pivot = new Vector2(0.5f, 0.5f);
        srt.anchoredPosition = new Vector2(localX, 0f);
        srt.sizeDelta = new Vector2(w, h);

        Image bg = slot.AddComponent<Image>();
        bg.color = ColEmpty;
        slotBgs[index] = bg;
        slotObjects[index] = slot;

        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(slot.transform, false);
        RectTransform irt = iconObj.AddComponent<RectTransform>();
        irt.anchorMin = new Vector2(0.1f, 0.25f);
        irt.anchorMax = new Vector2(0.9f, 0.95f);
        irt.offsetMin = irt.offsetMax = Vector2.zero;
        Image icon = iconObj.AddComponent<Image>();
        icon.preserveAspect = true;
        icon.color = new Color(1, 1, 1, 0f);
        slotIcons[index] = icon;

        var nameTxt = CreateText(slot.transform, "Name",
            new Vector2(0f, 0.22f), new Vector2(1f, 0.78f),
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
            "", 10f, ColNameOff);
        nameTxt.alignment = TextAlignmentOptions.Center;
        nameTxt.overflowMode = TextOverflowModes.Truncate;
        slotNames[index] = nameTxt;

        GameObject barObj = new GameObject("Bar");
        barObj.transform.SetParent(slot.transform, false);
        RectTransform barRt = barObj.AddComponent<RectTransform>();
        barRt.anchorMin = new Vector2(0f, 0f);
        barRt.anchorMax = new Vector2(1f, 0.24f);
        barRt.offsetMin = barRt.offsetMax = Vector2.zero;
        barObj.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);

        var numTxt = CreateText(barObj.transform, "Num",
            Vector2.zero, Vector2.one,
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
            (index + 1).ToString(), 16f, ColNumOff);
        numTxt.alignment = TextAlignmentOptions.Center;
        slotNumbers[index] = numTxt;

        var countTxt = CreateText(slot.transform, "Count",
            new Vector2(0.55f, 0.6f), new Vector2(1f, 1f),
            new Vector2(1f, 1f), Vector2.zero, Vector2.zero,
            "", 13f, Color.white);
        countTxt.alignment = TextAlignmentOptions.TopRight;
        slotCounts[index] = countTxt;
    }

    TextMeshProUGUI CreateText(Transform parent, string objName,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 anchoredPos, Vector2 size, string text, float fontSize, Color color)
    {
        GameObject obj = new GameObject(objName);
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        if (size != Vector2.zero) rt.sizeDelta = size;
        else { rt.offsetMin = rt.offsetMax = Vector2.zero; }

        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        return tmp;
    }

    // ── 슬롯 갱신 ─────────────────────────────

    void RefreshSlot(int i)
    {
        if (slotObjects == null || i >= slotObjects.Length || slotObjects[i] == null) return;

        bool has = items[i] != null;
        bool isActive = i == activeSlot;
        bool showCount = has && (items[i] is ThrowableData || items[i] is ConsumableData);

        slotBgs[i].color = isActive ? ColActive : (has ? ColFilled : ColEmpty);
        slotNumbers[i].color = isActive ? Color.white : ColNumOff;

        if (has)
        {
            if (items[i].icon != null)
            {
                slotIcons[i].sprite = items[i].icon;
                slotIcons[i].color = Color.white;
                slotNames[i].text = "";
            }
            else
            {
                slotIcons[i].sprite = null;
                slotIcons[i].color = new Color(1, 1, 1, 0f);
                slotNames[i].text = ShortenName(items[i].itemName);
            }
            slotNames[i].color = isActive ? Color.white : ColNameOff;
            slotCounts[i].text = showCount ? $"x{ammos[i]}" : "";
        }
        else
        {
            slotIcons[i].sprite = null;
            slotIcons[i].color = new Color(1, 1, 1, 0f);
            slotNames[i].text = "";
            slotCounts[i].text = "";
        }
    }

    /// <summary>데이터만 세팅, SelectSlot 호출 안 함 (탄수 덮어쓰기 방지)</summary>
    public void SetSlotOnly(int index, ItemData item, int count)
    {
        if (index < 0 || index >= SLOT_COUNT) return;
        items[index] = item;
        ammos[index] = count;
        RefreshAllSlots();
    }
    public int GetActiveAmmo() => activeSlot >= 0 ? ammos[activeSlot] : 0;

    void RefreshAllSlots() { for (int i = 0; i < SLOT_COUNT; i++) RefreshSlot(i); }

    static string ShortenName(string name) => name.Length > 9 ? name[..9] : name;
}