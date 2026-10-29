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

    void Build()
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
