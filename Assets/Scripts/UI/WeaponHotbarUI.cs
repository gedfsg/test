using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;

/// <summary>
/// 화면 하단 중앙 무기 핫바 (슬롯 1~5)
/// 무기 픽업 시 순서대로 슬롯 채워지고, 1~5 키로 교체
/// </summary>
public class WeaponHotbarUI : MonoBehaviour
{
    public static WeaponHotbarUI Instance { get; private set; }

    private const int SLOT_COUNT = 5;

    // 핫바 데이터
    private WeaponData[] weapons = new WeaponData[SLOT_COUNT];
    private int[] ammos          = new int[SLOT_COUNT];
    private int activeSlot       = -1;

    // UI 레퍼런스
    private GameObject[] slotObjects          = new GameObject[SLOT_COUNT];
    private Image[]      slotBgs              = new Image[SLOT_COUNT];
    private Image[]      slotIcons            = new Image[SLOT_COUNT];
    private TextMeshProUGUI[] slotNumbers     = new TextMeshProUGUI[SLOT_COUNT];
    private TextMeshProUGUI[] slotNames       = new TextMeshProUGUI[SLOT_COUNT];

    [Header("위치 설정")]
    [SerializeField] private float positionY = 90f;   // 하단으로부터 높이 (Inspector에서 조절)

    private PlayerController playerController;

    // ── 색상 ──────────────────────────────────
    static readonly Color ColEmpty   = new Color(0.06f, 0.08f, 0.11f, 0.88f);
    static readonly Color ColFilled  = new Color(0.14f, 0.17f, 0.21f, 0.92f);
    static readonly Color ColActive  = new Color(0.00f, 0.45f, 0.70f, 0.95f);
    static readonly Color ColNumOff  = new Color(0.55f, 0.60f, 0.65f, 1f);
    static readonly Color ColNameOff = new Color(0.70f, 0.73f, 0.76f, 1f);

    // ─────────────────────────────────────────

    void Awake() { Instance = this; }

    void Start()
    {
        playerController = FindAnyObjectByType<PlayerController>();
        BuildHotbarUI();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if      (kb.digit1Key.wasPressedThisFrame) EquipSlot(0);
        else if (kb.digit2Key.wasPressedThisFrame) EquipSlot(1);
        else if (kb.digit3Key.wasPressedThisFrame) EquipSlot(2);
        else if (kb.digit4Key.wasPressedThisFrame) EquipSlot(3);
        else if (kb.digit5Key.wasPressedThisFrame) EquipSlot(4);
    }

    // ── Public API ────────────────────────────

    /// <summary>무기를 다음 빈 슬롯에 추가. 꽉 찬 경우 false 반환.</summary>
    public bool AddWeapon(WeaponData weapon, int ammo = -1)
    {
        // 이미 같은 무기 → 탄약만 갱신
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            if (weapons[i] == weapon)
            {
                if (ammo >= 0) ammos[i] = ammo;
                RefreshSlot(i);
                return true;
            }
        }

        // 빈 슬롯 탐색
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            if (weapons[i] == null)
            {
                weapons[i] = weapon;
                ammos[i]   = ammo >= 0 ? ammo : weapon.maxAmmo;
                RefreshSlot(i);

                // 처음 무기면 자동 장착
                if (activeSlot == -1)
                    EquipSlot(i);

                return true;
            }
        }

        return false; // 슬롯 가득 참
    }

    /// <summary>해당 슬롯 무기 장착</summary>
    public void EquipSlot(int index)
    {
        if (index < 0 || index >= SLOT_COUNT) return;
        if (weapons[index] == null) return;

        // 현재 장착 무기 탄약 저장
        if (activeSlot >= 0 && playerController != null)
            ammos[activeSlot] = playerController.GetCurrentAmmo();

        activeSlot = index;
        playerController?.SwapWeaponData(weapons[index], ammos[index]);
        RefreshAllSlots();
    }

    // ── UI 생성 ───────────────────────────────

    void BuildHotbarUI()
    {
        // 핫바 전용 Canvas (Scale With Screen Size 1920x1080 기준)
        GameObject canvasObj = new GameObject("HotbarCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        UnityEngine.UI.CanvasScaler scaler = canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode         = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;

        canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        float slotW   = 70f;
        float slotH   = 70f;
        float spacing = 6f;

        // 슬롯 컨테이너
        GameObject container = new GameObject("WeaponHotbarContainer");
        container.transform.SetParent(canvas.transform, false);

        RectTransform crt = container.AddComponent<RectTransform>();
        crt.anchorMin        = new Vector2(0.5f, 0f);
        crt.anchorMax        = new Vector2(0.5f, 0f);
        crt.pivot            = new Vector2(0.5f, 0f);
        crt.anchoredPosition = new Vector2(0f, positionY);

        float totalW = SLOT_COUNT * slotW + (SLOT_COUNT - 1) * spacing;
        crt.sizeDelta = new Vector2(totalW, slotH);

        // 슬롯을 수동으로 배치
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
        srt.anchorMin        = new Vector2(0.5f, 0.5f);
        srt.anchorMax        = new Vector2(0.5f, 0.5f);
        srt.pivot            = new Vector2(0.5f, 0.5f);
        srt.anchoredPosition = new Vector2(localX, 0f);
        srt.sizeDelta        = new Vector2(w, h);

        Image bg = slot.AddComponent<Image>();
        bg.color           = ColEmpty;
        slotBgs[index]     = bg;
        slotObjects[index] = slot;

        // ── 아이콘 (중앙, 약간 위)
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(slot.transform, false);
        RectTransform irt = iconObj.AddComponent<RectTransform>();
        irt.anchorMin = new Vector2(0.1f, 0.25f);
        irt.anchorMax = new Vector2(0.9f, 0.95f);
        irt.offsetMin = Vector2.zero;
        irt.offsetMax = Vector2.zero;
        Image icon = iconObj.AddComponent<Image>();
        icon.preserveAspect = true;
        icon.color          = new Color(1, 1, 1, 0f);
        slotIcons[index]    = icon;

        // ── 이름 텍스트 (아이콘 없을 때도 보이는 중앙 텍스트)
        var nameTxt = CreateText(slot.transform, "Name",
            anchorMin: new Vector2(0f, 0.22f), anchorMax: new Vector2(1f, 0.78f),
            pivot: new Vector2(0.5f, 0.5f),
            anchoredPos: Vector2.zero, size: Vector2.zero,
            text: "", fontSize: 10f, color: ColNameOff);
        nameTxt.alignment    = TextAlignmentOptions.Center;
        nameTxt.overflowMode = TextOverflowModes.Truncate;
        slotNames[index]     = nameTxt;

        // ── 하단 바 (번호 표시)
        GameObject barObj = new GameObject("Bar");
        barObj.transform.SetParent(slot.transform, false);
        RectTransform barRt = barObj.AddComponent<RectTransform>();
        barRt.anchorMin = new Vector2(0f, 0f);
        barRt.anchorMax = new Vector2(1f, 0.24f);
        barRt.offsetMin = Vector2.zero;
        barRt.offsetMax = Vector2.zero;
        barObj.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);

        var numTxt = CreateText(barObj.transform, "Num",
            anchorMin: Vector2.zero, anchorMax: Vector2.one,
            pivot: new Vector2(0.5f, 0.5f),
            anchoredPos: Vector2.zero, size: Vector2.zero,
            text: (index + 1).ToString(),
            fontSize: 16f, color: ColNumOff);
        numTxt.alignment  = TextAlignmentOptions.Center;
        slotNumbers[index] = numTxt;
    }

    TextMeshProUGUI CreateText(Transform parent, string objName,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 anchoredPos, Vector2 size, string text, float fontSize, Color color)
    {
        GameObject obj = new GameObject(objName);
        obj.transform.SetParent(parent, false);

        RectTransform rt   = obj.AddComponent<RectTransform>();
        rt.anchorMin        = anchorMin;
        rt.anchorMax        = anchorMax;
        rt.pivot            = pivot;
        rt.anchoredPosition = anchoredPos;
        if (size != Vector2.zero) rt.sizeDelta = size;
        else { rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }

        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = fontSize;
        tmp.color     = color;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        return tmp;
    }

    // ── 슬롯 갱신 ─────────────────────────────

    void RefreshSlot(int i)
    {
        if (slotObjects == null || i >= slotObjects.Length || slotObjects[i] == null) return;

        bool has      = weapons[i] != null;
        bool isActive = i == activeSlot;

        slotBgs[i].color     = isActive ? ColActive : (has ? ColFilled : ColEmpty);
        slotNumbers[i].color = isActive ? Color.white : ColNumOff;

        if (has)
        {
            // 아이콘 있으면 이미지 표시
            if (weapons[i].icon != null)
            {
                slotIcons[i].sprite = weapons[i].icon;
                slotIcons[i].color  = Color.white;
                slotNames[i].text   = "";  // 아이콘 있으면 이름 숨김
            }
            else
            {
                // 아이콘 없으면 이름 텍스트로 대체
                slotIcons[i].sprite = null;
                slotIcons[i].color  = new Color(1, 1, 1, 0f);
                slotNames[i].text   = ShortenName(weapons[i].itemName);
            }
            slotNames[i].color = isActive ? Color.white : ColNameOff;
        }
        else
        {
            slotIcons[i].sprite = null;
            slotIcons[i].color  = new Color(1, 1, 1, 0f);
            slotNames[i].text   = "";
        }
    }

    void RefreshAllSlots()
    {
        for (int i = 0; i < SLOT_COUNT; i++) RefreshSlot(i);
    }

    static string ShortenName(string name)
    {
        return name.Length > 9 ? name.Substring(0, 9) : name;
    }
}
