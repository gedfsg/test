using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 좌하단 플레이어 상태 패널 (레퍼런스 UI 섹션 3).
///
///   ┌──────────────────────────────────┐
///   │ ❤ 체력    ▓▓▓▓▓░░░░░   54 / 100 │
///   │ ⚡ 에너지  ▓▓▓▓▓▓▓▓░░   82 / 100 │
///   │──────────────────────────────────│
///   │ (◔💧) 수분 68%  │  (◕🍴) 포만감 75% │
///   └──────────────────────────────────┘
///
/// Start에서 코드로 전부 생성, 데이터는 읽기만 함:
///   체력 = Health, 에너지 = Locomotion 스태미나, 수분/포만감 = SurvivalNeeds.
/// 좌표는 레퍼런스 이미지(1681x936) 픽셀을 패널 중심(242.5, 775) 기준으로 환산한 값.
/// Canvas 밑 StatusPanelHUD 오브젝트(좌하단 앵커)에 붙임 → Tools/HUD/Apply Layout/3.
///
/// (2026-11-22) 아포칼립스 리디자인 (Assets/UI/변경할 ui.png): 위치·크기 그대로, 모양만
///   ❤ ▓▓▓▓▓▓▓▓▓▓▓▓▓░░░  100 / 100   (빨강 그라데이션)
///   ⚡ ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓░  100 / 100   (노랑)
///   💧 ▓▓▓▓▓▓▓▓▓▓▓▓░░░░        95%   (파랑)
///   🍴 ▓▓▓▓▓▓▓▓▓▓▓▓▓▓░░        97%   (초록)
///   원형 게이지·라벨·테두리 없음, 반투명 검정 배경(모서리 작게), 숫자 bold. 예전 Build()는 BuildLegacy()로 남김
/// </summary>
public class StatusPanelHUD : MonoBehaviour
{
    [Header("색상 (레퍼런스 샘플링)")]
    public Color panelBg = new Color32(30, 31, 35, 225);
    public Color panelBorder = new Color32(100, 98, 96, 255);
    public Color trackBg = new Color32(20, 21, 24, 255);
    public Color trackBorder = new Color32(75, 75, 76, 255);
    public Color healthFill = new Color32(0xFC, 0x62, 0x60, 0xFF);
    public Color energyFill = new Color32(0xFE, 0xB6, 0x4A, 0xFF);
    public Color heartColor = new Color32(0xFD, 0x5D, 0x60, 0xFF);
    public Color boltColor = new Color32(0xFC, 0xBA, 0x4B, 0xFF);
    public Color separator = new Color32(130, 130, 131, 255);
    public Color divider = new Color32(66, 67, 70, 255);
    public Color ringTrack = new Color32(57, 57, 57, 255);
    public Color waterRing = new Color32(0x8B, 0xCC, 0xF8, 0xFF);
    public Color waterIcon = new Color32(0x91, 0xCA, 0xFD, 0xFF);
    public Color foodRing = new Color32(0x9B, 0xBE, 0x64, 0xFF);
    public Color foodIcon = new Color32(0xF8, 0xE9, 0xC1, 0xFF);
    public Color labelColor = new Color32(240, 240, 240, 255);

    const float BarWidth = 191f, BarHeight = 22f, FillInset = 4f;

    Health health;
    Locomotion locomotion;
    SurvivalNeeds needs;

    RectTransform hpFill, enFill;
    TextMeshProUGUI hpText, enText, waterText, foodText;
    Image waterFill, foodFill;
    // (2026-11-22) 새 레이아웃: 4줄 모두 Filled 그라데이션 바
    bool apoc;
    Image hpBar, enBar, waterBar, foodBar;

    void Start()
    {
        var player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            health = player.GetComponent<Health>();
            locomotion = player.GetComponent<Locomotion>();
            needs = player.GetComponent<SurvivalNeeds>();
        }
        Build();
        Update();
    }

    // (2026-11-22) 아포칼립스 리디자인 레이아웃 (패널 크기 431x228 그대로 → 화면 위치·크기 같음)
    const float RowGap = 52f, BarLeft = -158f, BarRight = 76f, ApocBarH = 22f; // 오른쪽 값 칸 확보 (바 끝과 숫자가 붙어 보였음)

    // ── (2026-11-22 2차) 레퍼런스 그대로: 패널 배경 없이 줄마다 검은 붓질 띠 + 슬림한 스텐실 바 + 점선 빈 홈 ──
    //  레퍼런스 px(1681 기준) x2 = 이 패널 단위 (오브젝트 스케일 0.5). 왼쪽 끝은 예전 패널 왼쪽(-215.5)에 맞추고 오른쪽으로 넓어짐
    //  색(레퍼런스 샘플): 체력 #C31124 / 에너지 #D39227 / 수분 #1D83A2 / 포만감(레퍼런스에 없음) #7A9A34
    //  아이콘: 하트 #E3BFB2 / 번개 #F1CB94 / 물방울 #5CBDE0 / 포크 #D9CBB0, 숫자 #F2EAE2 Bold
    const float RefRowGap = 70f, RefBarL = -150f, RefBarR = 220f, RefBarH = 28f;
    static readonly Color RefHp = new Color32(0xC3, 0x11, 0x24, 0xFF), RefEn = new Color32(0xD3, 0x92, 0x27, 0xFF);
    static readonly Color RefWater = new Color32(0x1D, 0x83, 0xA2, 0xFF), RefFood = new Color32(0x7A, 0x9A, 0x34, 0xFF);
    static readonly Color RefText = new Color32(0xF2, 0xEA, 0xE2, 0xFF);
    void Build()
    {
        apoc = true; // 갱신은 같은 함수(UpdateApoc) 사용
        Transform root = transform;
        float y0 = RefRowGap * 1.5f;
        hpBar = BuildRefRow(root, "Health", y0, HudStyle.Heart, new Color32(0xE3, 0xBF, 0xB2, 0xFF), new Vector2(40f, 36f), RefHp, "a", out hpText);
        enBar = BuildRefRow(root, "Energy", y0 - RefRowGap, HudStyle.Bolt, new Color32(0xF1, 0xCB, 0x94, 0xFF), new Vector2(34f, 40f), RefEn, "b", out enText);
        waterBar = BuildRefRow(root, "Water", y0 - RefRowGap * 2f, HudStyle.Drop, new Color32(0x5C, 0xBD, 0xE0, 0xFF), new Vector2(32f, 40f), RefWater, "c", out waterText);
        foodBar = BuildRefRow(root, "Food", y0 - RefRowGap * 3f, HudStyle.ForkKnife, new Color32(0xD9, 0xCB, 0xB0, 0xFF), new Vector2(38f, 38f), RefFood, "d", out foodText);
    }

    Image BuildRefRow(Transform root, string name, float y, Sprite icon, Color iconColor, Vector2 iconSize, Color barColor, string brushVariant, out TextMeshProUGUI valueText)
    {
        // 붓질 띠 (아이콘~숫자까지 뒤에 깔림, 검정 반투명)
        HudStyle.NewImage(root, name + "Brush", HudGrunge.Brush(brushVariant), new Color(0f, 0f, 0f, 0.78f), new Vector2(110f, y), new Vector2(700f, 62f));
        HudStyle.NewImage(root, name + "Icon", icon, iconColor, new Vector2(-185f, y), iconSize).preserveAspect = true;
        float w = RefBarR - RefBarL, cx = (RefBarL + RefBarR) / 2f;
        HudStyle.NewImage(root, name + "Track", HudGrunge.BarTrack(), new Color(barColor.r, barColor.g, barColor.b, 0.55f), new Vector2(cx, y), new Vector2(w, RefBarH + 6f));
        var fill = HudStyle.NewImage(root, name + "Fill", HudGrunge.BarFill(), barColor, new Vector2(cx, y), new Vector2(w, RefBarH));
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        valueText = HudStyle.NewText(root, name + "Value", "100 / 100", HudStyle.Bold, 34f, RefText,
            TextAlignmentOptions.Right, new Vector2(RefBarR + 30f + 90f, y), new Vector2(180f, 44f));
        return fill;
    }

    // (2026-11-22 1차) 반투명 패널 + 그라데이션 바 4줄
    void BuildApoc()
    {
        apoc = true;
        Transform root = transform;
        Sliced(HudStyle.NewImage(root, "Bg", HudStyle.RoundedRect, HudStyle.ApocPanel, Vector2.zero, new Vector2(431f, 228f)), 4f); // 모서리 작게
        float y0 = RowGap * 1.5f;
        hpBar = BuildApocRow(root, "Health", y0, HudStyle.Heart, HudStyle.HealthB, new Vector2(30f, 27f), HudStyle.HealthA, HudStyle.HealthB, out hpText);
        enBar = BuildApocRow(root, "Energy", y0 - RowGap, HudStyle.Bolt, HudStyle.EnergyB, new Vector2(28f, 30f), HudStyle.EnergyA, HudStyle.EnergyB, out enText);
        waterBar = BuildApocRow(root, "Water", y0 - RowGap * 2f, HudStyle.Drop, HudStyle.WaterB, new Vector2(26f, 30f), HudStyle.WaterA, HudStyle.WaterB, out waterText);
        foodBar = BuildApocRow(root, "Food", y0 - RowGap * 3f, HudStyle.ForkKnife, HudStyle.FoodB, new Vector2(30f, 29f), HudStyle.FoodA, HudStyle.FoodB, out foodText);
    }

    // 왼쪽 아이콘 + 긴 바(어두운 홈 + 그라데이션 Filled) + 오른쪽 bold 값
    Image BuildApocRow(Transform root, string name, float y, Sprite icon, Color iconColor, Vector2 iconSize,
        Color gradA, Color gradB, out TextMeshProUGUI valueText)
    {
        HudStyle.NewImage(root, name + "Icon", icon, iconColor, new Vector2(-188f, y), iconSize).preserveAspect = true;
        float w = BarRight - BarLeft, cx = (BarLeft + BarRight) / 2f;
        HudStyle.NewImage(root, name + "Track", null, HudStyle.ApocTrack, new Vector2(cx, y), new Vector2(w, ApocBarH));
        var fill = HudStyle.NewImage(root, name + "Fill", HudStyle.HorizontalGradient(gradA, gradB), Color.white, new Vector2(cx, y), new Vector2(w, ApocBarH));
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        valueText = HudStyle.NewText(root, name + "Value", "100 / 100", HudStyle.Bold, 22f, Color.white,
            TextAlignmentOptions.Right, new Vector2(202f - 58f, y), new Vector2(116f, 30f));
        return fill;
    }

    // (이전 ~2026-11-21) 체력·에너지 바 + 수분·포만감 원형 게이지 레이아웃
    void BuildLegacy()
    {
        Transform root = transform;

        // 패널 배경 + 테두리 (모서리 반경 8px)
        Sliced(HudStyle.NewImage(root, "Bg", HudStyle.RoundedRect, panelBg, Vector2.zero, new Vector2(431f, 228f)), 2f);
        Sliced(HudStyle.NewImage(root, "Border", HudStyle.RoundedFrame, panelBorder, Vector2.zero, new Vector2(431f, 228f)), 2f);

        // ── 체력 / 에너지 줄 ──
        hpFill = BuildBarRow(root, "Health", 82f, HudStyle.Heart, heartColor, new Vector2(25f, 22f), "체력", healthFill, out hpText);
        enFill = BuildBarRow(root, "Energy", 42f, HudStyle.Bolt, boltColor, new Vector2(27f, 24f), "에너지", energyFill, out enText);

        // 가로 구분선
        HudStyle.NewImage(root, "Separator", null, separator, new Vector2(-7.5f, 11.5f), new Vector2(412f, 1.5f));

        // ── 수분 / 포만감 원형 게이지 ──
        waterFill = BuildRing(root, "Water", new Vector2(-152.5f, -53f), waterRing, HudStyle.Drop, waterIcon, new Vector2(44f, 41f), "수분", out waterText);
        foodFill = BuildRing(root, "Food", new Vector2(58.5f, -53f), foodRing, HudStyle.ForkKnife, foodIcon, new Vector2(46f, 44f), "포만감", out foodText);

        HudStyle.NewImage(root, "Divider", null, divider, new Vector2(-8.5f, -51.5f), new Vector2(1.5f, 81f));
    }

    // 아이콘 + 라벨 + 바(홈 + 채움) + "54 / 100" 값. 채움 RectTransform을 돌려줌
    RectTransform BuildBarRow(Transform root, string name, float y, Sprite icon, Color iconColor, Vector2 iconSize,
        string label, Color fillColor, out TextMeshProUGUI valueText)
    {
        HudStyle.NewImage(root, name + "Icon", icon, iconColor, new Vector2(-178.5f, y), iconSize);
        HudStyle.NewText(root, name + "Label", label, HudStyle.Regular, 18f, labelColor,
            TextAlignmentOptions.Left, new Vector2(-150f + 40f, y), new Vector2(80f, 26f));

        // 바: 레퍼런스 x 160~351 → 패널 중심 기준 가운데 +13
        var track = Sliced(HudStyle.NewImage(root, name + "Track", HudStyle.RoundedRect, trackBg, new Vector2(13f, y), new Vector2(BarWidth, BarHeight)), 4f);
        Sliced(HudStyle.NewImage(track.transform, "Border", HudStyle.RoundedFrame, trackBorder, Vector2.zero, new Vector2(BarWidth, BarHeight)), 4f);

        // 채움: 왼쪽 고정, 폭만 바꿈 (둥근 끝 유지하려고 Filled 대신 Sliced + 폭 조절)
        var fill = Sliced(HudStyle.NewImage(track.transform, "Fill", HudStyle.RoundedRect, fillColor, Vector2.zero, Vector2.zero), 4f).rectTransform;
        fill.anchorMin = new Vector2(0f, 0.5f);
        fill.anchorMax = new Vector2(0f, 0.5f);
        fill.pivot = new Vector2(0f, 0.5f);
        fill.anchoredPosition = new Vector2(FillInset, 0f);
        fill.sizeDelta = new Vector2(0f, BarHeight - FillInset * 2f + 2f);

        // 값: 오른쪽 끝이 레퍼런스 x 434 → 패널 중심 기준 +191.5
        valueText = HudStyle.NewText(root, name + "Value", "100 / 100", HudStyle.Regular, 18f, labelColor,
            TextAlignmentOptions.Right, new Vector2(191.5f - 50f, y), new Vector2(100f, 26f));
        return fill;
    }

    // 회색 트랙 링 + 위에서 시계방향으로 차오르는 색 링 + 가운데 아이콘 + 라벨/퍼센트
    Image BuildRing(Transform root, string name, Vector2 center, Color ringColor, Sprite icon, Color iconColor, Vector2 iconSize,
        string label, out TextMeshProUGUI percentText)
    {
        HudStyle.NewImage(root, name + "Track", HudStyle.RingThick, ringTrack, center, new Vector2(86f, 86f));
        var fill = HudStyle.NewImage(root, name + "Fill", HudStyle.RingThick, ringColor, center, new Vector2(86f, 86f));
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Radial360;
        fill.fillOrigin = (int)Image.Origin360.Top;
        fill.fillClockwise = true;
        HudStyle.NewImage(root, name + "Icon", icon, iconColor, center, iconSize);

        // 라벨/퍼센트: 링 오른쪽 (레퍼런스 왼쪽 끝 = 링 중심 + 59)
        float left = center.x + 59f;
        HudStyle.NewText(root, name + "Label", label, HudStyle.SemiBold, 17f, labelColor,
            TextAlignmentOptions.Left, new Vector2(left + 40f, center.y + 23f), new Vector2(80f, 24f));
        percentText = HudStyle.NewText(root, name + "Percent", "100%", HudStyle.Regular, 28f, labelColor,
            TextAlignmentOptions.Left, new Vector2(left + 45f, center.y - 13f), new Vector2(90f, 34f));
        return fill;
    }

    static Image Sliced(Image img, float multiplier)
    {
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = multiplier; // 스프라이트 반경 16px → 16/multiplier px
        return img;
    }

    void Update()
    {
        if (apoc) { UpdateApoc(); return; } // (2026-11-22)
        if (hpFill == null) return;

        if (health != null)
            SetBar(hpFill, hpText, health.GetCurrentHealth(), health.GetMaxHealth());

        if (locomotion != null)
            SetBar(enFill, enText, locomotion.GetStaminaNormalized() * locomotion.maxStamina, locomotion.maxStamina);

        if (needs != null)
        {
            waterFill.fillAmount = needs.WaterNormalized;
            foodFill.fillAmount = needs.FoodNormalized;
            waterText.text = $"{Mathf.CeilToInt(needs.water)}%";
            foodText.text = $"{Mathf.CeilToInt(needs.food)}%";
        }
    }

    // (2026-11-22) 새 레이아웃 갱신
    void UpdateApoc()
    {
        if (hpBar == null) return;
        if (health != null) SetApocBar(hpBar, hpText, health.GetCurrentHealth(), health.GetMaxHealth(), false);
        if (locomotion != null) SetApocBar(enBar, enText, locomotion.GetStaminaNormalized() * locomotion.maxStamina, locomotion.maxStamina, false);
        if (needs != null)
        {
            SetApocBar(waterBar, waterText, needs.water, 100f, true);
            SetApocBar(foodBar, foodText, needs.food, 100f, true);
        }
    }

    static void SetApocBar(Image bar, TextMeshProUGUI text, float current, float max, bool percent)
    {
        bar.fillAmount = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        // text.text = percent ? $"{Mathf.CeilToInt(Mathf.Max(current, 0f))}%" : ... // (1차)
        text.text = percent ? $"{Mathf.CeilToInt(Mathf.Max(current, 0f))} %" : $"{Mathf.CeilToInt(Mathf.Max(current, 0f))} / {Mathf.RoundToInt(max)}"; // (2차) 레퍼런스 "98 %"
    }

    static void SetBar(RectTransform fill, TextMeshProUGUI text, float current, float max)
    {
        float ratio = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        float fullWidth = BarWidth - FillInset * 2f;
        // 너무 짧으면 둥근 모서리가 찌그러지므로 0일 때만 숨김
        fill.gameObject.SetActive(ratio > 0f);
        fill.sizeDelta = new Vector2(Mathf.Max(fullWidth * ratio, 8f), fill.sizeDelta.y);
        text.text = $"{Mathf.CeilToInt(Mathf.Max(current, 0f))} / {Mathf.RoundToInt(max)}";
    }
}
