using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 상단 중앙 생존 날짜/시간 HUD (레퍼런스 UI 섹션 2).
///
///   ──────  생존 1일차  ──────
///   ①──②──③──④──⑤──⑥──⑦        (현재 날짜만 노란 원, 나머지 회색 테두리)
///   ☀ 12:51  │  낮  │  생존 목표 · 7일
///
/// Start에서 코드로 전부 생성하고, DayNightCycle.Instance를 매 프레임 읽기만 함.
/// 위치/크기는 레퍼런스 이미지(1681x936) 픽셀 기준 — Canvas Scaler가 같은 해상도 기준이라 그대로 맞음.
/// 이 컴포넌트는 Canvas 밑 빈 오브젝트(TopCenterHUD, 상단 중앙 앵커)에 붙임 → Tools/HUD/Apply Layout/2.
/// </summary>
public class SurvivalDayHUD : MonoBehaviour
{
    [Header("색상 (레퍼런스 샘플링)")]
    public Color currentDayFill = new Color32(0xFE, 0xBD, 0x51, 0xFF);
    public Color currentDayNumber = new Color32(30, 35, 26, 255);
    public Color dayBorder = new Color32(95, 90, 87, 255);
    public Color dayFill = new Color32(30, 29, 30, 200);
    public Color dayNumber = new Color32(185, 184, 184, 255);
    public Color connector = new Color32(82, 77, 75, 255);
    public Color titleColor = new Color32(240, 235, 225, 255);
    public Color clockColor = new Color32(0xF2, 0xE9, 0xCD, 0xFF);
    public Color dayPhaseColor = new Color32(0xF6, 0xC1, 0x56, 0xFF);
    public Color nightPhaseColor = new Color32(0x8F, 0xB6, 0xFF, 0xFF);
    public Color sunColor = new Color32(0xFD, 0xB8, 0x4C, 0xFF);
    public Color dividerColor = new Color32(227, 215, 193, 200);

    // 레퍼런스에서 이 블록의 중심은 화면 중앙보다 25px 왼쪽 (x≈815.5 / 1681)
    // 아래 좌표들은 전부 그 블록 중심(=이 오브젝트) 기준
    const float CircleY = -35f, TitleY = 0f, BottomY = -81f;
    const float CircleSize = 30f, CircleSpacing = 55f;

    TextMeshProUGUI titleText, clockText, phaseText, goalText;
    Image phaseIcon;
    Image weatherIcon; // 낮/밤 글자 옆 날씨 아이콘 (WeatherManager)
    WeatherManager.Weather shownWeather = (WeatherManager.Weather)(-1);
    Image[] dayFills, dayBorders;
    TextMeshProUGUI[] dayNumbers;
    int shownDay = -1;
    DayNightCycle.Phase shownPhase;

    void Start()
    {
        var dnc = DayNightCycle.Instance;
        int totalDays = dnc != null ? dnc.totalDays : 7;
        Build(totalDays);
        Refresh(force: true);
    }

    void Update()
    {
        Refresh(force: false);
    }

    void Build(int totalDays)
    {
        Transform root = transform;

        // 글자 뒤 은은한 어두운 그림자 (레퍼런스의 상단 어두운 그라데이션)
        HudStyle.NewImage(root, "Shadow", HudStyle.SoftShadow, new Color(0f, 0f, 0f, 0.55f), new Vector2(0f, -42f), new Vector2(560f, 170f));

        // ── 제목 + 양옆 가는 선 ──
        titleText = HudStyle.NewText(root, "Title", "생존 1일차", HudStyle.SemiBold, 19f, titleColor,
            TextAlignmentOptions.Center, new Vector2(0f, TitleY), new Vector2(200f, 30f));
        var lineColor = new Color32(200, 196, 192, 200);
        HudStyle.NewImage(root, "TitleLineL", null, lineColor, new Vector2(-123f, TitleY + 0.5f), new Vector2(115f, 2f));
        HudStyle.NewImage(root, "TitleLineR", null, lineColor, new Vector2(122f, TitleY + 0.5f), new Vector2(115f, 2f));

        // ── 날짜 원 1~N + 연결선 ──
        float firstX = -(totalDays - 1) * CircleSpacing / 2f;
        for (int i = 0; i < totalDays - 1; i++)
        {
            float midX = firstX + CircleSpacing * (i + 0.5f);
            HudStyle.NewImage(root, $"Connector{i + 1}", null, connector, new Vector2(midX, CircleY), new Vector2(CircleSpacing - CircleSize, 2f));
        }

        dayBorders = new Image[totalDays];
        dayFills = new Image[totalDays];
        dayNumbers = new TextMeshProUGUI[totalDays];
        for (int i = 0; i < totalDays; i++)
        {
            Vector2 c = new Vector2(firstX + CircleSpacing * i, CircleY);
            dayBorders[i] = HudStyle.NewImage(root, $"Day{i + 1}", HudStyle.Circle, dayBorder, c, new Vector2(CircleSize, CircleSize));
            dayFills[i] = HudStyle.NewImage(dayBorders[i].transform, "Fill", HudStyle.Circle, dayFill, Vector2.zero, new Vector2(CircleSize - 5f, CircleSize - 5f));
            dayNumbers[i] = HudStyle.NewText(dayBorders[i].transform, "Num", (i + 1).ToString(), HudStyle.SemiBold, 19f, dayNumber,
                TextAlignmentOptions.Center, new Vector2(0f, 0.5f), new Vector2(CircleSize, CircleSize));
        }

        // ── 아래 줄: ☀ 12:51 │ 낮 │ 생존 목표 · 7일 ──
        phaseIcon = HudStyle.NewImage(root, "PhaseIcon", HudStyle.Sun, sunColor, new Vector2(-181.5f, BottomY), new Vector2(36f, 36f));
        clockText = HudStyle.NewText(root, "Clock", "12:51", HudStyle.Bold, 36f, clockColor,
            TextAlignmentOptions.Left, new Vector2(-138.5f + 50f, BottomY), new Vector2(100f, 40f));
        HudStyle.NewImage(root, "Divider1", null, dividerColor, new Vector2(-26.5f, BottomY), new Vector2(1.5f, 30f));
        phaseText = HudStyle.NewText(root, "Phase", "낮", HudStyle.SemiBold, 22f, dayPhaseColor,
            TextAlignmentOptions.Center, new Vector2(4f, BottomY), new Vector2(50f, 34f));
        weatherIcon = HudStyle.NewImage(root, "WeatherIcon", Resources.Load<Sprite>("HUD/Sprites/Weather_Clear"), Color.white,
            new Vector2(36f, BottomY), new Vector2(24f, 24f));
        weatherIcon.preserveAspect = true;
        HudStyle.NewImage(root, "Divider2", null, dividerColor, new Vector2(50.5f, BottomY), new Vector2(1.5f, 30f));
        goalText = HudStyle.NewText(root, "Goal", $"생존 목표 · {totalDays}일", HudStyle.Regular, 17f, Color.white,
            TextAlignmentOptions.Left, new Vector2(75.5f + 80f, BottomY), new Vector2(160f, 30f));
    }

    void Refresh(bool force)
    {
        var dnc = DayNightCycle.Instance;
        if (dnc == null || clockText == null) return;

        if (weatherIcon != null && (force || WeatherManager.Current != shownWeather))
        {
            shownWeather = WeatherManager.Current;
            weatherIcon.sprite = Resources.Load<Sprite>("HUD/Sprites/Weather_" + shownWeather);
        }

        var (hour, minute) = dnc.GetVirtualClock();
        clockText.text = $"{hour:00}:{minute:00}";

        int day = Mathf.Clamp(dnc.CurrentDay, 1, dayFills.Length);
        if (force || day != shownDay)
        {
            shownDay = day;
            titleText.text = $"생존 {day}일차";
            for (int i = 0; i < dayFills.Length; i++)
            {
                bool current = i + 1 == day;
                dayBorders[i].color = current ? currentDayFill : dayBorder;
                dayFills[i].color = current ? currentDayFill : dayFill;
                dayNumbers[i].color = current ? currentDayNumber : dayNumber;
                dayNumbers[i].font = current ? HudStyle.Bold : HudStyle.SemiBold;
            }
        }

        if (force || dnc.CurrentPhase != shownPhase)
        {
            shownPhase = dnc.CurrentPhase;
            bool dark = shownPhase == DayNightCycle.Phase.Night;
            phaseText.text = shownPhase switch
            {
                DayNightCycle.Phase.Day => "낮",
                DayNightCycle.Phase.Dusk => "노을",
                DayNightCycle.Phase.Night => "밤",
                _ => "새벽",
            };
            phaseText.color = dark ? nightPhaseColor : dayPhaseColor;
            phaseIcon.sprite = dark ? HudStyle.Moon : HudStyle.Sun;
            phaseIcon.color = dark ? nightPhaseColor : sunColor;
        }
    }
}
