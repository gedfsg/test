using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 아이템 툴팁: 인벤토리/핫바 슬롯에 마우스를 올리면 커서 옆에 이름 + 설명 + 효과를 띄운다.
/// SlotView.OnPointerEnter/Exit에서 Show/Hide 호출. UI는 처음 쓸 때 슬롯이 있는 캔버스 위에 코드로 만든다.
///  - 반투명 검정 둥근 패널 (HudStyle.RoundedRect), Pretendard
///  - 효과 줄은 아이템 데이터 수치로 자동 생성 (수분 +30%, 체력 +20 등) → 에셋 값을 바꾸면 툴팁도 같이 바뀜
///  - 드래그 중에는 표시 안 함
/// </summary>
public class ItemTooltipUI : MonoBehaviour
{
    static ItemTooltipUI instance;

    const float Width = 260f;
    static readonly Vector2 CursorOffset = new Vector2(20f, -20f); // 커서 오른쪽 아래

    RectTransform panel;
    RectTransform canvasRect;
    Canvas canvas;
    TextMeshProUGUI nameText, descText, effectText;
    SlotView owner; // 지금 툴팁을 띄운 슬롯 (슬롯이 비거나 꺼지면 숨김)

    /// <summary>슬롯 위에 마우스가 올라왔을 때. 빈 슬롯/드래그 중이면 무시</summary>
    public static void Show(SlotView slot)
    {
        if (slot == null || slot.CurrentItem == null || SlotDragDrop.IsDragging) return;
        var c = slot.GetComponentInParent<Canvas>();
        if (c == null) return;
        if (instance == null || instance.canvas != c.rootCanvas) Create(c.rootCanvas);
        instance.Open(slot);
    }

    public static void Hide(SlotView slot = null)
    {
        if (instance == null) return;
        if (slot != null && instance.owner != slot) return; // 다른 슬롯에서 나간 이벤트면 무시
        instance.owner = null;
        instance.panel.gameObject.SetActive(false);
    }

    public static bool IsShowing => instance != null && instance.panel.gameObject.activeSelf;

    static void Create(Canvas root)
    {
        if (instance != null) Destroy(instance.gameObject);

        var go = new GameObject("ItemTooltip", typeof(RectTransform));
        go.layer = root.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(root.transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0f, 1f); // 왼쪽 위 기준 → 커서 오른쪽 아래로 펼쳐짐
        rt.sizeDelta = new Vector2(Width, 100f);

        // 반투명 검정 둥근 배경
        var bg = go.AddComponent<Image>();
        bg.sprite = HudStyle.RoundedRect;
        bg.type = bg.sprite != null && bg.sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        bg.color = new Color(0f, 0f, 0f, 0.82f);
        bg.raycastTarget = false; // 툴팁이 마우스를 가로채면 슬롯 Exit/Enter가 깜빡임

        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 10, 12);
        layout.spacing = 4f;
        layout.childControlWidth = true; layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var t = go.AddComponent<ItemTooltipUI>();
        t.panel = rt;
        t.canvas = root;
        t.canvasRect = (RectTransform)root.transform;
        t.nameText = NewLine(rt, "Name", HudStyle.Bold, 18f, Color.white);
        t.descText = NewLine(rt, "Desc", HudStyle.Regular, 14f, new Color(1f, 1f, 1f, 0.72f));
        t.effectText = NewLine(rt, "Effect", HudStyle.SemiBold, 14f, Color.white);
        go.SetActive(false);
        instance = t;
    }

    static TextMeshProUGUI NewLine(Transform parent, string name, TMP_FontAsset font, float size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = font;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.enableWordWrapping = true; // 설명이 길면 줄바꿈 (패널 폭 고정)
        tmp.raycastTarget = false;
        tmp.richText = true;
        return tmp;
    }

    void Open(SlotView slot)
    {
        owner = slot;
        ItemData item = slot.CurrentItem;
        nameText.text = item.itemName;
        nameText.color = item.rarity == Rarity.Common ? Color.white : RarityColors.Get(item.rarity); // 희귀도 색 (일반은 흰색)
        descText.text = Description(item);
        string fx = Effects(item);
        effectText.text = fx;
        effectText.gameObject.SetActive(!string.IsNullOrEmpty(fx));
        panel.SetAsLastSibling(); // 다른 UI 위로
        panel.gameObject.SetActive(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel); // 높이 바로 계산 (화면 밖 밀어넣기용)
        Follow();
    }

    void LateUpdate()
    {
        if (!panel.gameObject.activeSelf) return;
        // 슬롯이 꺼졌거나(인벤토리 닫힘) 아이템이 빠졌거나(사용/이동) 드래그가 시작되면 숨김
        if (owner == null || !owner.isActiveAndEnabled || owner.CurrentItem == null || SlotDragDrop.IsDragging) { Hide(); return; }
        Follow();
    }

    /// <summary>커서 오른쪽 아래에 붙이고, 화면 밖으로 나가면 반대쪽으로 뒤집기</summary>
    void Follow()
    {
        if (Mouse.current == null) return;
        Vector2 screen = Mouse.current.position.ReadValue();
        Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, cam, out Vector2 local);

        Vector2 size = panel.rect.size;
        Rect area = canvasRect.rect;
        Vector2 pos = local + CursorOffset;
        if (pos.x + size.x > area.xMax) pos.x = local.x - CursorOffset.x - size.x; // 오른쪽 넘치면 커서 왼쪽으로
        if (pos.y - size.y < area.yMin) pos.y = local.y - CursorOffset.y + size.y; // 아래 넘치면 커서 위로
        panel.anchoredPosition = pos;
    }

    // ── 내용 ─────────────────────────────────────

    // 아이템별 설명 한 줄 (itemName 기준). 없으면 MaterialData.description → 타입별 기본 문구
    static readonly Dictionary<string, string> Flavor = new Dictionary<string, string>
    {
        { "생수", "깨끗한 물 한 병. 갈증을 해소한다." },
        { "콜라", "김 빠진 탄산음료. 달아서 배도 조금 찬다." },
        { "커피", "식은 캔커피. 목을 축이기엔 충분하다." },
        { "사과", "조금 무른 사과. 가볍게 허기를 달랜다." },
        { "빵", "포장된 빵. 아직 먹을 만하다." },
        { "통조림", "오래 보관된 통조림. 든든한 한 끼." },
        { "라면", "부숴 먹는 라면. 배도 차고 국물도 조금." },
        { "붕대", "상처를 감싸 출혈을 막는다." },
        { "의료키트", "응급 처치 도구 세트. 큰 상처도 치료한다." },
        { "진통제", "통증을 줄여 서서히 체력이 회복된다." },
    };

    static string Description(ItemData item)
    {
        if (Flavor.TryGetValue(item.itemName, out var s)) return s;
        if (item is MaterialData m && !string.IsNullOrEmpty(m.description)) return m.description;
        return item switch
        {
            WeaponData w when w.type == WeaponType.Melee => "근접 무기. 소리 없이 좀비를 처리할 수 있다.",
            WeaponData => "총기. 탄약이 있어야 쓸 수 있고 소리가 크다.",
            ConsumableData => "먹거나 사용할 수 있다.",
            MaterialData => "제작 재료.",
            _ => ""
        };
    }

    // 색: HudStyle의 수분/포만감/체력 색과 맞춤
    const string CWater = "#4A9EFF", CFood = "#4ADE80", CHeal = "#FF6B6B", CStamina = "#FFA500", CInfo = "#FFD700", CDim = "#FFFFFFAA";

    static string Effects(ItemData item)
    {
        var sb = new StringBuilder();
        void Line(string color, string text) { if (sb.Length > 0) sb.Append('\n'); sb.Append($"<color={color}>{text}</color>"); }

        switch (item)
        {
            case ConsumableData c:
                if (c.healAmount > 0f) Line(CHeal, $"체력 +{c.healAmount:0}");
                if (c.healPerSecond > 0f) Line(CHeal, $"체력 초당 +{c.healPerSecond:0} ({c.healDuration:0}초)");
                if (c.waterAmount > 0f) Line(CWater, $"수분 +{c.waterAmount:0}%");
                if (c.foodAmount > 0f) Line(CFood, $"포만감 +{c.foodAmount:0}%");
                if (c.staminaAmount > 0f) Line(CStamina, $"스태미나 +{c.staminaAmount:0}");
                if (c.buffType == BuffType.SpeedBoost || c.buffType == BuffType.Adrenaline)
                    Line(CStamina, $"이동속도 +{(c.speedMultiplier - 1f) * 100f:0}% ({c.buffDuration:0}초)");
                break;
            case WeaponData w:
                if (w.type == WeaponType.Melee) Line(CInfo, $"데미지 {w.damage:0}");
                else
                {
                    string pellets = w.type == WeaponType.Shotgun ? $" × {w.pelletCount}발" : "";
                    Line(CInfo, $"데미지 {w.damage:0}{pellets} · 탄창 {w.maxAmmo}");
                    Line(CDim, $"{AmmoName(w.type)} 사용{(w.autoFire ? " · 연사" : "")}");
                }
                break;
            case MaterialData:
                var recipe = CraftingRecipe.FindByIngredient(item);
                if (recipe != null) Line(CInfo, $"{recipe.ingredientCount}개 → {recipe.result.itemName} {recipe.resultCount}개 (우클릭 → 제작하기)");
                break;
        }
        return sb.ToString();
    }

    static string AmmoName(WeaponType t) => t switch
    {
        WeaponType.Pistol => "권총 탄약",
        WeaponType.Shotgun => "샷건 탄약",
        WeaponType.Sniper => "저격 탄약",
        _ => "소총 탄약",
    };
}
